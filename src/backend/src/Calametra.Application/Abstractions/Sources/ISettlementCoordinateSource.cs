namespace Calametra.Application.Abstractions.Sources;

/// <summary>
/// A mapped town centre, as published by a settlement gazetteer.
/// </summary>
/// <remarks>
/// Deliberately thinner than <see cref="CatalogPlace"/>. This source is read for one field — where
/// the town actually is — and not to name places, rank them or build a hierarchy. Carrying only a
/// name, a kind and a position keeps it impossible to accidentally treat as a directory: the
/// authority for what a place is called and which province it belongs to stays with the gazetteer
/// that publishes the Philippine Standard Geographic Code.
/// </remarks>
public sealed record MappedSettlement
{
    /// <summary>The source's own identifier, kept so a correction can be traced upstream.</summary>
    public required string Key { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// Whether the source classes this as a city.
    /// </summary>
    /// <remarks>
    /// Used only as a weak cross-check on a match, never to reclassify a place. City status in the
    /// Philippines is conferred by law and recorded in the PSGC register; a volunteer map's opinion
    /// of it is not evidence, and 148 of this archive's cities are already identified from the
    /// official name.
    /// </remarks>
    public required bool IsCity { get; init; }

    public required double Latitude { get; init; }

    public required double Longitude { get; init; }
}

/// <summary>
/// Reads mapped town centres, used to place a city or municipality more precisely than an
/// administrative gazetteer does.
/// </summary>
/// <remarks>
/// A separate port from <see cref="IPlaceDirectorySource"/> because it answers a different question
/// and carries a different licence. The directory answers "which places exist, what are they called
/// and what is their code"; this answers "where is the town", and its data is share-alike, so a row
/// refined from it inherits an attribution obligation the directory's does not.
/// </remarks>
public interface ISettlementCoordinateSource
{
    /// <summary>Slug of the corresponding registered data source.</summary>
    string SourceSlug { get; }

    /// <summary>
    /// How far a candidate may sit from the gazetteer's point and still be considered the same
    /// place.
    /// </summary>
    double MatchRadiusKm { get; }

    /// <summary>Fetches every mapped city and town in the country.</summary>
    Task<IReadOnlyList<MappedSettlement>> FetchAsync(CancellationToken cancellationToken);
}
