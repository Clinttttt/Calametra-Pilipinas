namespace Calametra.Application.Abstractions.Sources;

/// <summary>One geographic row in the PSA publication, keyed only by the published ten-digit code.</summary>
public sealed record OfficialLandAreaSourceRow(
    string CanonicalPsgcCode,
    string SourceLabel,
    decimal AreaSquareKm);

/// <summary>An exact, hashed acquisition of one PSA land-area matrix edition.</summary>
public sealed record OfficialLandAreaSnapshot
{
    public required string Label { get; init; }

    public required string MatrixId { get; init; }

    public required string AccessRoute { get; init; }

    public required int ReferenceYear { get; init; }

    public required DateTimeOffset RetrievedAt { get; init; }

    public DateTimeOffset? SourceUpdatedAt { get; init; }

    public required string Attribution { get; init; }

    public required string MetadataJson { get; init; }

    public required string PayloadJson { get; init; }

    public required string MetadataSha256 { get; init; }

    public required string PayloadSha256 { get; init; }

    public required IReadOnlyList<OfficialLandAreaSourceRow> Rows { get; init; }
}

/// <summary>Reads the official PSA statistical land-area publication.</summary>
public interface IOfficialLandAreaSource
{
    string SourceSlug { get; }

    Task<OfficialLandAreaSnapshot> ReadAsync(CancellationToken cancellationToken);
}
