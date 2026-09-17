using Calametra.Application.Abstractions.Data;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Administrative;
using Calametra.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Earthquakes.Shared;

/// <summary>The single authoritative definition of earthquake membership in a current LGU land outline.</summary>
internal static class EarthquakeContainmentQuery
{
    internal static readonly Error BoundaryUnavailable = new(
        ErrorType.NotFound,
        "lgu_boundary.not_available",
        "The local government unit is known, but no current COD-AB land boundary is available. No geometry "
        + "is fabricated for a known coverage exception.");

    internal const string Predicate = "ST_Intersects";

    internal const string Semantics =
        "Distinct earthquake events whose canonical epicentres are covered by the selected current "
        + "COD-AB land boundary. Points exactly on the boundary are included. Offshore epicentres and "
        + "per-agency observation rows are not counted.";

    internal sealed record Context(
        Guid BoundaryId,
        string CanonicalPsgcCode,
        string Name,
        string Level,
        double BoundaryGeometryAreaSquareKm,
        string BoundaryLabel,
        DateOnly? BoundaryVintage,
        string BoundaryAttribution);

    internal static async Task<Result<Context>> ResolveAsync(
        IApplicationDbContext context,
        string canonicalPsgcCode,
        CancellationToken cancellationToken)
    {
        var lgu = await context.Lgus
            .AsNoTracking()
            .Where(candidate => candidate.CanonicalPsgcCode == canonicalPsgcCode)
            .Select(candidate => new { candidate.Id, candidate.Name, candidate.Level })
            .SingleOrDefaultAsync(cancellationToken);

        if (lgu is null)
        {
            return Result<Context>.Failure(LguErrors.NotFound);
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

        return boundary is null
            ? Result<Context>.Failure(BoundaryUnavailable)
            : Result<Context>.Success(new Context(
                boundary.Id,
                canonicalPsgcCode,
                lgu.Name,
                lgu.Level.ToString(),
                Math.Round(boundary.AreaSquareKm, 1, MidpointRounding.AwayFromZero),
                boundary.ExtractLabel,
                boundary.Vintage,
                boundary.Attribution));
    }

    /// <summary>
    /// Distinct canonical events covered by the current land polygon. Kept as an IQueryable so PostGIS
    /// performs both the GiST-backed prefilter and exact geography predicate.
    /// </summary>
    internal static IQueryable<Guid> EventIds(IApplicationDbContext context, Guid boundaryId) =>
        (
            from outline in context.LguBoundaries.AsNoTracking()
            from earthquake in context.HazardEvents.AsNoTracking()
            where outline.Id == boundaryId
                && earthquake.Type == HazardEventType.Earthquake
                && outline.Geometry.Intersects(earthquake.CanonicalEpicenter)
            select earthquake.Id
        ).Distinct();
}
