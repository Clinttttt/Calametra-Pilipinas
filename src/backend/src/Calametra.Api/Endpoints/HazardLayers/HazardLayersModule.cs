using Calametra.Api.Extensions;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.HazardLayers;
using Calametra.Domain.Hazards;
using Microsoft.AspNetCore.Mvc;

namespace Calametra.Api.Endpoints.HazardLayers;

/// <summary>
/// How long a proxied hazard tile may be cached by clients and intermediaries.
/// </summary>
/// <remarks>
/// Declared here rather than read from the Infrastructure options type, because only
/// Program.cs may name an Infrastructure type. Cache duration is a transport concern
/// anyway: it describes the HTTP response, not how the upstream service is called.
/// </remarks>
internal static class HazardTileCache
{
    /// <summary>Seven days. Fault traces and hazard polygons change on a timescale of years.</summary>
    public const int MaxAgeSeconds = 604_800;
}

internal static class ListHazardLayersEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/", async (
                IDispatcher dispatcher,
                CancellationToken cancellationToken,
                HazardLens? lens = null) =>
            {
                var result = await dispatcher.Send(new ListHazardLayers.Query { Lens = lens }, cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("ListHazardLayers")
            .WithSummary("List available hazard layers")
            .WithDescription(
                "Returns the hazard layer catalogue with attribution, terms links and plain-language "
                + "explanations. Clients build their layer controls from this rather than hard-coding "
                + "a list, so attribution can never drift from what is displayed.");
}

internal static class GetHazardTileEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/{hazardLayerId:guid}/tile", async (
                Guid hazardLayerId,
                [FromQuery] string bbox,
                IDispatcher dispatcher,
                HttpContext httpContext,
                CancellationToken cancellationToken,
                int width = 256,
                int height = 256) =>
            {
                var query = new GetHazardTile.Query
                {
                    HazardLayerId = hazardLayerId,
                    BoundingBox = bbox,
                    Width = width,
                    Height = height,
                };

                var result = await dispatcher.Send(query, cancellationToken);

                if (result.IsFailure)
                {
                    return result.ToHttpResult();
                }

                // Cached hard: fault traces and hazard polygons change on a timescale
                // of years, and every uncached request is load placed on a government
                // service that is doing us a favour by being reachable at all.
                var maxAge = HazardTileCache.MaxAgeSeconds;
                httpContext.Response.Headers.CacheControl = $"public, max-age={maxAge}, immutable";

                return Results.File(result.Value.Content, result.Value.ContentType);
            })
            .WithName("GetHazardTile")
            .WithSummary("Proxy a rendered hazard tile")
            .WithDescription(
                "Forwards a WMS GetMap request to the publishing agency and returns the image "
                + "unmodified. Proxied rather than stored because the upstream service permits "
                + "rendering but not bulk vector extraction, and public reachability is not a "
                + "redistribution licence. Bounding box must be EPSG:3857.");
}

internal static class IdentifyHazardFeatureEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/{hazardLayerId:guid}/identify", async (
                Guid hazardLayerId,
                double latitude,
                double longitude,
                IDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                var query = new IdentifyHazardFeature.Query
                {
                    HazardLayerId = hazardLayerId,
                    Latitude = latitude,
                    Longitude = longitude,
                };

                var result = await dispatcher.Send(query, cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("IdentifyHazardFeature")
            .WithSummary("Inspect a hazard feature at a position")
            .WithDescription(
                "Returns the publishing agency's own attributes for the feature under a clicked "
                + "point. Attributes are passed through verbatim: renaming an authority's "
                + "terminology would misrepresent it.");
}

internal static class GetHazardFeaturesEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/{hazardLayerId:guid}/features", async (
                Guid hazardLayerId,
                IDispatcher dispatcher,
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                var query = new GetHazardFeatures.Query { HazardLayerId = hazardLayerId };

                var result = await dispatcher.Send(query, cancellationToken);

                if (result.IsSuccess)
                {
                    // Stored fault geometry changes only when the upstream catalogue
                    // is revised, which is a matter of years.
                    httpContext.Response.Headers.CacheControl = "public, max-age=86400";
                }

                return result.ToHttpResult();
            })
            .WithName("GetHazardFeatures")
            .WithSummary("Locally stored hazard geometry as GeoJSON")
            .WithDescription(
                "Returns geometry for layers Calametra is licensed to store, with the attribution "
                + "that must accompany it. Proxied layers return no features and should be rendered "
                + "through the tile endpoint instead — a layer's deliveryMode says which applies.");
}

internal static class HazardLayersModule
{
    public static void MapHazardLayers(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/hazard-layers")
            .WithTags("Hazard layers");

        // Reads served from our own database.
        var catalogue = app
            .MapGroup("/api/hazard-layers")
            .WithTags("Hazard layers")
            .RequireRateLimiting(RateLimitPolicies.PublicRead);

        ListHazardLayersEndpoint.Map(catalogue);
        GetHazardFeaturesEndpoint.Map(catalogue);

        // Tile and identify calls become outbound requests to an agency service, so
        // they take the tighter policy.
        var proxied = group.RequireRateLimiting(RateLimitPolicies.HazardProxy);

        GetHazardTileEndpoint.Map(proxied);
        IdentifyHazardFeatureEndpoint.Map(proxied);
    }
}
