using Calametra.Domain.Places;

namespace Calametra.Application.Abstractions.Sources;

/// <summary>
/// One administrative place as published upstream, with its position and its place in the
/// hierarchy.
/// </summary>
/// <remarks>
/// <para>
/// The hierarchy is carried as adapter-scoped keys rather than as codes, because the two are
/// not the same thing in any source examined. GeoNames links a municipality to its province
/// with its own internal codes while separately publishing the Philippine Standard Geographic
/// Code, and those two numbering schemes disagree — Samar is province <c>55</c> to GeoNames
/// and <c>60</c> in the PSGC. Linking on the source's own key is reliable; linking on a code
/// that has to be reconciled is not.
/// </para>
/// <para>
/// <see cref="PsgcCode"/> is therefore the public, durable identifier and nothing else, and it
/// is nullable because it is genuinely absent for some entries. Guardrail 8 applies to places
/// exactly as it applies to events: a shared link must not carry an internal id minted at
/// insert.
/// </para>
/// </remarks>
public sealed record CatalogPlace
{
    /// <summary>The source's own key for this place, unique within one fetch.</summary>
    public required string Key { get; init; }

    /// <summary>The source's key for the containing place, or null at the coarsest level.</summary>
    public string? ParentKey { get; init; }

    public required string Name { get; init; }

    public required PlaceKind Kind { get; init; }

    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    /// <summary>
    /// Philippine Standard Geographic Code, where the source publishes one that can be used
    /// without reconstruction.
    /// </summary>
    public string? PsgcCode { get; init; }
}

/// <summary>Reads the administrative place directory from an upstream gazetteer.</summary>
public interface IPlaceDirectorySource
{
    /// <summary>Slug of the corresponding registered data source.</summary>
    string SourceSlug { get; }

    /// <summary>
    /// Fetches every administrative place in the country, coarsest level first.
    /// </summary>
    /// <remarks>
    /// A list rather than an async stream, which is the opposite choice to
    /// <see cref="ICycloneTrackSource"/> and for a stated reason: the whole Philippine
    /// directory is under two thousand rows once settlements are excluded, and the importer
    /// has to resolve parents, which needs the coarser levels in hand before the finer ones
    /// can be linked. Streaming would buy nothing and would push the ordering guarantee into
    /// the caller.
    /// <para>
    /// The ordering is part of the contract: a place's parent always appears before it.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<CatalogPlace>> FetchAsync(CancellationToken cancellationToken);
}
