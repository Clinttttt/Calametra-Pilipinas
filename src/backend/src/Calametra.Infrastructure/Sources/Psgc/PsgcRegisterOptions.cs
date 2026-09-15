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
}
