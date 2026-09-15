using Calametra.Domain.Abstractions;

namespace Calametra.Domain.Administrative;

/// <summary>
/// How a register edition reached this platform.
/// </summary>
/// <remarks>
/// Recorded rather than assumed, because it decides whether ADR-005's first gate condition is met.
/// A register is only <see cref="PsaDirect"/> when it came from the Philippine Statistics Authority's
/// own publication or API; anything else is a copy, however faithful, and a copy can be stale in ways
/// that matter. Measured on 2026-09-15: the most complete open mirror was last modified in August 2022
/// and carries 17 regions and 81 provinces — it therefore predates both the 2022 Maguindanao split and
/// the 2024 creation of the Negros Island Region, and would silently omit them.
/// </remarks>
public enum RegisterProvenance
{
    Unknown = 0,

    /// <summary>From the PSA's own publication or API. The only provenance that satisfies gate 1.</summary>
    PsaDirect = 1,

    /// <summary>A third-party copy of the register. Usable to exercise the pipeline, not to certify it.</summary>
    Mirror = 2,

    /// <summary>A file placed on disk by an operator, who states what it is.</summary>
    LocalFile = 3,
}

public static class PsgcRegisterEditionErrors
{
    public static readonly Error LabelRequired = new(
        ErrorType.Validation,
        "psgc_edition.label_required",
        "A register edition requires a label naming the publication it is.");

    public static readonly Error ProvenanceRequired = new(
        ErrorType.Validation,
        "psgc_edition.provenance_required",
        "A register edition requires a stated provenance.");

    public static readonly Error AccessRouteRequired = new(
        ErrorType.Validation,
        "psgc_edition.access_route_required",
        "A register edition requires the URL or path it was read from.");

    public static readonly Error SourceRequired = new(
        ErrorType.Validation,
        "psgc_edition.source_required",
        "A register edition requires the data source it belongs to.");

    public static readonly Error NotFound = new(
        ErrorType.NotFound,
        "psgc_edition.not_found",
        "No PSGC register edition has been loaded.");
}

/// <summary>
/// One dated edition of the Philippine Standard Geographic Code, as this platform read it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the edition is an entity rather than a string on each row.</b> ADR-005 requires every
/// confirmed crosswalk row to cite the register edition it was confirmed against, and the register
/// changes quarterly as units are created, renamed and dissolved. A label repeated on 1,700 rows would
/// drift; a foreign key cannot. It also makes the question "which edition is this platform reconciled
/// to" answerable by a query rather than by reading code.
/// </para>
/// <para>
/// <b>The observed counts are stored deliberately.</b> A register that reports 17 regions is a
/// pre-2024 register whatever its label claims, and a register showing an undivided Maguindanao
/// predates May 2022. Holding the counts lets the readiness report state that in figures rather than
/// taking the label's word for it — the same reason every reading on this platform carries its own
/// measured properties instead of a description of them.
/// </para>
/// </remarks>
public sealed class PsgcRegisterEdition : AuditableEntity
{
    private PsgcRegisterEdition()
    {
    }

    private PsgcRegisterEdition(
        Guid id,
        string label,
        RegisterProvenance provenance,
        string accessRoute,
        Guid dataSourceId,
        DateTimeOffset retrievedAt,
        DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        Label = label;
        Provenance = provenance;
        AccessRoute = accessRoute;
        DataSourceId = dataSourceId;
        RetrievedAt = retrievedAt;
    }

    /// <summary>What this edition is, in the words of whoever published it.</summary>
    public string Label { get; private set; } = string.Empty;

    public RegisterProvenance Provenance { get; private set; }

    /// <summary>The URL or file path this edition was read from, so the read can be repeated.</summary>
    public string AccessRoute { get; private set; } = string.Empty;

    /// <summary>The registered <c>DataSource</c> this edition belongs to.</summary>
    public Guid DataSourceId { get; private set; }

    /// <summary>When this platform read it. Not when the publisher issued it.</summary>
    public DateTimeOffset RetrievedAt { get; private set; }

    /// <summary>
    /// The upstream file's own last-modified stamp, where the transport reports one.
    /// </summary>
    /// <remarks>
    /// The single most useful staleness signal available for a mirror, and the one that established
    /// that the open mirror is a 2022 snapshot. Null when the transport gives none, which is itself
    /// worth knowing.
    /// </remarks>
    public DateTimeOffset? UpstreamLastModified { get; private set; }

    public int RegionCount { get; private set; }

    public int ProvinceCount { get; private set; }

    public int CityCount { get; private set; }

    public int MunicipalityCount { get; private set; }

    /// <summary>What the operator or the importer observed about this edition. Rendered to readers.</summary>
    public string? Notes { get; private set; }

    // ── ACQUISITION CHAIN ────────────────────────────────────────────────────
    //
    // Enough to answer "what exactly did we load, from where, and can we prove it hasn't changed?"
    // without opening the file. A register edition is the foundation every canonical code rests on, so
    // its provenance has to be as auditable as a magnitude's agency.

    /// <summary>
    /// The date the PSA states the publication is as of, where the operator supplies it.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="RetrievedAt"/>, and the distinction matters: the PSGC changes quarterly,
    /// so "as of 30 June 2026" and "downloaded on 15 September 2026" answer different questions. Held as
    /// a date rather than an instant because a publication is dated to a day.
    /// </remarks>
    public DateOnly? PublicationDate { get; private set; }

    /// <summary>The file name as the publisher named it, kept verbatim.</summary>
    /// <remarks>
    /// Not cosmetic. The PSA encodes the quarter in the file name, so this is often the only place the
    /// edition's own identity survives once the bytes are parsed — and it is what lets a reviewer check
    /// that the hash below belongs to the publication they think it does.
    /// </remarks>
    public string? OriginalFileName { get; private set; }

    /// <summary>Lowercase hex SHA-256 of the acquired file, exactly as it was read.</summary>
    /// <remarks>
    /// Of the original bytes, never of a converted copy. That is why the importer reads the PSA's own
    /// workbook rather than asking an operator to export a CSV first: a hash over a derived file
    /// describes something the PSA never published, which would make the whole chain decorative.
    /// </remarks>
    public string? FileSha256 { get; private set; }

    /// <summary>How the file was obtained, in the operator's own words.</summary>
    /// <remarks>
    /// A declaration rather than a measurement, and it has to be. A file on disk carries no provenance
    /// of its own; inferring one would let a downloaded mirror be presented as a PSA publication, which
    /// is precisely the substitution gate 1 exists to prevent.
    /// </remarks>
    public string? AcquisitionNote { get; private set; }

    // ── SUPERSESSION ─────────────────────────────────────────────────────────

    /// <summary>When a later edition replaced this one. Null while this edition is current.</summary>
    /// <remarks>
    /// Superseded rather than deleted, for the same reason a rejected pairing is retained: the run made
    /// against this edition produced figures, and those figures have to remain explainable after the
    /// edition behind them is replaced.
    /// </remarks>
    public DateTimeOffset? SupersededAt { get; private set; }

    public Guid? SupersededByEditionId { get; private set; }

    /// <summary>Whether this edition is the one the platform is currently reconciled to.</summary>
    public bool IsCurrent => SupersededAt is null;

    /// <summary>
    /// Whether this edition may be cited by a confirmed crosswalk row.
    /// </summary>
    /// <remarks>
    /// Derived rather than stored, so it cannot be set to true by an import that wants to proceed.
    /// A mirror can be loaded, matched against and reported on; it cannot certify a crosswalk.
    /// </remarks>
    public bool IsCitableAsAuthority => Provenance == RegisterProvenance.PsaDirect;

    public static Result<PsgcRegisterEdition> Create(
        string label,
        RegisterProvenance provenance,
        string accessRoute,
        Guid dataSourceId,
        DateTimeOffset retrievedAt,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return Result<PsgcRegisterEdition>.Failure(PsgcRegisterEditionErrors.LabelRequired);
        }

        if (provenance == RegisterProvenance.Unknown)
        {
            return Result<PsgcRegisterEdition>.Failure(PsgcRegisterEditionErrors.ProvenanceRequired);
        }

        if (string.IsNullOrWhiteSpace(accessRoute))
        {
            return Result<PsgcRegisterEdition>.Failure(PsgcRegisterEditionErrors.AccessRouteRequired);
        }

        if (dataSourceId == Guid.Empty)
        {
            return Result<PsgcRegisterEdition>.Failure(PsgcRegisterEditionErrors.SourceRequired);
        }

        return Result<PsgcRegisterEdition>.Success(new PsgcRegisterEdition(
            Guid.CreateVersion7(),
            label.Trim(),
            provenance,
            accessRoute.Trim(),
            dataSourceId,
            retrievedAt,
            now));
    }

    /// <summary>Records what the import actually counted, and any staleness signal the transport gave.</summary>
    public PsgcRegisterEdition WithObservedComposition(
        int regionCount,
        int provinceCount,
        int cityCount,
        int municipalityCount,
        DateTimeOffset? upstreamLastModified,
        string? notes)
    {
        RegionCount = regionCount;
        ProvinceCount = provinceCount;
        CityCount = cityCount;
        MunicipalityCount = municipalityCount;
        UpstreamLastModified = upstreamLastModified;
        Notes = notes;

        return this;
    }

    /// <summary>
    /// Records the acquisition chain: what the file was called, when it was published, its hash, and how
    /// it was obtained.
    /// </summary>
    public PsgcRegisterEdition WithAcquisition(
        DateOnly? publicationDate,
        string? originalFileName,
        string? fileSha256,
        string? acquisitionNote)
    {
        PublicationDate = publicationDate;
        OriginalFileName = string.IsNullOrWhiteSpace(originalFileName) ? null : originalFileName.Trim();
        FileSha256 = string.IsNullOrWhiteSpace(fileSha256) ? null : fileSha256.Trim().ToLowerInvariant();
        AcquisitionNote = string.IsNullOrWhiteSpace(acquisitionNote) ? null : acquisitionNote.Trim();

        return this;
    }

    /// <summary>
    /// Marks this edition as replaced by a later one.
    /// </summary>
    /// <remarks>
    /// Idempotent and one-way: an edition already superseded stays pointing at the edition that first
    /// replaced it, because rewriting that pointer would lose the order in which editions arrived.
    /// </remarks>
    public void Supersede(Guid supersededByEditionId, DateTimeOffset now)
    {
        if (SupersededAt is not null || supersededByEditionId == Id)
        {
            return;
        }

        SupersededAt = now;
        SupersededByEditionId = supersededByEditionId;
        Touch(now);
    }
}
