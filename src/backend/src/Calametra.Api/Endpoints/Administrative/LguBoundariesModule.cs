using Calametra.Api.Extensions;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Administrative;
using Calametra.Application.Features.Earthquakes;

namespace Calametra.Api.Endpoints.Administrative;

/// <summary>
/// GET /api/lgu-boundaries/tile/{z}/{x}/{y} — one vector tile of current municipality outlines.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tiles, not a national payload.</b> 1,611 land outlines at full resolution are hundreds of megabytes of
/// coordinates; the source archive alone is 885 MB. A tile carries only its own extent and PostGIS builds it
/// in the database, so nothing is ever materialised into managed memory.
/// </para>
/// <para>
/// <b>Read-only and public, like the hazard tile proxy.</b> No authentication, and deliberately so: this
/// serves boundary geometry the platform is licensed to redistribute under CC BY 3.0 IGO, carrying no
/// personal data and no unreviewed identity. What it must never serve is the superseded OSM geometry retained
/// beside it, and that guarantee lives in the query rather than in the route.
/// </para>
/// </remarks>
internal static class GetLguBoundaryTileEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/tile/{z:int}/{x:int}/{y:int}", async (
                int z,
                int x,
                int y,
                IDispatcher dispatcher,
                HttpResponse response,
                CancellationToken cancellationToken) =>
            {
                var result = await dispatcher.Send(new GetLguBoundaryTile.Query(z, x, y), cancellationToken);

                if (result.IsFailure)
                {
                    return result.ToHttpResult();
                }

                // Administrative boundaries change by legislation, not by the hour, so a long cache is
                // honest rather than merely convenient. Immutability is not claimed: an import supersedes
                // geometry and the tiles then differ.
                response.Headers.CacheControl = "public, max-age=86400";

                var content = result.Value.Content;

                // An empty tile is the ordinary answer over water, and 204 says so without making the client
                // treat open sea as an error. MapLibre reads it as "nothing here".
                return content.Length == 0
                    ? Results.NoContent()
                    : Results.Bytes(content, "application/vnd.mapbox-vector-tile");
            })
            .WithName("GetLguBoundaryTile")
            .WithSummary("Vector tile of current city and municipality land outlines")
            .WithDescription(
                "Mapbox Vector Tile, layer name 'lgu', for zoom 6 to 14. Beyond 14 the client overzooms the "
                + "deepest tile rather than the server generating levels a municipal outline cannot usefully "
                + "fill. Below zoom 6 nothing is served, because ADR-005 D6 does not draw municipality "
                + "boundaries at national zoom — and a zoom-4 tile would be 189 KB of outlines nobody can "
                + "see.\n\n"
                + "**Land outlines.** OCHA COD-AB administrative boundaries, from NAMRIA and PSA sources "
                + "under CC BY 3.0 IGO. They stop at the coast and are NOT municipal-water jurisdiction: "
                + "Philippine LGUs do administer waters to 15 km offshore under RA 8550, but that is a "
                + "separate concept this platform does not store.\n\n"
                + "**Only geometry in force, only from the canonical source.** Superseded versions and the "
                + "retained OpenStreetMap outlines — which do extend to municipal waters — are never served "
                + "here.\n\n"
                + "Each feature's id is the canonical ten-digit PSGC, which is what makes hover and selection "
                + "survive tile loads and re-imports. Properties are the interaction's only: 'psgc', 'name' "
                + "as the register states it, 'kind', and 'area_km2'.\n\n"
                + "Coverage is 1,611 of 1,642 current cities and municipalities (98.11%); municipality-only "
                + "coverage is 1,462 of 1,493 (97.92%). 31 units have no outline and are known exceptions, "
                + "not gaps to be filled by guessing: 23 Maguindanao municipalities the PSA renumbered "
                + "without publishing a correspondence, and the 8 Bangsamoro Special Geographic Area "
                + "municipalities, which postdate this vintage.\n\n"
                + "204 No Content where a tile covers no outline, which over an archipelago is most of them.")
            .Produces(StatusCodes.Status200OK, contentType: "application/vnd.mapbox-vector-tile")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem();
}

/// <summary>
/// GET /api/lgu-boundaries — what the boundary set is and how much of the country it covers.
/// </summary>
/// <remarks>
/// Separate from the tiles because the client needs it once, and because a reader deserves the caveat before
/// they interpret an outline rather than after. It is what the layer panel renders beside the toggle.
/// </remarks>
internal static class GetLguBoundaryCoverageEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/", async (IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                var result = await dispatcher.Send(
                    new GetLguBoundaryCoverage.Query(),
                    cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("GetLguBoundaryMetadata")
            .WithSummary("Provenance, semantics and measured coverage of the boundary set")
            .WithDescription(
                "The acquisition chain — source, licence, vintage, SHA-256 — and coverage counted from the "
                + "active PSA register outwards, per level.\n\n"
                + "These are LAND outlines and must not be presented as maritime jurisdiction. Coverage is "
                + "not complete and is not rounded up: 31 units are known exceptions.")
            .Produces<object>(StatusCodes.Status200OK);
}

/// <summary>
/// GET /api/lgu-boundaries/{canonicalPsgcCode}/earthquakes â€” distinct earthquake epicentres covered by
/// one current COD-AB land outline.
/// </summary>
internal static class GetEarthquakeContainmentEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/{canonicalPsgcCode}/earthquakes", async (
                string canonicalPsgcCode,
                IDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                var result = await dispatcher.Send(
                    new GetEarthquakeContainment.Query(canonicalPsgcCode),
                    cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("GetEarthquakeContainment")
            .WithSummary("Earthquakes contained by one municipality or city land outline")
            .WithDescription(
                "Counts distinct real-world earthquake events whose canonical epicentres are covered by "
                + "the selected current COD-AB land polygon. Uses geography ST_Intersects, whose point-in-"
                + "polygon semantics include an epicentre exactly on the boundary. The count is not over "
                + "agency observation rows: one event "
                + "reported by both PHIVOLCS and USGS remains one event.\n\n"
                + "This is land containment, not proximity or maritime jurisdiction. Offshore earthquakes "
                + "are outside the answer and remain available through the existing radius tools. The "
                + "response includes computed boundary area and the dated boundary edition so the count cannot be read "
                + "without its spatial denominator and provenance.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
}

/// <summary>
/// GET /api/lgu-boundaries/{canonicalPsgcCode}/earthquakes/map — compact canonical events covered by
/// one current COD-AB land outline.
/// </summary>
internal static class GetContainedEarthquakeMapDataEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/{canonicalPsgcCode}/earthquakes/map", async (
                string canonicalPsgcCode,
                IDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                var result = await dispatcher.Send(
                    new GetContainedEarthquakeMapData.Query(canonicalPsgcCode),
                    cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("GetContainedEarthquakeMapData")
            .WithSummary("Compact earthquake map data contained by one LGU land outline")
            .WithDescription(
                "Returns the same terse map-point representation as /api/earthquakes/map, restricted "
                + "server-side to distinct canonical earthquake events whose canonical epicentres fall "
                + "within or exactly on the current COD-AB land boundary. It does not derive containment "
                + "from vector tiles or return agency observation rows. The response carries the computed "
                + "boundary geometry area, edition and attribution; this is not official land area.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
}

/// <summary>
/// Routes for the administrative boundary delivery path.
/// </summary>
internal static class LguBoundariesModule
{
    public static void MapLguBoundaries(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/lgu-boundaries")
            .WithTags("Administrative boundaries")
            .RequireRateLimiting(RateLimitPolicies.PublicRead);

        GetLguBoundaryCoverageEndpoint.Map(group);
        GetLguBoundaryTileEndpoint.Map(group);
        GetEarthquakeContainmentEndpoint.Map(group);
        GetContainedEarthquakeMapDataEndpoint.Map(group);
    }
}
