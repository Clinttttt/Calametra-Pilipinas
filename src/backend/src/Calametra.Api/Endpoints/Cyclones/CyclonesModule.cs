using Calametra.Api.Extensions;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Cyclones;

namespace Calametra.Api.Endpoints.Cyclones;

/// <summary>
/// GET /api/cyclones — storms whose track entered the Philippine area.
/// </summary>
internal static class SearchCyclonesEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/", async (
                IDispatcher dispatcher,
                CancellationToken cancellationToken,
                int? season = null,
                string? name = null,
                bool landfallOnly = false,
                SearchCyclones.CycloneOrder order = SearchCyclones.CycloneOrder.Intensity,
                int limit = 60) =>
            {
                var query = new SearchCyclones.Query
                {
                    Season = season,
                    Name = name,
                    LandfallOnly = landfallOnly,
                    Order = order,
                    Limit = limit,
                };

                var result = await dispatcher.Send(query, cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("SearchCyclones")
            .WithSummary("Tropical cyclones that entered the Philippine area")
            .WithDescription(
                "Ordered by intensity — lowest central pressure — by default rather than by date. "
                + "The most recent storms carry only one agency's provisional track, since the "
                + "others publish their reanalyses a season or more later, so a recency-ordered "
                + "default would put the thinnest records first and bury every storm with a "
                + "multi-agency comparison.\n\n"
                + "Pressure orders the list, not wind. A maximum wind taken across agencies would "
                + "rank whichever one uses the shortest averaging interval; minimum central "
                + "pressure is the same quantity to every agency and is the only intensity "
                + "measure that can legitimately be compared across them.\n\n"
                + "Peak intensity is returned once per wind averaging period, never as a single "
                + "figure.\n\n"
                + "Agencies average sustained wind over different intervals — one minute for "
                + "JTWC, ten for JMA and Hong Kong, two for CMA — and a shorter interval "
                + "preserves brief peaks that a longer one smooths away. Taking the maximum "
                + "across all agencies would therefore report whichever agency uses the shortest "
                + "interval and present it as the storm's strength. For Haiyan that is 170 kt "
                + "from JTWC, while the regional specialised centre for this basin published "
                + "125 kt. Both are correct.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem();
}

/// <summary>
/// GET /api/cyclones/{eventId} — one storm's track, as each agency drew it.
/// </summary>
internal static class GetCycloneTrackEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/{eventId:guid}", async (
                Guid eventId,
                IDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                var query = new GetCycloneTrack.Query { EventId = eventId };

                var result = await dispatcher.Send(query, cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("GetCycloneTrack")
            .WithSummary("One cyclone's track, per agency")
            .WithDescription(
                "Returns one track per agency rather than an averaged path. Agencies differ on "
                + "where the centre was as well as how strong it was, and a mean track would be "
                + "a line no agency published.\n\n"
                + "Each track states its averaging period once, because that is a property of the "
                + "agency's method and cannot vary along the path. The response also carries a "
                + "plain-language note on how far the agencies diverge, generated from the "
                + "readings actually present — it distinguishes agencies that share an interval "
                + "and genuinely disagree from agencies that are not measuring the same thing.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
}

/// <summary>
/// GET /api/cyclones/external/{externalStormId} — one storm by the source archive's identifier.
/// </summary>
internal static class GetCycloneByExternalIdEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/external/{externalStormId}", async (
                string externalStormId,
                IDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                var query = new GetCycloneByExternalId.Query { ExternalStormId = externalStormId };

                var result = await dispatcher.Send(query, cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("GetCycloneByExternalId")
            .WithSummary("One cyclone's track, by the IBTrACS storm identifier")
            .WithDescription(
                "Resolves an IBTrACS SID — 2013306N07162 for Haiyan — to the storm in this archive, "
                + "returning the same per-agency tracks as /api/cyclones/{eventId}.\n\n"
                + "Exists because this platform's primary keys are minted at insert and the IBTrACS "
                + "import is a re-runnable one-shot, so a storm's internal id changes whenever the "
                + "basin file is re-imported. Anything durable — curated story content, a citation, "
                + "a link shared with another institution — must reference the SID instead.\n\n"
                + "The SID rather than name and season because international names are reused: this "
                + "archive holds MERANTI in 2010 and 2016, GONI in 2015 and 2020, and MAWAR in "
                + "2012, 2017 and 2023.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
}

/// <summary>
/// GET /api/cyclones/decades — storm counts by decade, for era comparison.
/// </summary>
internal static class GetCycloneDecadesEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/decades", async (
                IDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                var result = await dispatcher.Send(new GetCycloneDecades.Query(), cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("GetCycloneDecades")
            .WithSummary("Storm counts by decade")
            .WithDescription(
                "Returns storms per decade with the landfalling count beside the total, plus the "
                + "season from which JTWC rates its own best-track record as high quality. The "
                + "landfalling count is the more era-comparable series: coverage of storms at sea "
                + "depended on what could be observed, while a storm that crossed the coast was "
                + "recorded by the people it crossed.")
            .Produces<object>(StatusCodes.Status200OK);
}

/// <summary>
/// GET /api/cyclones/nearby — the portions of storm tracks that passed near a point.
/// </summary>
internal static class GetCycloneTracksNearbyEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/nearby", async (
                double latitude,
                double longitude,
                IDispatcher dispatcher,
                CancellationToken cancellationToken,
                double radiusKm = 100d,
                int limit = 150) =>
            {
                var query = new GetCycloneTracksNearby.Query
                {
                    Latitude = latitude,
                    Longitude = longitude,
                    RadiusKm = radiusKm,
                    Limit = limit,
                };

                var result = await dispatcher.Send(query, cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("GetCycloneTracksNearby")
            .WithSummary("Storm track segments passing near a point")
            .WithDescription(
                "Returns each nearby storm's track cut to the fixes near the point, not its whole "
                + "path. A place with 91 storms within 100 km has some fifteen thousand fixes "
                + "spanning the basin from Micronesia to the Chinese coast; drawn in full they "
                + "answer where storms in this basin go, while the question asked here is how they "
                + "passed this place.\n\n"
                + "Segments extend to a wider radius than the one that selects the storms — "
                + "reported as drawnRadiusKm — because a line cut exactly at the ring looks like a "
                + "storm that began and ended there. The margin lets each track visibly enter and "
                + "leave, which is what makes its direction of travel readable.\n\n"
                + "One agency per storm, chosen as the one with the most fixes near the point on "
                + "the ground that it has the finest spatial resolution here. That is a choice, "
                + "not a claim of authority: agencies disagree on where the centre was, and the "
                + "per-agency comparison is at /api/cyclones/{eventId}.\n\n"
                + "peakKnotsNearby is the strongest reading within the returned segment, with the "
                + "averaging period that produced it. It is not the storm's peak intensity — for a "
                + "storm that passed early and intensified later the difference is large.\n\n"
                + "stormCount is the full number of storms within the radius and matches the place "
                + "context for the same point; it exceeds the number of tracks returned when the "
                + "limit bit, so a caller can state the truncation rather than hide it.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem();
}

/// <summary>Route group for the cyclone endpoints.</summary>
internal static class CyclonesModule
{
    public static void MapCyclones(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/cyclones")
            .WithTags("Cyclones")
            .RequireRateLimiting(RateLimitPolicies.PublicRead);

        SearchCyclonesEndpoint.Map(group);
        GetCycloneDecadesEndpoint.Map(group);
        GetCycloneTracksNearbyEndpoint.Map(group);

        // Literal route before the {eventId:guid} route for readability. Ordering is not
        // load-bearing: the guid constraint means "external" cannot match it.
        GetCycloneByExternalIdEndpoint.Map(group);
        GetCycloneTrackEndpoint.Map(group);
    }
}
