using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Calametra.Domain.Places;
using Calametra.Domain.Seismology;

namespace Calametra.Api.IntegrationTests;

/// <summary>
/// Epicentre naming and similarity, against a real PostGIS instance.
/// </summary>
/// <remarks>
/// The two claims worth testing here are both refusals, and both are invisible to a unit test
/// because they depend on what the database returns: an epicentre far from any settlement is not
/// given a place name, and a reading on one magnitude scale is not compared with a reading on
/// another.
/// </remarks>
[Collection(PostgisCollection.Name)]
public sealed class EarthquakeLocationTests(PostgisApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task AnEpicentre_ShouldBeNamedAfterTheNearestCityWithItsDirection()
    {
        Guid eventId = default;

        await fixture.WithContextAsync(async context =>
        {
            var province = await TestData.AddPlaceAsync(
                context,
                "Province of Bearingshire",
                14.0d,
                121.0d,
                psgcCode: "980000000",
                kind: PlaceKind.Province);

            await TestData.AddPlaceAsync(
                context,
                "Northtown",
                14.0d,
                121.0d,
                psgcCode: "980101000",
                parent: province);

            // A quarter of a degree due north is about 27.8 km, so the expected phrase can be
            // computed by hand: "28 km N of Northtown".
            var earthquake = await TestData.AddEarthquakeAsync(
                context,
                14.25d,
                121.0d,
                5.6d,
                MagnitudeType.Mb,
                40d,
                externalId: "naming-north");

            eventId = earthquake.Id;
        });

        var detail = await GetAsync<DetailDto>($"/api/earthquakes/{eventId}");

        detail.Location.ShouldBe("28 km N of Northtown, Province of Bearingshire");
    }

    [Fact]
    public async Task AnEpicentreBeyondTheGazetteersReach_ShouldNotBeNamed()
    {
        // Measured against the live archive: the nearest city or municipality is within 300 km for
        // 27,175 of 27,241 events, and beyond it for 66. For those a town name would describe the
        // reach of the directory rather than the position of the earthquake.
        Guid eventId = default;

        await fixture.WithContextAsync(async context =>
        {
            // 2°N 140°E, in the deep Pacific. Every place these tests arrange is inside the
            // archipelago, which spans 4.7-20.8°N and 114-127°E, so this position is more than a
            // thousand kilometres from any of them by construction.
            //
            // That reasoning is load-bearing because the suite shares one database: the first
            // version of this test sat 99 km from a city another test class had added, and so
            // passed or failed depending on execution order.
            var earthquake = await TestData.AddEarthquakeAsync(
                context,
                2.0d,
                140.0d,
                6.1d,
                MagnitudeType.Mww,
                20d,
                externalId: "naming-far");

            eventId = earthquake.Id;
        });

        var detail = await GetAsync<DetailDto>($"/api/earthquakes/{eventId}");

        detail.Location.ShouldBeNull();

        // The coordinates are still there. Refusing to name a place is not refusing to say where.
        detail.Latitude.ShouldBe(2.0d, tolerance: 0.0001d);
    }

    [Fact]
    public async Task SimilarEvents_ShouldSetAsideNeighboursOnAnIncomparableScale()
    {
        Guid referenceId = default;

        await fixture.WithContextAsync(async context =>
        {
            // A surface-wave reference, which is the position the platform's flagship event is in:
            // PHIVOLCS reports 2017 Surigao as Ms 6.7, and 173 of 27,242 observations use Ms.
            var reference = await TestData.AddEarthquakeAsync(
                context,
                9.0d,
                126.0d,
                6.7d,
                MagnitudeType.Ms,
                10d,
                externalId: "similar-reference",
                sourceSlug: "test-authority");

            referenceId = reference.Id;

            // One comparable neighbour and two on a scale that cannot be differenced against it.
            await TestData.AddEarthquakeAsync(
                context, 9.2d, 126.0d, 6.5d, MagnitudeType.Ms, 12d, externalId: "similar-ms");
            await TestData.AddEarthquakeAsync(
                context, 9.1d, 126.1d, 6.6d, MagnitudeType.Mb, 15d, externalId: "similar-mb-1");
            await TestData.AddEarthquakeAsync(
                context, 9.15d, 126.05d, 6.4d, MagnitudeType.Mb, 18d, externalId: "similar-mb-2");
        });

        var result = await GetAsync<SimilarDto>(
            $"/api/earthquakes/{referenceId}/similar?maxDistanceKm=150");

        result.NearbyEvents.ShouldBe(3);

        // Body-wave magnitude saturates near M6 and is a different quantity from surface-wave
        // magnitude. Both Mb neighbours are counted as set aside rather than compared, even though
        // one of them carries a larger number than the reference.
        result.ExcludedForIncomparableScale.ShouldBe(2);
        result.CandidatesConsidered.ShouldBe(1);

        var match = result.Matches.ShouldHaveSingleItem();
        match.MagnitudeDisplay.ShouldBe("Ms 6.5");
        match.MagnitudeComparable.ShouldBeTrue();

        // The match is named, which is what the place directory added to this endpoint.
        match.Location.ShouldNotBeNull();
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

    private sealed record DetailDto(
        Guid Id,
        double Latitude,
        double Longitude,
        string? Location);

    private sealed record SimilarDto(
        int NearbyEvents,
        int ExcludedForIncomparableScale,
        int CandidatesConsidered,
        List<MatchDto> Matches);

    private sealed record MatchDto(
        string MagnitudeDisplay,
        string? Location,
        bool MagnitudeComparable,
        double Score);
}
