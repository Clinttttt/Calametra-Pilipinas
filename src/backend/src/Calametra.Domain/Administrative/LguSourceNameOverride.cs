using Calametra.Domain.Abstractions;

namespace Calametra.Domain.Administrative;

public static class LguSourceNameOverrideErrors
{
    public static readonly Error CodeRequired = new(
        ErrorType.Validation,
        "source_name_override.code_required",
        "An override applies to one canonical ten-digit code.");

    public static readonly Error SourceNameRequired = new(
        ErrorType.Validation,
        "source_name_override.source_name_required",
        "An override must quote the publisher's name verbatim. It excuses one specific disagreement, not "
        + "any future disagreement about the same unit.");

    public static readonly Error ReviewerRequired = new(
        ErrorType.Validation,
        "source_name_override.reviewer_required",
        "An override is a person's decision and requires their name.");

    public static readonly Error ReasonRequired = new(
        ErrorType.Validation,
        "source_name_override.reason_required",
        "An override requires written evidence that the publisher's name is a superseded name for this "
        + "unit. Without it the override is code equality by another route, which is what the guard exists "
        + "to refuse.");

    public static readonly Error ReasonTooShort = new(
        ErrorType.Validation,
        "source_name_override.reason_too_short",
        "The reason must name the evidence. A few characters satisfy 'not blank' while establishing "
        + "nothing.");

    public static readonly Error EditionNotCitable = new(
        ErrorType.Validation,
        "source_name_override.edition_not_citable",
        "Only a PSA-direct edition may certify that a name is superseded, since the register is what "
        + "superseded it.");
}

/// <summary>
/// A reviewed finding that one publisher's name for one unit is a superseded name.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately narrow.</b> The boundary import requires a matching canonical code <em>and</em> an
/// agreeing name, because COD-AB and PSA 2Q 2026 both number Maguindanao's municipalities in the 1908 block
/// and disagree about which number is which — sixteen outlines attached to the wrong unit on exact code
/// equality before the name check existed. That guard is not relaxed here.
/// </para>
/// <para>
/// What this permits is one exception at a time, for one quoted name, with evidence that the publisher is
/// using a name the PSA has since replaced. Four such cases exist in this vintage: the register writes
/// "Leon T. Postigo" where COD-AB writes "Bacungan (Leon T. Postigo)", "Sawata" where COD-AB writes "San
/// Isidro", and expanded official forms where COD-AB keeps the short one. Each is a rename, not a different
/// municipality, and each must be said so in writing by a named person.
/// </para>
/// <para>
/// The publisher's name is stored verbatim so the override cannot silently widen: if a later vintage renames
/// the unit again, that new name does not match this row and the guard applies afresh.
/// </para>
/// </remarks>
public sealed class LguSourceNameOverride : AuditableEntity
{
    private const int MinimumReasonLength = 40;

    private LguSourceNameOverride()
    {
    }

    private LguSourceNameOverride(
        Guid id,
        string canonicalPsgcCode,
        string sourceName,
        string registerName,
        string reviewedBy,
        string reason,
        Guid confirmedAgainstEditionId,
        DateTimeOffset now)
        : base(id, now)
    {
        CanonicalPsgcCode = canonicalPsgcCode;
        SourceName = sourceName;
        RegisterName = registerName;
        ReviewedBy = reviewedBy;
        Reason = reason;
        ConfirmedAgainstEditionId = confirmedAgainstEditionId;
        ReviewedAt = now;
    }

    public string CanonicalPsgcCode { get; private set; } = string.Empty;

    /// <summary>The publisher's name, verbatim. Matching is exact, so the override cannot widen.</summary>
    public string SourceName { get; private set; } = string.Empty;

    /// <summary>The register's name at the time of review, for the record.</summary>
    public string RegisterName { get; private set; } = string.Empty;

    public string ReviewedBy { get; private set; } = string.Empty;

    /// <summary>The evidence that the publisher's name is superseded.</summary>
    public string Reason { get; private set; } = string.Empty;

    public DateTimeOffset ReviewedAt { get; private set; }

    public Guid ConfirmedAgainstEditionId { get; private set; }

    public static Result<LguSourceNameOverride> Confirm(
        string canonicalPsgcCode,
        string sourceName,
        string registerName,
        string reviewedBy,
        string reason,
        PsgcRegisterEdition edition,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(canonicalPsgcCode)
            || canonicalPsgcCode.Trim().Length != 10
            || !canonicalPsgcCode.Trim().All(char.IsAsciiDigit))
        {
            return Result<LguSourceNameOverride>.Failure(LguSourceNameOverrideErrors.CodeRequired);
        }

        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return Result<LguSourceNameOverride>.Failure(LguSourceNameOverrideErrors.SourceNameRequired);
        }

        if (string.IsNullOrWhiteSpace(reviewedBy))
        {
            return Result<LguSourceNameOverride>.Failure(LguSourceNameOverrideErrors.ReviewerRequired);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result<LguSourceNameOverride>.Failure(LguSourceNameOverrideErrors.ReasonRequired);
        }

        if (reason.Trim().Length < MinimumReasonLength)
        {
            return Result<LguSourceNameOverride>.Failure(LguSourceNameOverrideErrors.ReasonTooShort);
        }

        if (edition is null || !edition.IsCitableAsAuthority)
        {
            return Result<LguSourceNameOverride>.Failure(LguSourceNameOverrideErrors.EditionNotCitable);
        }

        return Result<LguSourceNameOverride>.Success(new LguSourceNameOverride(
            Guid.CreateVersion7(),
            canonicalPsgcCode.Trim(),
            sourceName.Trim(),
            string.IsNullOrWhiteSpace(registerName) ? "not recorded" : registerName.Trim(),
            reviewedBy.Trim(),
            reason.Trim(),
            edition.Id,
            now));
    }
}
