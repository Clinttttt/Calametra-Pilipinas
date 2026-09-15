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
}
