using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Places.Shared;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Places;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Places;

/// <summary>
/// Finds an administrative place by name.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every match carries its container, and that is not decoration.</b> 111 of the 1,646
/// city and municipality names in this country are not unique — there are several San
/// Isidros and several Sans Miguels — so a bare name is genuinely ambiguous and a reader
/// choosing between two identical rows would be guessing. The province, and the region
/// above it, are what make the choice possible.
/// </para>
/// <para>
/// <b>Ranking is by match quality and then by administrative level, never by size.</b>
/// There is no population figure in this database to rank by: the gazetteer publishes one
/// but not the census year it came from, so it is deliberately not imported. Ordering on a
/// figure whose vintage is unknown would be exactly the kind of unattributed number this
/// platform refuses, and it would be invisible in the output.
/// </para>
/// </remarks>
public static class SearchPlaces
{
    public sealed record Query : IQuery<IReadOnlyList<PlaceMatch>>
    {
        public required string Term { get; init; }

        public int Limit { get; init; } = 20;
    }

    /// <param name="PsgcCode">
    /// The durable identifier, and null for five places in the directory. Guardrail 8: a link
    /// a reader shares must not carry the internal id, which is minted at insert and changes
    /// when the directory is re-imported.
    /// </param>
    /// <param name="ContainedBy">The province, or the region for a province-level unit.</param>
    /// <param name="Region">The region, where the chain reaches that far.</param>
    public sealed record PlaceMatch(
        string? PsgcCode,
        string Name,
        string Kind,
        string? ContainedBy,
        string? Region,
        double Latitude,
        double Longitude);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            // Two characters minimum. One character matches several hundred places and answers
            // nothing, and the cost is a full scan of the name column per keystroke.
            RuleFor(query => query.Term).NotEmpty().MinimumLength(2).MaximumLength(160);

            RuleFor(query => query.Limit).InclusiveBetween(1, 50);
        }
    }

    internal sealed class Handler(IApplicationDbContext context)
        : IQueryHandler<Query, IReadOnlyList<PlaceMatch>>
    {
        public async Task<Result<IReadOnlyList<PlaceMatch>>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var term = request.Term.Trim().ToUpperInvariant();

            // ToUpper().Contains(...) rather than EF.Functions.ILike, which is an Npgsql
            // extension: the architecture tests keep this project provider-agnostic. Same
            // trade-off, and the same CA1862 suppression, as the cyclone name filter.
#pragma warning disable CA1862
            var matches = await context.Places.AsNoTracking()
                .Where(place => place.Name.ToUpper().Contains(term))
                // Exact match first, then names that begin with the term, then names that
                // merely contain it. Typing "Davao" should offer the region named exactly that
                // before Davao City, and Davao City before Governor Generoso.
                .OrderByDescending(place => place.Name.ToUpper() == term)
                .ThenByDescending(place => place.Name.ToUpper().StartsWith(term))
                // Then by level, with the units a reader actually flies to first. Written as an
                // explicit ordinal rather than relying on the enum: the column stores the name
                // as text, so ordering by the property would order alphabetically and only
                // happen to be right.
                .ThenBy(place => place.Kind == PlaceKind.City
                    ? 0
                    : place.Kind == PlaceKind.Municipality
                        ? 1
                        : place.Kind == PlaceKind.Province
                            ? 2
                            : 3)
                .ThenBy(place => place.Name)
                .Take(request.Limit)
                .Select(place => new
                {
                    place.PsgcCode,
                    place.Name,
                    place.Kind,
                    place.ParentPlaceId,
                    place.Latitude,
                    place.Longitude,
                })
                .ToListAsync(cancellationToken);
#pragma warning restore CA1862

            if (matches.Count == 0)
            {
                return Result<IReadOnlyList<PlaceMatch>>.Success([]);
            }

            var chain = await PlaceHierarchyLoader.LoadAsync(
                context,
                matches.Select(match => match.ParentPlaceId),
                cancellationToken);

            var results = matches
                .Select(match =>
                {
                    var (containedBy, region) = PlaceHierarchyLoader.Describe(match.ParentPlaceId, chain);

                    return new PlaceMatch(
                        match.PsgcCode,
                        match.Name,
                        match.Kind.ToString(),
                        containedBy,
                        region,
                        match.Latitude,
                        match.Longitude);
                })
                .ToList();

            return Result<IReadOnlyList<PlaceMatch>>.Success(results);
        }
    }
}
