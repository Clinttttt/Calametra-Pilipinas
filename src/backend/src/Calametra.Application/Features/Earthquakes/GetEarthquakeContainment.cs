using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Administrative;
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
    private static readonly Error BoundaryUnavailable = new(
        ErrorType.NotFound,
        "lgu_boundary.not_available",
        "The local government unit is known, but no current COD-AB land boundary is available. No geometry "
        + "is fabricated for a known coverage exception.");

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
        private const string Predicate = "ST_Intersects";

        private const string Semantics =
            "Distinct earthquake events whose canonical epicentres are covered by the selected current "
            + "COD-AB land boundary. Points exactly on the boundary are included. Offshore epicentres and "
            + "per-agency observation rows are not counted.";

        public async Task<Result<Response>> Handle(Query request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var lgu = await context.Lgus
                .AsNoTracking()
                .Where(candidate => candidate.CanonicalPsgcCode == request.CanonicalPsgcCode)
                .Select(candidate => new { candidate.Id, candidate.Name, candidate.Level })
                .SingleOrDefaultAsync(cancellationToken);

            if (lgu is null)
            {
                return Result<Response>.Failure(LguErrors.NotFound);
            }

            var boundary = await (
                    from outline in context.LguBoundaries.AsNoTracking()
                    join extract in context.LguBoundaryExtracts.AsNoTracking()
                        on outline.ExtractId equals extract.Id
                    join source in context.DataSources.AsNoTracking()
                        on outline.SourceId equals source.Id
                    where outline.LguId == lgu.Id
                        && outline.ValidTo == null
                        && extract.Provenance == BoundaryProvenance.OchaCodAb
                    select new
                    {
                        outline.Id,
                        outline.AreaSquareKm,
                        ExtractLabel = extract.Label,
                        extract.Vintage,
                        source.Attribution,
                    })
                .SingleOrDefaultAsync(cancellationToken);

            if (boundary is null)
            {
                return Result<Response>.Failure(BoundaryUnavailable);
            }

            // One SQL query. The outline remains in PostGIS rather than being materialised into managed
            // memory, and Intersects translates to ST_Intersects(geography, geography), allowing the event
            // GiST index to apply its bounding-box prefilter before the exact predicate.
            var earthquakeCount = await (
                    from outline in context.LguBoundaries.AsNoTracking()
                    from earthquake in context.HazardEvents.AsNoTracking()
                    where outline.Id == boundary.Id
                        && earthquake.Type == HazardEventType.Earthquake
                        && outline.Geometry.Intersects(earthquake.CanonicalEpicenter)
                    select earthquake.Id)
                .Distinct()
                .CountAsync(cancellationToken);

            return Result<Response>.Success(new Response(
                request.CanonicalPsgcCode,
                lgu.Name,
                lgu.Level.ToString(),
                Math.Round(boundary.AreaSquareKm, 1, MidpointRounding.AwayFromZero),
                earthquakeCount,
                Predicate,
                Semantics,
                new BoundaryEdition(boundary.ExtractLabel, boundary.Vintage, boundary.Attribution)));
        }
    }
}
