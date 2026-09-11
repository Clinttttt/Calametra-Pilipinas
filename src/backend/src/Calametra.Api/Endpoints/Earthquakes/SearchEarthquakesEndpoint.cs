using Calametra.Api.Extensions;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Earthquakes;
using Calametra.Domain.Seismology;

namespace Calametra.Api.Endpoints.Earthquakes;

/// <summary>
/// GET /api/earthquakes — the archive search that backs the map, the timeline, the
/// place-history panel and the cross-section.
/// </summary>
/// <remarks>
/// Mirrors <c>Application/Features/Earthquakes/SearchEarthquakes.cs</c> one-to-one,
/// as the architecture requires. Binding and HTTP shaping happen here; nothing else.
/// </remarks>
internal static class SearchEarthquakesEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/", async (
                IDispatcher dispatcher,
                CancellationToken cancellationToken,
                DateTimeOffset? from = null,
                DateTimeOffset? to = null,
                double? minMagnitude = null,
                double? maxMagnitude = null,
                double? minDepthKm = null,
                double? maxDepthKm = null,
                MagnitudeScaleFamily? scaleFamily = null,
                bool includeAssignedDepths = true,
                double? centreLatitude = null,
                double? centreLongitude = null,
                double? radiusKm = null,
                int page = 1,
                int pageSize = 100) =>
            {
                var query = new SearchEarthquakes.Query
                {
                    From = from,
                    To = to,
                    MinMagnitude = minMagnitude,
                    MaxMagnitude = maxMagnitude,
                    MinDepthKm = minDepthKm,
                    MaxDepthKm = maxDepthKm,
                    ScaleFamily = scaleFamily,
                    IncludeAssignedDepths = includeAssignedDepths,
                    CentreLatitude = centreLatitude,
                    CentreLongitude = centreLongitude,
                    RadiusKm = radiusKm,
                    Page = page,
                    PageSize = pageSize,
                };

                var result = await dispatcher.Send(query, cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("SearchEarthquakes")
            .WithSummary("Search the earthquake archive")
            .WithDescription(
                "Filters by time, magnitude, depth and geography. Every result carries the reporting "
                + "agency and the magnitude scale, because a magnitude without its scale is not "
                + "comparable. Set includeAssignedDepths=false to exclude events whose depth was fixed "
                + "to an agency default rather than measured — roughly 43% of the USGS Philippine "
                + "catalogue — which is required for depth analysis such as the cross-section.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem();
}

/// <summary>
/// GET /api/earthquakes/{id} — one event with every agency's reading.
/// </summary>
internal static class GetEarthquakeDetailEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/{eventId:guid}", async (
                Guid eventId,
                IDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                var result = await dispatcher.Send(
                    new GetEarthquakeDetail.Query { EventId = eventId },
                    cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("GetEarthquakeDetail")
            .WithSummary("One earthquake, with every agency reading")
            .WithDescription(
                "Returns all observations of an event rather than a single reduced figure. The same "
                + "earthquake is reported differently by different networks — the 2017 Surigao event is "
                + "Ms 6.7 at 10 km to PHIVOLCS and Mww 6.5 at 15 km to USGS — so the response also "
                + "explains why the values differ.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
}

/// <summary>
/// GET /api/earthquakes/external/{externalEventId} — one earthquake by the agency's own identifier.
/// </summary>
internal static class GetEarthquakeByExternalIdEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/external/{externalEventId}", async (
                string externalEventId,
                IDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                var result = await dispatcher.Send(
                    new GetEarthquakeByExternalId.Query { ExternalEventId = externalEventId },
                    cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("GetEarthquakeByExternalId")
            .WithSummary("One earthquake, by the reporting agency's identifier")
            .WithDescription(
                "Resolves an agency's own event id — us20008ixa, iscgem913230 — to the event in this "
                + "archive.\n\n"
                + "Exists because this platform's primary keys are minted at insert, so they change "
                + "whenever the archive is re-ingested. Anything durable — curated story content, a "
                + "citation in a publication, a link shared with another institution — must reference "
                + "the agency identifier instead, which survives re-ingestion and resolves to the same "
                + "event in any copy of the catalogue.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
}

/// <summary>
/// GET /api/earthquakes/activity — monthly counts for the timeline histogram.
/// </summary>
internal static class GetEarthquakeActivityEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/activity", async (
                IDispatcher dispatcher,
                CancellationToken cancellationToken,
                DateTimeOffset? from = null,
                DateTimeOffset? to = null,
                MagnitudeScaleFamily? scaleFamily = null,
                double? minMagnitude = null) =>
            {
                var query = new GetEarthquakeActivity.Query
                {
                    From = from,
                    To = to,
                    ScaleFamily = scaleFamily,
                    MinMagnitude = minMagnitude,
                };

                var result = await dispatcher.Send(query, cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("GetEarthquakeActivity")
            .WithSummary("Monthly event counts across the archive")
            .WithDescription(
                "Returns one bucket per month, including months with no events, plus the archive's "
                + "full extent. Backs the timeline's density histogram: seismicity is extremely "
                + "uneven, so a scrubber without a distribution behind it gives no indication that "
                + "most months are quiet and a few weeks are not.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem();
}

/// <summary>
/// GET /api/earthquakes/map — the whole archive, compactly, for rendering.
/// </summary>
internal static class GetEarthquakeMapDataEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/map", async (
                IDispatcher dispatcher,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                var result = await dispatcher.Send(new GetEarthquakeMapData.Query(), cancellationToken);

                if (result.IsSuccess)
                {
                    // The historical archive is effectively immutable; only the last few
                    // days change. A short cache keeps a page reload instant without
                    // risking a stale view of recent activity.
                    httpContext.Response.Headers.CacheControl = "public, max-age=300";
                }

                return result.ToHttpResult();
            })
            .WithName("GetEarthquakeMapData")
            .WithSummary("Compact archive for map rendering")
            .WithDescription(
                "Returns every earthquake reduced to the fields a circle layer needs, with terse "
                + "field names. Roughly a fifth the size of the search endpoint for the same events, "
                + "because it omits the agency names and formatted display strings that the map "
                + "never reads. Use /api/earthquakes for lists and /api/earthquakes/{id} for detail.")
            .Produces<object>(StatusCodes.Status200OK);
}

/// <summary>
/// GET /api/earthquakes/cross-section — hypocentres along a vertical slice.
/// </summary>
internal static class GetCrossSectionEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/cross-section", async (
                double startLatitude,
                double startLongitude,
                double endLatitude,
                double endLongitude,
                IDispatcher dispatcher,
                CancellationToken cancellationToken,
                double corridorKm = 50d,
                bool includeAssignedDepths = false,
                double? minMagnitude = null,
                DateTimeOffset? from = null,
                DateTimeOffset? to = null) =>
            {
                var query = new GetCrossSection.Query
                {
                    StartLatitude = startLatitude,
                    StartLongitude = startLongitude,
                    EndLatitude = endLatitude,
                    EndLongitude = endLongitude,
                    CorridorKm = corridorKm,
                    IncludeAssignedDepths = includeAssignedDepths,
                    MinMagnitude = minMagnitude,
                    From = from,
                    To = to,
                };

                var result = await dispatcher.Send(query, cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("GetCrossSection")
            .WithSummary("Hypocentres along a vertical slice through the crust")
            .WithDescription(
                "Returns earthquakes within a corridor either side of a line, each positioned by "
                + "distance along the section and depth. Reveals the subducting slab, which is "
                + "invisible in plan view.\n\n"
                + "Agency-assigned depths are EXCLUDED by default — the opposite of the map's "
                + "default, deliberately. On a map an assigned depth still shows an earthquake "
                + "happened there; on a depth section it is a fabricated vertical position, and "
                + "43% of the archive carries one. Set includeAssignedDepths=true to see them, and "
                + "expect four flat bands at 10, 15, 33 and 35 km if you do.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem();
}

/// <summary>
/// GET /api/earthquakes/{eventId}/similar — events resembling this one, with the reasoning.
/// </summary>
internal static class GetSimilarEarthquakesEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/{eventId:guid}/similar", async (
                Guid eventId,
                IDispatcher dispatcher,
                CancellationToken cancellationToken,
                double maxDistanceKm = 150d,
                double maxMagnitudeDelta = 0.5d,
                double? maxDepthDeltaKm = 25d,
                bool requireComparableMagnitudeScale = true,
                bool excludeOperatorAssignedDepths = true,
                int limit = 10) =>
            {
                var query = new GetSimilarEarthquakes.Query
                {
                    EventId = eventId,
                    MaxDistanceKm = maxDistanceKm,
                    MaxMagnitudeDelta = maxMagnitudeDelta,
                    MaxDepthDeltaKm = maxDepthDeltaKm,
                    RequireComparableMagnitudeScale = requireComparableMagnitudeScale,
                    ExcludeOperatorAssignedDepths = excludeOperatorAssignedDepths,
                    Limit = limit,
                };

                var result = await dispatcher.Send(query, cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("GetSimilarEarthquakes")
            .WithSummary("Earthquakes resembling a given one")
            .WithDescription(
                "Ranks nearby events by a composite of epicentral distance, magnitude difference "
                + "and depth difference, and returns the component breakdown rather than a bare "
                + "score.\n\n"
                + "Two comparisons are frequently unavailable, and the response says so rather "
                + "than substituting zero. Magnitude difference is null across scale families — "
                + "`mb` saturates near M6 and is not comparable with moment magnitude — and depth "
                + "difference is null when either depth was assigned by the agency rather than "
                + "measured. A component that cannot be computed is omitted from the average, so "
                + "an event is never penalised for a gap in the catalogue.\n\n"
                + "By default candidates must report a comparable scale and a measured depth. "
                + "Relaxing either widens the result set; the response reports how many events "
                + "each filter accounted for.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
}

/// <summary>
/// GET /api/earthquakes/compare — two events set against each other.
/// </summary>
internal static class CompareEarthquakesEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/compare", async (
                Guid left,
                Guid right,
                IDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                var query = new CompareEarthquakes.Query
                {
                    LeftEventId = left,
                    RightEventId = right,
                };

                var result = await dispatcher.Send(query, cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("CompareEarthquakes")
            .WithSummary("Two earthquakes set against each other")
            .WithDescription(
                "Returns both events with their full multi-agency reading sets, plus three "
                + "cross-event differences: epicentral separation, magnitude and depth.\n\n"
                + "Two of the three can be unavailable, and the response says why rather than "
                + "substituting a figure. Magnitude cannot be differenced across scale "
                + "families — PHIVOLCS favours surface-wave magnitude for large local events "
                + "while the USGS archive is 92.8% body-wave — and depth cannot be differenced "
                + "when either value was fixed to an agency default, which applies to roughly "
                + "43% of the archive.\n\n"
                + "Comparisons use each event's preferred reading rather than an average "
                + "across agencies, because averaging magnitudes from different scales is the "
                + "error this platform exists to make visible.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
}

/// <summary>Route group for the earthquake endpoints.</summary>
internal static class EarthquakesModule
{
    public static void MapEarthquakes(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/earthquakes")
            .WithTags("Earthquakes")
            .RequireRateLimiting(RateLimitPolicies.PublicRead);

        SearchEarthquakesEndpoint.Map(group);

        // Literal routes before the {eventId:guid} route for readability. Ordering is
        // not load-bearing: the guid constraint means these cannot match it.
        GetEarthquakeMapDataEndpoint.Map(group);
        GetEarthquakeActivityEndpoint.Map(group);
        GetCrossSectionEndpoint.Map(group);
        CompareEarthquakesEndpoint.Map(group);
        GetSimilarEarthquakesEndpoint.Map(group);
        GetEarthquakeByExternalIdEndpoint.Map(group);
        GetEarthquakeDetailEndpoint.Map(group);
    }
}
