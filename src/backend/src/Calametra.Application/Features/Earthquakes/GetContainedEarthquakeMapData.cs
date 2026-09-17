using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Earthquakes.Shared;
using Calametra.Domain.Abstractions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Earthquakes;

/// <summary>Compact map records for the canonical earthquakes contained by one current LGU land outline.</summary>
public static class GetContainedEarthquakeMapData
{
    public sealed record Query(string CanonicalPsgcCode) : IQuery<Response>;

    public sealed record BoundaryEdition(string Label, DateOnly? Vintage, string Attribution);

    public sealed record Response(
        string CanonicalPsgcCode,
        string Name,
        string Level,
        double BoundaryGeometryAreaSquareKm,
        int Count,
        IReadOnlyList<GetEarthquakeMapData.MapPoint> Points,
        string CountSemantics,
        BoundaryEdition Boundary);

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
            var resolved = await EarthquakeContainmentQuery.ResolveAsync(
                context,
                request.CanonicalPsgcCode,
                cancellationToken);

            if (resolved.IsFailure)
            {
                return Result<Response>.Failure(resolved.Error!);
            }

            var boundary = resolved.Value;
            var eventIds = EarthquakeContainmentQuery.EventIds(context, boundary.BoundaryId);
            var rows = await GetEarthquakeMapData.Rows(context, eventIds).ToListAsync(cancellationToken);
            var points = GetEarthquakeMapData.ToPoints(rows);

            // Count comes from the returned canonical-event projection itself. It therefore cannot count
            // observations or drift from the map payload, while both this and the count endpoint share the
            // authoritative EventIds predicate above.
            return Result<Response>.Success(new Response(
                boundary.CanonicalPsgcCode,
                boundary.Name,
                boundary.Level,
                boundary.BoundaryGeometryAreaSquareKm,
                points.Count,
                points,
                EarthquakeContainmentQuery.Semantics,
                new BoundaryEdition(
                    boundary.BoundaryLabel,
                    boundary.BoundaryVintage,
                    boundary.BoundaryAttribution)));
        }
    }
}
