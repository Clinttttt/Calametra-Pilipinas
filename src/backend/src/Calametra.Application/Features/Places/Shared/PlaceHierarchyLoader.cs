using Calametra.Application.Abstractions.Data;
using Calametra.Domain.Places;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Places.Shared;

/// <summary>One place in the containment chain above a match.</summary>
internal sealed record PlaceAncestor(Guid Id, string Name, PlaceKind Kind, Guid? ParentPlaceId);

/// <summary>
/// Resolves the province and region above a place.
/// </summary>
/// <remarks>
/// Extracted so <c>SearchPlaces</c> and <c>GetPlaceContext</c> share one implementation. The
/// part that must not be duplicated is the rule for reading the chain: a municipality's region
/// is its grandparent while a province's region is its own parent, and 111 municipality names
/// in this country are not unique, so a wrong container is a wrong answer rather than a
/// cosmetic slip.
/// </remarks>
internal static class PlaceHierarchyLoader
{
    /// <summary>
    /// Loads the parents of the given places, and then their parents.
    /// </summary>
    /// <remarks>
    /// Two small keyed queries rather than a recursive CTE. The hierarchy is three levels deep
    /// and a page of results names at most twenty parents, so the readable version costs
    /// nothing measurable.
    /// </remarks>
    public static async Task<Dictionary<Guid, PlaceAncestor>> LoadAsync(
        IApplicationDbContext context,
        IEnumerable<Guid?> parentIds,
        CancellationToken cancellationToken)
    {
        var wanted = parentIds
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var chain = new Dictionary<Guid, PlaceAncestor>();

        for (var depth = 0; depth < 2 && wanted.Count > 0; depth++)
        {
            var loaded = await context.Places.AsNoTracking()
                .Where(place => wanted.Contains(place.Id))
                .Select(place => new PlaceAncestor(place.Id, place.Name, place.Kind, place.ParentPlaceId))
                .ToListAsync(cancellationToken);

            foreach (var ancestor in loaded)
            {
                chain[ancestor.Id] = ancestor;
            }

            wanted = loaded
                .Where(ancestor => ancestor.ParentPlaceId.HasValue)
                .Select(ancestor => ancestor.ParentPlaceId!.Value)
                .Where(id => !chain.ContainsKey(id))
                .Distinct()
                .ToList();
        }

        return chain;
    }

    /// <summary>
    /// Names the containing unit and the region, from the stored hierarchy only.
    /// </summary>
    /// <remarks>
    /// Nothing is inferred from the level. A place whose parent could not be resolved reports
    /// no container rather than a guessed one — the import counts those, and the figure is
    /// currently zero.
    /// </remarks>
    public static (string? ContainedBy, string? Region) Describe(
        Guid? parentPlaceId,
        IReadOnlyDictionary<Guid, PlaceAncestor> chain)
    {
        if (parentPlaceId is not { } parentId || !chain.TryGetValue(parentId, out var parent))
        {
            return (null, null);
        }

        if (parent.ParentPlaceId is { } grandparentId && chain.TryGetValue(grandparentId, out var grandparent))
        {
            return (parent.Name, grandparent.Name);
        }

        return (parent.Name, parent.Kind == PlaceKind.Region ? parent.Name : null);
    }
}
