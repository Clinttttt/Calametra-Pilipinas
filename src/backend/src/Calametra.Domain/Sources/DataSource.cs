using Calametra.Domain.Abstractions;

namespace Calametra.Domain.Sources;

/// <summary>
/// An upstream dataset, its access route, its licensing position, and its known
/// coverage limits.
/// </summary>
/// <remarks>
/// <para>
/// Provenance is a first-class aggregate rather than a few columns hung off other
/// tables, for three reasons specific to this project.
/// </para>
/// <para>
/// First, correctness. The same real earthquake carries different numbers
/// depending on who measured it — PHIVOLCS reports the 10 February 2017 Surigao
/// event as Ms 6.7 at 10 km, USGS reports it as Mww 6.5 at 15 km. Neither is
/// wrong. A single "magnitude" column would force an arbitrary choice and hide
/// the disagreement.
/// </para>
/// <para>
/// Second, honesty about coverage. <see cref="MinimumReliableMagnitude"/> exists
/// because the USGS catalogue holds no Philippine events below M3.5 and effectively
/// begins at M4.0, whereas the PHIVOLCS local network records M2.x events
/// routinely. Any "events today" figure is meaningless without stating which
/// catalogue produced it.
/// </para>
/// <para>
/// Third, licensing. <see cref="IsRedistributable"/> separates "we can reach this
/// endpoint" from "we may store and re-serve its contents". Public reachability
/// is not a licence. Sources awaiting written permission are marked
/// <see langword="false"/> and may only be proxied for display.
/// </para>
/// <para>
/// The About Data page is generated from this aggregate, so documentation cannot
/// drift from what the system actually does.
/// </para>
/// </remarks>
public sealed class DataSource : AuditableEntity
{
    private DataSource()
    {
    }

    private DataSource(
        Guid id,
        string slug,
        string agency,
        string datasetName,
        SourceAccessKind accessKind,
        string attribution,
        DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        Slug = slug;
        Agency = agency;
        DatasetName = datasetName;
        AccessKind = accessKind;
        Attribution = attribution;
    }

    /// <summary>Stable machine key, e.g. <c>usgs-comcat</c>. Used by seeding and ingestion.</summary>
    public string Slug { get; private set; } = string.Empty;

    /// <summary>Publishing body, e.g. <c>DOST-PHIVOLCS</c>, <c>USGS</c>, <c>NOAA NCEI</c>.</summary>
    public string Agency { get; private set; } = string.Empty;

    /// <summary>Dataset name as the publisher calls it, e.g. <c>ComCat</c>, <c>Active Faults</c>.</summary>
    public string DatasetName { get; private set; } = string.Empty;

    public SourceAccessKind AccessKind { get; private set; }

    /// <summary>Attribution text. Rendered wherever data from this source appears.</summary>
    public string Attribution { get; private set; } = string.Empty;

    /// <summary>Landing page or service endpoint for the dataset.</summary>
    public string? SourceUrl { get; private set; }

    /// <summary>Terms of use / licence page, where the publisher provides one.</summary>
    public string? TermsUrl { get; private set; }

    /// <summary>
    /// Whether Calametra may store this source's data locally and serve it onward.
    /// Defaults to <see langword="false"/>: permission must be established, not assumed.
    /// </summary>
    public bool IsRedistributable { get; private set; }

    /// <summary>Whether this is an official Philippine authority for its hazard domain.</summary>
    public bool IsAuthoritativeForPhilippines { get; private set; }

    /// <summary>
    /// Magnitude below which this catalogue is known to be incomplete for the study
    /// area. Displayed to users beside any event count derived from this source.
    /// </summary>
    public double? MinimumReliableMagnitude { get; private set; }

    /// <summary>Plain-language statement of what this source does and does not cover.</summary>
    public string? CoverageNotes { get; private set; }

    /// <summary>Publisher's own version or edition string, where one exists.</summary>
    public string? SourceVersion { get; private set; }

    /// <summary>When Calametra last successfully read from this source.</summary>
    public DateTimeOffset? LastRetrievedAt { get; private set; }

    /// <summary>Hash of the last payload ingested, for change detection on bulk files.</summary>
    public string? LastPayloadChecksum { get; private set; }

    public static Result<DataSource> Create(
        string slug,
        string agency,
        string datasetName,
        SourceAccessKind accessKind,
        string attribution,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return Result<DataSource>.Failure(DataSourceErrors.SlugRequired);
        }

        if (string.IsNullOrWhiteSpace(agency))
        {
            return Result<DataSource>.Failure(DataSourceErrors.AgencyRequired);
        }

        if (string.IsNullOrWhiteSpace(datasetName))
        {
            return Result<DataSource>.Failure(DataSourceErrors.DatasetNameRequired);
        }

        if (string.IsNullOrWhiteSpace(attribution))
        {
            return Result<DataSource>.Failure(DataSourceErrors.AttributionRequired);
        }

        var source = new DataSource(
            Guid.CreateVersion7(),
            slug.Trim(),
            agency.Trim(),
            datasetName.Trim(),
            accessKind,
            attribution.Trim(),
            now);

        return Result<DataSource>.Success(source);
    }

    public DataSource WithLinks(string? sourceUrl, string? termsUrl)
    {
        SourceUrl = sourceUrl;
        TermsUrl = termsUrl;
        return this;
    }

    public DataSource WithCoverage(double? minimumReliableMagnitude, string? coverageNotes)
    {
        MinimumReliableMagnitude = minimumReliableMagnitude;
        CoverageNotes = coverageNotes;
        return this;
    }

    public DataSource WithPermissions(bool isRedistributable, bool isAuthoritativeForPhilippines)
    {
        IsRedistributable = isRedistributable;
        IsAuthoritativeForPhilippines = isAuthoritativeForPhilippines;
        return this;
    }

    /// <summary>Records a successful read, for the "last synchronised" indicator.</summary>
    public void RecordRetrieval(DateTimeOffset now, string? payloadChecksum = null, string? sourceVersion = null)
    {
        LastRetrievedAt = now;

        if (payloadChecksum is not null)
        {
            LastPayloadChecksum = payloadChecksum;
        }

        if (sourceVersion is not null)
        {
            SourceVersion = sourceVersion;
        }

        Touch(now);
    }
}
