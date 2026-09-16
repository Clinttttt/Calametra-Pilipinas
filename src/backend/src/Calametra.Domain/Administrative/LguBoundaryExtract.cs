using Calametra.Domain.Abstractions;

namespace Calametra.Domain.Administrative;

/// <summary>
/// How boundary geometry reached this platform.
/// </summary>
/// <remarks>
/// Declared by the operator for a file, never inferred. A file on disk carries no provenance, and guessing
/// would let any shapefile someone exported be presented as an upstream extract — the same reason the PSGC
/// register makes its provenance a declaration.
/// </remarks>
public enum BoundaryProvenance
{
    Unknown = 0,

    /// <summary>Read live from an Overpass API instance.</summary>
    OverpassApi = 1,

    /// <summary>A dated Geofabrik regional extract of OpenStreetMap.</summary>
    GeofabrikExtract = 2,

    /// <summary>A local file the operator did not declare as a named upstream extract.</summary>
    LocalFile = 3,
}

public static class LguBoundaryExtractErrors
{
    public static readonly Error SourceRequired = new(
        ErrorType.Validation,
        "boundary_extract.source_required",
        "An extract must name the registered DataSource it belongs to, so the licence travels with the "
        + "geometry read from it.");

    public static readonly Error LabelRequired = new(
        ErrorType.Validation,
        "boundary_extract.label_required",
        "An extract requires a label a person can cite.");

    public static readonly Error ProvenanceRequired = new(
        ErrorType.Validation,
        "boundary_extract.provenance_required",
        "An extract must state how it reached this platform.");

    public static readonly Error FileDetailsRequired = new(
        ErrorType.Validation,
        "boundary_extract.file_details_required",
        "A file-based extract requires the original filename and a SHA-256 of the bytes as delivered. A "
        + "geometry set whose origin cannot be checked is a geometry set nobody can audit.");

    public static readonly Error HashFormat = new(
        ErrorType.Validation,
        "boundary_extract.hash_format",
        "The SHA-256 must be 64 lowercase hexadecimal characters. A truncated digest is worse than none, "
        + "because it looks verifiable and is not.");
}

/// <summary>
/// One dated acquisition of boundary geometry, with the chain that makes it checkable.
/// </summary>
/// <remarks>
/// <para>
/// <b>A separate record rather than columns repeated on every outline.</b> One extract yields roughly
/// sixteen hundred polygons; holding the filename and digest on each would invite sixteen hundred slightly
/// different answers to "where did this come from". The same reason
/// <see cref="PsgcRegisterEdition"/> exists beside <see cref="Lgu"/>.
/// </para>
/// <para>
/// <b>The digest is of the original bytes, before anything is parsed.</b> A hash computed after conversion
/// would describe a file the upstream never published, which is exactly the trap the PSGC import avoided by
/// reading the PSA workbook rather than a CSV someone exported from it.
/// </para>
/// <para>
/// ADR-005 D7 requires a containment figure to be able to name the boundary edition behind it. This is that
/// edition: dated, hashed, attributed, and pointed at by every outline read from it.
/// </para>
/// </remarks>
public sealed class LguBoundaryExtract : AuditableEntity
{
    private LguBoundaryExtract()
    {
    }

    private LguBoundaryExtract(
        Guid id,
        Guid sourceId,
        string label,
        BoundaryProvenance provenance,
        string accessRoute,
        DateTimeOffset now)
        : base(id, now)
    {
        SourceId = sourceId;
        Label = label;
        Provenance = provenance;
        AccessRoute = accessRoute;
        AcquiredAt = now;
    }

    public Guid SourceId { get; private set; }

    /// <summary>What a person would call this extract, e.g. "Geofabrik Philippines, 2026-09-13".</summary>
    public string Label { get; private set; } = string.Empty;

    public BoundaryProvenance Provenance { get; private set; }

    /// <summary>The path or endpoint it was read from.</summary>
    public string AccessRoute { get; private set; } = string.Empty;

    /// <summary>The filename verbatim. Geofabrik encodes the extract date in it.</summary>
    public string? OriginalFileName { get; private set; }

    /// <summary>SHA-256 of the original bytes, lowercase hex.</summary>
    public string? FileSha256 { get; private set; }

    public long? FileSizeBytes { get; private set; }

    /// <summary>
    /// The date the upstream states the extract represents.
    /// </summary>
    /// <remarks>
    /// A <see cref="DateOnly"/> because that is the precision the claim has. It is a different fact from
    /// <see cref="AcquiredAt"/>: "the map as of 13 September" and "downloaded on 16 September" are not
    /// interchangeable, and only the first belongs beside a boundary.
    /// </remarks>
    public DateOnly? Vintage { get; private set; }

    /// <summary>Where the operator says they got it, in their own words.</summary>
    public string? AcquisitionNote { get; private set; }

    /// <summary>When this platform read it.</summary>
    public DateTimeOffset AcquiredAt { get; private set; }

    /// <summary>Outlines successfully read from this extract.</summary>
    public int BoundariesRead { get; private set; }

    public static Result<LguBoundaryExtract> Create(
        Guid sourceId,
        string label,
        BoundaryProvenance provenance,
        string accessRoute,
        DateTimeOffset now)
    {
        if (sourceId == Guid.Empty)
        {
            return Result<LguBoundaryExtract>.Failure(LguBoundaryExtractErrors.SourceRequired);
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            return Result<LguBoundaryExtract>.Failure(LguBoundaryExtractErrors.LabelRequired);
        }

        if (provenance == BoundaryProvenance.Unknown)
        {
            return Result<LguBoundaryExtract>.Failure(LguBoundaryExtractErrors.ProvenanceRequired);
        }

        return Result<LguBoundaryExtract>.Success(new LguBoundaryExtract(
            Guid.CreateVersion7(),
            sourceId,
            label.Trim(),
            provenance,
            string.IsNullOrWhiteSpace(accessRoute) ? "unspecified" : accessRoute.Trim(),
            now));
    }

    /// <summary>
    /// Records the acquisition chain of a file-based extract.
    /// </summary>
    public Result WithFile(
        string originalFileName,
        string fileSha256,
        long fileSizeBytes,
        DateOnly? vintage,
        string? acquisitionNote)
    {
        if (string.IsNullOrWhiteSpace(originalFileName) || string.IsNullOrWhiteSpace(fileSha256))
        {
            return Result.Failure(LguBoundaryExtractErrors.FileDetailsRequired);
        }

        var normalised = fileSha256.Trim().ToLowerInvariant();

        if (normalised.Length != 64 || !normalised.All(Uri.IsHexDigit))
        {
            return Result.Failure(LguBoundaryExtractErrors.HashFormat);
        }

        OriginalFileName = originalFileName.Trim();
        FileSha256 = normalised;
        FileSizeBytes = fileSizeBytes;
        Vintage = vintage;
        AcquisitionNote = string.IsNullOrWhiteSpace(acquisitionNote) ? null : acquisitionNote.Trim();

        return Result.Success();
    }

    public void WithUpstreamVintage(DateOnly? vintage)
    {
        Vintage ??= vintage;
    }

    public void RecordBoundariesRead(int count, DateTimeOffset now)
    {
        BoundariesRead = count;
        Touch(now);
    }
}
