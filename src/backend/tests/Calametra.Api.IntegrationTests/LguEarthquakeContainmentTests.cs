using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Calametra.Application.Features.Earthquakes;
using Calametra.Domain.Administrative;
using Calametra.Domain.Events;
using Calametra.Domain.Geospatial;
using Calametra.Domain.Seismology;
using Calametra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace Calametra.Api.IntegrationTests;

/// <summary>The first analytical use of the current COD-AB land boundary.</summary>
[Collection(PostgisCollection.Name)]
public sealed class LguEarthquakeContainmentTests(PostgisApiFixture fixture)
{
    private const string CoveredCode = "9900000001";
    private const string MissingCode = "9900000002";
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    [Fact]
    public async Task Counts_distinct_events_including_an_epicentre_exactly_on_the_boundary()
    {
        await ArrangeAsync();

        using var client = fixture.CreateClient();
        var response = await client.GetAsync($"/api/lgu-boundaries/{CoveredCode}/earthquakes");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<GetEarthquakeContainment.Response>();

        body.ShouldNotBeNull();
        body.CanonicalPsgcCode.ShouldBe(CoveredCode);
        body.EarthquakeCount.ShouldBe(2);
        body.BoundaryGeometryAreaSquareKm.ShouldBe(100d);
        body.SpatialPredicate.ShouldBe("ST_Intersects");
        body.CountSemantics.ShouldContain("Points exactly on the boundary are included");
        body.CountSemantics.ShouldContain("observation rows are not counted");
        body.Boundary.Label.ShouldBe("COD-AB containment test extract");
    }

    [Fact]
    public async Task Compact_map_events_exactly_match_the_containment_count_and_semantics()
    {
        await ArrangeAsync();

        using var client = fixture.CreateClient();
        var count = await client.GetFromJsonAsync<GetEarthquakeContainment.Response>(
            $"/api/lgu-boundaries/{CoveredCode}/earthquakes");
        var map = await client.GetFromJsonAsync<GetContainedEarthquakeMapData.Response>(
            $"/api/lgu-boundaries/{CoveredCode}/earthquakes/map");

        count.ShouldNotBeNull();
        map.ShouldNotBeNull();
        map.Count.ShouldBe(count.EarthquakeCount);
        map.Points.Count.ShouldBe(map.Count);
        map.Count.ShouldBe(2);
        map.BoundaryGeometryAreaSquareKm.ShouldBe(100d);
        map.CountSemantics.ShouldBe(count.CountSemantics);

        // The western-edge point is included, the outside point is absent, and the cyclone has no map
        // record. The inside event's two agency observations remain one point with its disclosure flag.
        map.Points.ShouldContain(point => point.X == 100.00 && point.Y == 1.05);
        map.Points.ShouldContain(point => point.X == 100.05 && point.Y == 1.05 && point.N);
        map.Points.ShouldNotContain(point => point.X == 100.20 && point.Y == 1.20);
    }

    [Fact]
    public async Task A_known_unit_without_a_boundary_is_not_given_a_fabricated_answer()
    {
        await ArrangeAsync();

        using var client = fixture.CreateClient();
        var response = await client.GetAsync($"/api/lgu-boundaries/{MissingCode}/earthquakes");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().ShouldBe("lgu_boundary.not_available");

        var mapResponse = await client.GetAsync(
            $"/api/lgu-boundaries/{MissingCode}/earthquakes/map");

        mapResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var mapProblem = JsonDocument.Parse(await mapResponse.Content.ReadAsStringAsync());
        mapProblem.RootElement.GetProperty("code").GetString().ShouldBe("lgu_boundary.not_available");
    }

    [Fact]
    public async Task Rejects_a_noncanonical_code()
    {
        using var client = fixture.CreateClient();
        var response = await client.GetAsync("/api/lgu-boundaries/123/earthquakes");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var mapResponse = await client.GetAsync("/api/lgu-boundaries/123/earthquakes/map");
        mapResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_lgu_is_not_found_for_count_and_map()
    {
        using var client = fixture.CreateClient();

        (await client.GetAsync("/api/lgu-boundaries/9999999999/earthquakes"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/lgu-boundaries/9999999999/earthquakes/map"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task ArrangeAsync() =>
        await fixture.WithContextAsync(async context =>
        {
            if (await context.Lgus.AnyAsync(lgu => lgu.CanonicalPsgcCode == CoveredCode))
            {
                return;
            }

            var boundarySource = await TestData.EnsureSourceAsync(context, "containment-cod-ab");
            var earthquakeSource = await TestData.EnsureSourceAsync(context, "containment-earthquakes");
            var secondAgency = await TestData.EnsureSourceAsync(context, "containment-second-agency");

            // The public source catalogue requires every registered source to state its coverage. These
            // rows share the integration database with that endpoint's tests, so arrange complete source
            // records rather than relying on test order to hide incomplete ones.
            boundarySource.WithCoverage(null, "Synthetic COD-AB boundary used only by integration tests.");
            earthquakeSource.WithCoverage(null, "Synthetic earthquake catalogue used only by integration tests.");
            secondAgency.WithCoverage(null, "Second synthetic agency used to verify event de-duplication.");

            var edition = PsgcRegisterEdition.Create(
                "Containment test PSGC edition",
                RegisterProvenance.PsaDirect,
                "test",
                boundarySource.Id,
                Now,
                Now).Value;

            context.PsgcRegisterEditions.Add(edition);

            var covered = Lgu.Create(
                CoveredCode,
                "Boundary Test Municipality",
                LguLevel.Municipality,
                edition.Id,
                Now).Value;

            var missing = Lgu.Create(
                MissingCode,
                "Known Missing Municipality",
                LguLevel.Municipality,
                edition.Id,
                Now).Value;

            context.Lgus.AddRange(covered, missing);

            var extract = LguBoundaryExtract.Create(
                boundarySource.Id,
                "COD-AB containment test extract",
                BoundaryProvenance.OchaCodAb,
                "test",
                Now).Value;

            context.LguBoundaryExtracts.Add(extract);
            await context.SaveChangesAsync();

            context.LguBoundaries.Add(LguBoundary.Record(
                covered.Id,
                CoveredCode,
                boundarySource.Id,
                extract.Id,
                null,
                CoveredCode,
                CoveredCode,
                covered.Name,
                0,
                Square(),
                100d,
                Now,
                "test",
                false,
                null,
                Now).Value);

            // One event strictly inside and one exactly on the western edge. ST_Contains would discard the
            // second; the chosen point/polygon ST_Intersects predicate must retain it.
            var inside = Earthquake(earthquakeSource.Id, "containment-inside", 1.05, 100.05);
            var boundary = Earthquake(earthquakeSource.Id, "containment-edge", 1.05, 100.00);

            // A second agency reading of the inside event. The endpoint must still count one HazardEvent.
            inside.AddObservation(
                secondAgency.Id,
                "containment-inside-agency-two",
                Now,
                Wgs84.Point(1.05, 100.05),
                new DepthReading(12d, DepthQuality.Constrained),
                new MagnitudeReading(5.1d, MagnitudeType.Mw),
                Now);

            var outside = Earthquake(earthquakeSource.Id, "containment-outside", 1.20, 100.20);
            var cyclone = HazardEvent.Create(
                HazardEventType.TropicalCyclone,
                Now,
                Wgs84.Point(1.05, 100.05),
                Now,
                "NOT-AN-EARTHQUAKE").Value;

            context.HazardEvents.AddRange(inside, boundary, outside, cyclone);
            await context.SaveChangesAsync();
        });

    private static HazardEvent Earthquake(Guid sourceId, string externalId, double latitude, double longitude)
    {
        var point = Wgs84.Point(latitude, longitude);
        var earthquake = HazardEvent.Create(HazardEventType.Earthquake, Now, point, Now).Value;

        earthquake.AddObservation(
            sourceId,
            externalId,
            Now,
            point,
            new DepthReading(10d, DepthQuality.Constrained),
            new MagnitudeReading(5d, MagnitudeType.Mw),
            Now);

        return earthquake;
    }

    private static MultiPolygon Square()
    {
        var ring = Factory.CreateLinearRing(
        [
            new Coordinate(100.00, 1.00),
            new Coordinate(100.10, 1.00),
            new Coordinate(100.10, 1.10),
            new Coordinate(100.00, 1.10),
            new Coordinate(100.00, 1.00),
        ]);

        return Factory.CreateMultiPolygon([Factory.CreatePolygon(ring)]);
    }
}
