using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Calametra.Domain.Places;
using Calametra.Domain.Seismology;

namespace Calametra.Api.IntegrationTests;

/// <summary>
/// The place endpoints, against a real PostGIS instance.
/// </summary>
/// <remarks>
/// <para>
/// These are the tests that would have caught the defect that shipped during this feature's
/// development: <c>place.Centroid.Y</c> in a projection compiles, passes every unit test, and
/// returns HTTP 500 from PostgreSQL because <c>ST_Y</c> is not defined for <c>geography</c>. A
/// translation failure is invisible to anything that does not talk to the database.
/// </para>
/// <para>
/// Each test arranges its own places with coordinates chosen so the expected answer can be checked
/// by hand, and asserts through HTTP rather than against a handler — the query, the translation,
/// the serialisation and the route are all part of what can break.
/// </para>
/// </remarks>
[Collection(PostgisCollection.Name)]
public sealed class PlaceEndpointTests(PostgisApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task SearchingByName_ShouldReturnTheMatchWithItsContainingProvince()
    {
        await fixture.WithContextAsync(async context =>
        {
            var province = await TestData.AddPlaceAsync(
                context,
                "Province of Testonia",
                10.0d,
                125.0d,
                psgcCode: "990000000",
                kind: PlaceKind.Province);

            await TestData.AddPlaceAsync(
                context,
                "Marikaba City",
                10.1d,
                125.1d,
                psgcCode: "990101000",
                parent: province);
        });

        var matches = await GetAsync<List<PlaceMatchDto>>("/api/places?q=marikaba");

        var match = matches.ShouldHaveSingleItem();
        match.PsgcCode.ShouldBe("990101000");
        match.Kind.ShouldBe("City");

        // The container is what makes a result usable: 111 municipality names in this country are
        // shared, so a bare name can identify the wrong town.
        match.ContainedBy.ShouldBe("Province of Testonia");

        // Read from the materialised columns rather than through ST_Y, which does not exist for
        // geography. This assertion is the one that fails if that regresses.
        match.Latitude.ShouldBe(10.1d, tolerance: 0.0001d);
        match.Longitude.ShouldBe(125.1d, tolerance: 0.0001d);
    }

    [Fact]
    public async Task SearchingWithOneCharacter_ShouldBeRejected()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync(new Uri("/api/places?q=a", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AnUnknownCode_ShouldBeNotFound()
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/places/000000000/context", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ARadiusBeyondTheMeasuredCap_ShouldBeRejected()
    {
        // 300 km is not a preference. Past roughly 400 km the planner abandons the GiST bitmap
        // scan and geography ST_DWithin runs spheroid arithmetic over the whole table, which was
        // measured at 15-23 s against 0.13 s.
        using var client = fixture.CreateClient();

        var response = await client.GetAsync(
            new Uri("/api/places/990201000/context?radiusKm=301", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ThePlaceContext_ShouldCountOnlyWhatFallsInsideTheRadius()
    {
        await fixture.WithContextAsync(async context =>
        {
            await TestData.AddPlaceAsync(context, "Radiusville", 12.0d, 123.0d, psgcCode: "990301000");

            // 0.09° of latitude is almost exactly 10 km, so these sit either side of a 25 km ring
            // by a margin no rounding can close.
            await TestData.AddEarthquakeAsync(context, 12.09d, 123.0d, 5.0d, MagnitudeType.Mb, 30d);
            await TestData.AddEarthquakeAsync(context, 12.18d, 123.0d, 6.2d, MagnitudeType.Mb, 25d);
            await TestData.AddEarthquakeAsync(context, 13.0d, 123.0d, 5.5d, MagnitudeType.Mb, 40d);
        });

        var context25 = await GetAsync<PlaceContextDto>("/api/places/990301000/context?radiusKm=25");

        context25.EventsWithinRadius.ShouldBe(2);

        // The M6.0+ figure sits beside the raw count because detection improved 285-fold across
        // the last century while the M6.0+ rate stayed flat — it is the only count comparable
        // between eras.
        context25.EventsAtComparableMagnitude.ShouldBe(1);

        var context150 = await GetAsync<PlaceContextDto>("/api/places/990301000/context?radiusKm=150");

        context150.EventsWithinRadius.ShouldBe(3);
    }

    [Fact]
    public async Task ThePlaceContext_ShouldReportTheStrongestReadingPerScaleFamilyRatherThanOneLargest()
    {
        await fixture.WithContextAsync(async context =>
        {
            await TestData.AddPlaceAsync(context, "Scaleton", 8.0d, 124.0d, psgcCode: "990401000");

            // The real shape of the archive in miniature: many body-wave readings topping out
            // near where mb saturates, and one surface-wave reading with a larger number.
            await TestData.AddEarthquakeAsync(context, 8.05d, 124.0d, 5.4d, MagnitudeType.Mb, 30d);
            await TestData.AddEarthquakeAsync(context, 8.06d, 124.0d, 6.0d, MagnitudeType.Mb, 35d);
            await TestData.AddEarthquakeAsync(context, 8.07d, 124.0d, 6.7d, MagnitudeType.Ms, 10d);
        });

        var context = await GetAsync<PlaceContextDto>("/api/places/990401000/context?radiusKm=25");

        context.StrongestByScaleFamily.Count.ShouldBe(2);

        var bodyWave = context.StrongestByScaleFamily.Single(family => family.ScaleFamily == "BodyWave");
        var surfaceWave = context.StrongestByScaleFamily.Single(family => family.ScaleFamily == "SurfaceWave");

        bodyWave.ReadingsInFamily.ShouldBe(2);
        bodyWave.Event.MagnitudeDisplay.ShouldBe("Mb 6.0");

        surfaceWave.ReadingsInFamily.ShouldBe(1);
        surfaceWave.Event.MagnitudeDisplay.ShouldBe("Ms 6.7");

        // The whole point: the largest number belongs to the family with the fewest readings, and
        // nothing in the response ranks the two against each other.
        bodyWave.ReadingsInFamily.ShouldBeGreaterThan(surfaceWave.ReadingsInFamily);
    }

    [Fact]
    public async Task ThePlaceContext_ShouldReportDepthsTheAgencyAssignedRatherThanMeasured()
    {
        await fixture.WithContextAsync(async context =>
        {
            await TestData.AddPlaceAsync(context, "Depthford", 6.5d, 125.5d, psgcCode: "990501000");

            await TestData.AddEarthquakeAsync(context, 6.55d, 125.5d, 5.0d, MagnitudeType.Mb, 42.3d);
            await TestData.AddEarthquakeAsync(
                context,
                6.56d,
                125.5d,
                5.1d,
                MagnitudeType.Mb,
                33d,
                DepthQuality.OperatorAssigned);
        });

        var context = await GetAsync<PlaceContextDto>("/api/places/990501000/context?radiusKm=25");

        context.EventsWithinRadius.ShouldBe(2);
        context.EventsWithAssignedDepth.ShouldBe(1);

        // Stated in the response rather than left for the client to word, because it is a claim
        // about the data.
        context.Notes.ShouldContain(note => note.Contains("assigned rather", StringComparison.Ordinal));
    }

    private async Task<T> GetAsync<T>(string path)
    {
        using var client = fixture.CreateClient();

        var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            $"GET {path} returned {(int)response.StatusCode}: "
            + await response.Content.ReadAsStringAsync());

        var payload = await response.Content.ReadFromJsonAsync<T>(Json);

        return payload ?? throw new InvalidOperationException($"GET {path} returned no body.");
    }

    // Local shapes rather than the Application's response records: a test that deserialises into
    // the same type the server serialises from cannot detect a field being renamed.
    private sealed record PlaceMatchDto(
        [property: JsonPropertyName("psgcCode")] string? PsgcCode,
        string Name,
        string Kind,
        string? ContainedBy,
        string? Region,
        double Latitude,
        double Longitude);

    private sealed record PlaceContextDto(
        string Name,
        double RadiusKm,
        int EventsWithinRadius,
        int EventsAtComparableMagnitude,
        int EventsWithAssignedDepth,
        List<StrongestDto> StrongestByScaleFamily,
        List<string> Notes);

    private sealed record StrongestDto(string ScaleFamily, int ReadingsInFamily, NearbyDto Event);

    private sealed record NearbyDto(string MagnitudeDisplay, string DepthDisplay, double DistanceKm);
}
