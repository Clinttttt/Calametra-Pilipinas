namespace Calametra.Infrastructure.Sources.Psgc;

/// <summary>
/// Where the PSGC register is read from, and what that route may be trusted to certify.
/// </summary>
/// <remarks>
/// Two routes, deliberately not equivalent. <see cref="PsaApiBaseUrl"/> is the Philippine Statistics
/// Authority's own classification API and the only route that satisfies ADR-005's first gate condition.
/// <see cref="MirrorBaseUrl"/> is a third-party copy: it can exercise the pipeline and produce
/// reconciliation figures, and the domain will refuse to let a crosswalk row cite it.
/// <para>
/// <see cref="LocalFileDirectory"/> takes precedence over both when set, so an operator who has
/// downloaded a PSA publication can load it and state what it is.
/// </para>
/// </remarks>
public sealed class PsgcRegisterOptions
{
    public const string Slug = "psa-psgc";

    public const string SectionName = "Sources:PsgcRegister";

    /// <summary>
    /// The PSA's own classification API.
    /// </summary>
    /// <remarks>
    /// Measured 2026-09-15: the root responds 200 and advertises <c>api/regions</c>,
    /// <c>api/provinces</c>, <c>api/municipalities</c> and <c>api/all</c>, but every one of those
    /// returned 400 to scripted access and the site root returned 403. Kept configured and attempted
    /// first, because the moment it answers, gate 1 becomes satisfiable without a code change.
    /// </remarks>
    public string PsaApiBaseUrl { get; init; } = "https://classification.psa.gov.ph/psgc/api";

    /// <summary>A third-party copy of the register, used only when the PSA route does not answer.</summary>
    public string MirrorBaseUrl { get; init; } = "https://psgc.gitlab.io/api";

    /// <summary>
    /// A directory holding <c>regions.json</c>, <c>provinces.json</c> and
    /// <c>cities-municipalities.json</c> downloaded by an operator. Takes precedence when set.
    /// </summary>
    public string? LocalFileDirectory { get; init; }

    /// <summary>
    /// What the operator states a local file is. Ignored unless <see cref="LocalFileDirectory"/> is set.
    /// </summary>
    /// <remarks>
    /// Deliberately an operator declaration rather than an inference. A file on disk carries no
    /// provenance of its own, and guessing would let a downloaded mirror be presented as a PSA
    /// publication — which is precisely the substitution gate 1 exists to prevent.
    /// </remarks>
    public bool LocalFileIsPsaPublication { get; init; }

    /// <summary>How the operator labels the local file, e.g. <c>PSGC 2Q 2026</c>.</summary>
    public string? LocalFileLabel { get; init; }

    /// <summary>
    /// A single PSA publication file — the workbook as downloaded — read in preference to a directory.
    /// </summary>
    /// <remarks>
    /// The route ADR-005 gate 1 is actually completed through. The PSA distributes the PSGC as an
    /// <c>.xlsx</c> workbook and its site refuses automated download: measured 2026-09-15,
    /// <c>psa.gov.ph</c> returned 403 to two different clients and the classification API's own
    /// endpoints returned 400. A browser session passes; a script does not. So the file arrives by hand,
    /// and this is where it is pointed at.
    /// </remarks>
    public string? LocalPublicationFile { get; init; }

    /// <summary>
    /// The date the PSA states the publication is as of, e.g. <c>2026-06-30</c>.
    /// </summary>
    /// <remarks>
    /// Supplied by the operator because it is on the publication's own cover rather than in its data, and
    /// it is not the download date. The register changes quarterly, so which quarter this is decides
    /// whether a reconciliation is current.
    /// </remarks>
    public DateOnly? LocalFilePublicationDate { get; init; }

    /// <summary>How the file was obtained, in the operator's words. Recorded verbatim.</summary>
    public string? LocalFileAcquisitionNote { get; init; }

    /// <summary>
    /// The worksheet holding the masterlist. Null takes the first sheet.
    /// </summary>
    /// <remarks>
    /// Named rather than assumed, because the PSA workbook carries several sheets and which one holds
    /// the masterlist has changed between publications.
    /// </remarks>
    public string? LocalFileWorksheet { get; init; }
}
