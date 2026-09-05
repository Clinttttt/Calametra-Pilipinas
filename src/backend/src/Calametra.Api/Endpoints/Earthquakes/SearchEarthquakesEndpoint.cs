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
                + "to an agency default rather than measured — roughly 28% of the USGS CARAGA "
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
        GetEarthquakeDetailEndpoint.Map(group);
    }
}
