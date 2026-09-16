using System.Net;

using Calametra.Domain.Administrative;
using Calametra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Shouldly;

namespace Calametra.Api.IntegrationTests;

/// <summary>
/// What the boundary delivery path is allowed to serve, against a real PostGIS.
/// </summary>
/// <remarks>
/// <para>
/// These assertions are about a guarantee rather than a shape. ADR-005 D2a forbids serving the canonical
/// land outlines and the retained OpenStreetMap municipal-water outlines as one spatial concept, and the
/// difference is not subtle: measured across the units held from both sources, OSM areas are a median 1.66
/// times the land figure and ten thousand times for Kalayaan. A tile mixing them would hand a reader two
/// meanings of the word boundary with nothing to tell them apart.
/// </para>
/// <para>
/// The enforcement lives in the tiling view, not in the endpoint, so it is tested through the endpoint —
/// which is the only place a defect would actually reach a reader.
/// </para>
/// </remarks>
[Collection(PostgisCollection.Name)]
public sealed class LguBoundaryTileTests(PostgisApiFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    [Fact]
    public async Task Serves_only_geometry_in_force_from_the_canonical_land_source()
    {
        await ArrangeAsync();

        // The tiling view is what the tiles read, and it is where D2a is enforced. Asserting on it directly
        // says which rows could ever be served, which is a stronger claim than any one tile's contents.
        await fixture.WithContextAsync(async context =>
        {
            var served = await context.Database
                .SqlQueryRaw<string>("SELECT psgc AS \"Value\" FROM lgu_boundary_tile_source ORDER BY psgc")
                .ToListAsync();

            // The superseded outline and the OpenStreetMap one are both absent, and the current land
            // outline is present.
            served.ShouldContain("0102801000");
            served.ShouldNotContain("0102802000");
            served.ShouldNotContain("0102803000");
        });
    }

    [Fact]
    public async Task A_tile_over_the_arranged_unit_is_a_vector_tile()
    {
        await ArrangeAsync();

        using var client = fixture.Api.CreateClient();

        // Zoom 8 over northern Luzon, where the arranged outline sits.
        var response = await client.GetAsync(new Uri("/api/lgu-boundaries/tile/8/213/114", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/vnd.mapbox-vector-tile");

        var bytes = await response.Content.ReadAsByteArrayAsync();

        bytes.Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Answers_no_content_where_no_outline_falls()
    {
        await ArrangeAsync();

        using var client = fixture.Api.CreateClient();

        // Open Pacific. Over an archipelago most tiles are empty, and 204 says so without making the
        // client treat open sea as a fault.
        var response = await client.GetAsync(new Uri("/api/lgu-boundaries/tile/8/230/120", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Refuses_zooms_it_does_not_draw()
    {
        using var client = fixture.Api.CreateClient();

        // ADR-005 D6 draws no municipality boundaries nationally, and a zoom-4 tile would be 189 KB of
        // outlines nobody can see. A band the design does not draw is not requestable.
        var national = await client.GetAsync(new Uri("/api/lgu-boundaries/tile/4/13/7", UriKind.Relative));

        national.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var tooDeep = await client.GetAsync(
            new Uri("/api/lgu-boundaries/tile/16/54789/30089", UriKind.Relative));

        tooDeep.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Refuses_a_coordinate_outside_the_pyramid()
    {
        using var client = fixture.Api.CreateClient();

        // 2^8 is 256, so x=999 is not a tile. A client bug rather than a query.
        var response = await client.GetAsync(new Uri("/api/lgu-boundaries/tile/8/999/114", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Keys_features_to_the_canonical_psgc_so_selection_survives_reimport()
    {
        await ArrangeAsync();

        await fixture.WithContextAsync(async context =>
        {
            // The feature id is the code as a bigint, and the code as text travels beside it. Both are
            // needed: MapLibre's feature-state requires an integer, while the leading zero every Luzon code
            // carries only survives in the string.
            var ids = await context.Database
                .SqlQueryRaw<long>("SELECT fid AS \"Value\" FROM lgu_boundary_tile_source ORDER BY fid")
                .ToListAsync();

            ids.ShouldContain(102801000L);
        });
    }

    [Fact]
    public async Task Does_not_invent_geometry_for_a_unit_that_has_none()
    {
        await ArrangeAsync();

        await fixture.WithContextAsync(async context =>
        {
            // The unit exists in the register and has no outline. ADR-005 D5 makes that valid rather than
            // broken, and the delivery path must not paper over it — 31 units are in exactly this state in
            // production, and a fabricated outline would be indistinguishable from a real one.
            var served = await context.Database
                .SqlQueryRaw<string>("SELECT psgc AS \"Value\" FROM lgu_boundary_tile_source")
                .ToListAsync();

            served.ShouldNotContain("0102804000");
        });
    }

    /// <summary>
    /// One register edition, four units: one with a current land outline, one with a superseded one, one
    /// with an OpenStreetMap outline, and one with none at all.
    /// </summary>
    private async Task ArrangeAsync() =>
        await fixture.WithContextAsync(async context =>
        {
            if (await context.Lgus.AnyAsync(lgu => lgu.CanonicalPsgcCode == "0102801000"))
            {
                return;
            }

            var source = await SeedSourceAsync(context);

            var edition = PsgcRegisterEdition.Create(
                "PSGC test edition",
                RegisterProvenance.PsaDirect,
                "test",
                source,
                Now,
                Now).Value;

            context.PsgcRegisterEditions.Add(edition);

            var extract = LguBoundaryExtract.Create(
                source,
                "COD-AB test extract",
                BoundaryProvenance.OchaCodAb,
                "test",
                Now).Value;

            context.LguBoundaryExtracts.Add(extract);

            var served = Unit(context, edition.Id, "0102801000", "Adams");
            var supersededUnit = Unit(context, edition.Id, "0102802000", "Bacarra");
            var osmUnit = Unit(context, edition.Id, "0102803000", "Badoc");

            // A unit with no outline at all, standing for the 31 known exceptions.
            Unit(context, edition.Id, "0102804000", "Bangui");

            await context.SaveChangesAsync();

            context.LguBoundaries.Add(Boundary(
                served.Id, "0102801000", source, extract.Id, sourceFeatureCode: "0102801000"));

            var retired = Boundary(
                supersededUnit.Id, "0102802000", source, extract.Id, sourceFeatureCode: "0102802000");

            retired.Supersede(Guid.CreateVersion7(), Now);

            context.LguBoundaries.Add(retired);

            // An OpenStreetMap outline: in force, but jurisdictional rather than land, so it must never be
            // served through this path.
            context.LguBoundaries.Add(Boundary(
                osmUnit.Id, "0102803000", source, extract.Id, sourceFeatureCode: null, osmRelationId: 12345L));

            await context.SaveChangesAsync();

            await context.Database.ExecuteSqlRawAsync(
                "REFRESH MATERIALIZED VIEW lgu_boundary_tile_source");
        });

    private static async Task<Guid> SeedSourceAsync(ApplicationDbContext context)
    {
        var source = await TestData.EnsureSourceAsync(context, "ocha-cod-ab-phl");

        return source.Id;
    }

    private static Lgu Unit(ApplicationDbContext context, Guid editionId, string code, string name)
    {
        var unit = Lgu.Create(code, name, LguLevel.Municipality, editionId, Now).Value;

        context.Lgus.Add(unit);

        return unit;
    }

    private static LguBoundary Boundary(
        Guid lguId,
        string code,
        Guid sourceId,
        Guid extractId,
        string? sourceFeatureCode,
        long? osmRelationId = null) =>
        LguBoundary.Record(
            lguId,
            code,
            sourceId,
            extractId,
            osmRelationId,
            sourceFeatureCode,
            code,
            "test",
            0,
            Square(),
            159d,
            Now,
            null,
            false,
            null,
            Now).Value;

    /// <summary>A small square in northern Luzon, so the arranged tile has something in it.</summary>
    private static MultiPolygon Square()
    {
        var ring = Factory.CreateLinearRing(
        [
            new Coordinate(120.90, 18.40),
            new Coordinate(121.00, 18.40),
            new Coordinate(121.00, 18.50),
            new Coordinate(120.90, 18.50),
            new Coordinate(120.90, 18.40),
        ]);

        return Factory.CreateMultiPolygon([Factory.CreatePolygon(ring)]);
    }
}
