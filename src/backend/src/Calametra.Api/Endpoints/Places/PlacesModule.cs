using Calametra.Api.Extensions;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Places;

namespace Calametra.Api.Endpoints.Places;

/// <summary>
/// GET /api/places — find a city, municipality, province or region by name.
/// </summary>
internal static class SearchPlacesEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/", async (
                IDispatcher dispatcher,
                CancellationToken cancellationToken,
                string q = "",
                int limit = 20) =>
            {
                var query = new SearchPlaces.Query { Term = q, Limit = limit };

                var result = await dispatcher.Send(query, cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("SearchPlaces")
            .WithSummary("Administrative places matching a name")
            .WithDescription(
                "17 regions, 87 province-level units and 1,646 cities and municipalities, from the "
                + "GeoNames Philippine gazetteer under CC BY 4.0. Barangays are not included.\n\n"
                + "Every match names the province containing it, because 111 of the city and "
                + "municipality names in this country are not unique — a bare 'San Isidro' is "
                + "genuinely ambiguous.\n\n"
                + "Results are ranked by match quality and then by administrative level, never by "
                + "size: the gazetteer publishes a population figure but not the census year it "
                + "came from, so it is deliberately not stored.\n\n"
                + "The PSGC code returned is the pre-2019 nine-digit form, and is null for five "
                + "places. Use it rather than any internal identifier for anything durable.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem();
}

/// <summary>
/// GET /api/places/{psgcCode}/context — what the archive holds around one place.
/// </summary>
internal static class GetPlaceContextEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapGet("/{psgcCode}/context", async (
                string psgcCode,
                IDispatcher dispatcher,
                CancellationToken cancellationToken,
                double radiusKm = 25d) =>
            {
                var query = new GetPlaceContext.Query { PsgcCode = psgcCode, RadiusKm = radiusKm };

                var result = await dispatcher.Send(query, cancellationToken);

                return result.ToHttpResult();
            })
            .WithName("GetPlaceContext")
            .WithSummary("Earthquakes and mapped faults around one place")
            .WithDescription(
                "Keyed on the PSGC code rather than an internal identifier, so a link to a place "
                + "survives re-importing the directory.\n\n"
                + "There is no single largest nearby earthquake in the response, and that is "
                + "deliberate. Ranking by reported magnitude across scales would compare a "
                + "body-wave value with a moment magnitude, and mb saturates near M6 — so the "
                + "strongest reading is returned once per scale family, exactly as cyclone peak "
                + "intensity is returned once per wind averaging period.\n\n"
                + "Distances are measured from a representative point, not from the place's "
                + "boundary, and that distinction is now sharper rather than softer. Land outlines "
                + "ARE stored for current cities and municipalities — OCHA COD-AB, land-only, "
                + "1,611 of 1,642 units — and they are served as vector tiles from "
                + "/api/lgu-boundaries. They are a different spatial concept and this endpoint does "
                + "not use them.\n\n"
                + "**Proximity is not containment.** Everything here is proximity: how much of the "
                + "archive lies within a distance of one representative point. Containment asks "
                + "which unit a location is in, which is what a boundary answers. ADR-005 D1 keeps "
                + "them apart because Philippine LGUs differ in area by more than two orders of "
                + "magnitude — counting events inside boundaries compares land area as much as "
                + "seismicity, which is exactly why the radius exists. For a large municipality the "
                + "representative point is tens of kilometres from its edge, so these figures are "
                + "not statements about the municipality's territory.\n\n"
                + "The nearest fault traces are returned whether or not they fall inside the "
                + "radius, each flagged. Proximity is not attribution — the 2013 Bohol earthquake "
                + "came from a fault no database held until afterwards.\n\n"
                + "Radius is capped at 300 km, below a measured PostGIS planner cliff at roughly "
                + "400 km. The interface offers 10, 25, 50 and 100 km.")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
}

/// <summary>Route group for the place endpoints.</summary>
internal static class PlacesModule
{
    public static void MapPlaces(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/places")
            .WithTags("Places")
            .RequireRateLimiting(RateLimitPolicies.PublicRead);

        SearchPlacesEndpoint.Map(group);
        GetPlaceContextEndpoint.Map(group);
    }
}
