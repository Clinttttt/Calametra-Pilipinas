using Calametra.Api.Extensions;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Sources;

namespace Calametra.Api.Endpoints.Sources;

/// <summary>
/// GET /api/data-sources — every upstream dataset, with its licence position and limits.
/// </summary>
internal static class ListDataSourcesEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/", async (IDispatcher dispatcher, CancellationToken cancellationToken) =>
            {
                var result = await dispatcher.Send(new ListDataSources.Query(), cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("ListDataSources")
            .WithSummary("Every dataset this platform reads")
            .WithDescription(
                "The credits page is generated from this response, so attribution cannot drift "
                + "from what the system actually reads.\n\n"
                + "Each entry carries three facts that are not inferable from the agency's name: "
                + "whether it is an official Philippine authority for its domain, whether this "
                + "platform is licensed to store its data or may only proxy the publisher's own "
                + "rendering, and what the dataset does not cover.\n\n"
                + "Reachability is not a licence. Sources awaiting written permission are marked "
                + "as not redistributable, and no rows of their data exist in this database.")
            .Produces<object>(StatusCodes.Status200OK);
}

/// <summary>Route group for the provenance endpoints.</summary>
internal static class DataSourcesModule
{
    public static void MapDataSources(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/data-sources")
            .WithTags("Data sources")
            .RequireRateLimiting(RateLimitPolicies.PublicRead);

        ListDataSourcesEndpoint.Map(group);
    }
}
