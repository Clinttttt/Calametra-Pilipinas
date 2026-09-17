using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Earthquakes.Shared;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Events;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Earthquakes;

/// <summary>
/// Counts distinct earthquake events whose canonical epicentres are covered by one current LGU land outline.
/// </summary>
/// <remarks>
/// <para>
/// This is containment, not the place-radius query. The selected polygon is the current COD-AB land outline,
/// and the response states its area because ADR-005 D1 requires the denominator implicit in any contained
/// count to remain visible. Offshore earthquakes are therefore outside this answer by design.
/// </para>
/// <para>
/// The count is over <see cref="HazardEvent"/>, not <see cref="EarthquakeObservation"/>. One real earthquake
/// can have PHIVOLCS and USGS observations; counting the observation table would turn better-reported events
/// into two earthquakes.
/// </para>
/// <para>
/// <c>ST_Intersects</c> is deliberate. For a point tested against a polygon it has the same containment
/// truth table as <c>ST_Covers</c>, including a point exactly on the boundary. Empirical comparison against
/// the live COD-AB geometry found geography <c>ST_Covers</c> pathologically slow on fragmented island
/// multipolygons, while <c>ST_Intersects</c> retained the same result and GiST prefilter. PostGIS does not
/// provide <c>ST_Contains(geography, geography)</c>; casting merely to call it would exclude edge points and
/// prevent the geography GiST index from supporting the event lookup.
/// </para>
/// </remarks>
public static class GetEarthquakeContainment
{
    public sealed record Query(string CanonicalPsgcCode) : IQuery<Response>;

    public sealed record BoundaryEdition(
        string Label,
        DateOnly? Vintage,
        string Attribution);

    public sealed record Response(
        string CanonicalPsgcCode,
        string Name,
        string Level,
        double BoundaryGeometryAreaSquareKm,
        int EarthquakeCount,
        string SpatialPredicate,
        string CountSemantics,
        BoundaryEdition Boundary)
    {
        /// <summary>
        /// Compatibility alias retained for the first frontend consumer. This has always been the
        /// computed COD-AB polygon area; new consumers use <see cref="BoundaryGeometryAreaSquareKm"/>.
        /// </summary>
        [Obsolete("Use BoundaryGeometryAreaSquareKm. This value is not an official LGU land area.")]
        public double LandAreaSquareKm => BoundaryGeometryAreaSquareKm;
    }

    internal sealed class Validator : AbstractValidator<Query>
    {
        public Validator() =>
            RuleFor(query => query.CanonicalPsgcCode)
                .NotEmpty()
                .Matches("^[0-9]{10}$")
                .WithMessage("The canonical PSGC code must contain exactly ten digits.");
    }

    internal sealed class Handler(IApplicationDbContext context) : IQueryHandler<Query, Response>
    {
        public async Task<Result<Response>> Handle(Query request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var resolved = await EarthquakeContainmentQuery.ResolveAsync(
                context,
                request.CanonicalPsgcCode,
                cancellationToken);

            if (resolved.IsFailure)
            {
                return Result<Response>.Failure(resolved.Error!);
            }

            var boundary = resolved.Value;

            // One SQL query. The outline remains in PostGIS rather than being materialised into managed
            // memory, and Intersects translates to ST_Intersects(geography, geography), allowing the event
            // GiST index to apply its bounding-box prefilter before the exact predicate.
            var earthquakeCount = await EarthquakeContainmentQuery.EventIds(context, boundary.BoundaryId)
                .CountAsync(cancellationToken);

            return Result<Response>.Success(new Response(
                request.CanonicalPsgcCode,
                boundary.Name,
                boundary.Level,
                boundary.BoundaryGeometryAreaSquareKm,
                earthquakeCount,
                EarthquakeContainmentQuery.Predicate,
                EarthquakeContainmentQuery.Semantics,
                new BoundaryEdition(
                    boundary.BoundaryLabel,
                    boundary.BoundaryVintage,
                    boundary.BoundaryAttribution)));
        }
    }
}
