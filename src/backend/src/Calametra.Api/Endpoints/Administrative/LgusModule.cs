using Calametra.Api.Extensions;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Administrative;

namespace Calametra.Api.Endpoints.Administrative;

internal static class GetAdministrativeUnitEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/{canonicalPsgcCode}", async (
                string canonicalPsgcCode,
                IDispatcher dispatcher,
                CancellationToken cancellationToken) =>
            {
                var result = await dispatcher.Send(
                    new GetAdministrativeUnit.Query(canonicalPsgcCode),
                    cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("GetAdministrativeUnit")
            .WithSummary("Canonical administrative identity and current published official land area")
            .WithDescription(
                "Returns PSA PSGC identity and, when a complete current official-area edition contains "
                + "the exact ten-digit code, its fixed-precision published land area. This value is not "
                + "computed from COD-AB geometry and never falls back to boundary area.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
}

internal static class LgusModule
{
    public static void MapLgus(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/lgus")
            .WithTags("Administrative units")
            .RequireRateLimiting(RateLimitPolicies.PublicRead);

        GetAdministrativeUnitEndpoint.Map(group);
    }
}
