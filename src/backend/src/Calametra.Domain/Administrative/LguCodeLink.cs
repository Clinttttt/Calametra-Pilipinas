using Calametra.Domain.Abstractions;

namespace Calametra.Domain.Administrative;

/// <summary>
/// Whether a pairing has been reviewed. See ADR-005 D4.
/// </summary>
/// <remarks>
/// The distinction this platform enforces is between <em>proposing</em> a pairing and
/// <em>establishing</em> one. A matcher may write <see cref="Proposed"/> rows in bulk; only a person,
/// with evidence recorded, may produce a <see cref="Confirmed"/> one. Analytics read confirmed rows and
/// nothing else, which is enforced at the read boundary rather than remembered as a convention.
/// </remarks>
public enum LguLinkStatus
{
    Unknown = 0,

    /// <summary>Written by a matcher. A work queue entry. No figure may be derived from it.</summary>
    Proposed = 1,

    /// <summary>Reviewed by a person against stated evidence. The only readable state.</summary>
    Confirmed = 2,

    /// <summary>Reviewed and refused, with a reason. Retained so the rejection rate is measurable.</summary>
    Rejected = 3,

    /// <summary>
    /// Proposed against a register edition that has since been replaced.
    /// </summary>
    /// <remarks>
    /// Retained rather than deleted, and deliberately not silently re-pointed at the new edition. A
    /// proposal is a claim about two specific editions of the code; when one of them is replaced the
    /// claim has to be remade rather than inherited, because the newer register may pair the unit
    /// differently, may have split it, or may not contain it at all.
    /// </remarks>
    Superseded = 4,
}

/// <summary>
/// What was actually checked to establish a pairing.
/// </summary>
/// <remarks>
/// Ordered by strength. <see cref="RegisterMatch"/> is the register stating both codes for one unit,
/// which is the only kind that needs no interpretation. <see cref="DigitReslice"/> is the rule a matcher
/// can apply and the rule that fails in the capital, so it is confirmable only where the register's own
/// name agrees. <see cref="ManualReview"/> is a person deciding from named sources, and it is the only
/// value that requires a written reason.
/// </remarks>
public enum LguLinkEvidence
{
    Unknown = 0,
    RegisterMatch = 1,
    DigitReslice = 2,
    ManualReview = 3,
}

public static class LguCodeLinkErrors
{
    public static readonly Error LguRequired = new(
        ErrorType.Validation,
        "lgu_link.lgu_required",
        "A crosswalk row requires the canonical unit it pairs.");

    public static readonly Error HistoricalCodeRequired = new(
        ErrorType.Validation,
        "lgu_link.historical_code_required",
        "A crosswalk row requires the historical nine-digit code it pairs.");

    public static readonly Error HistoricalCodeLength = new(
        ErrorType.Validation,
        "lgu_link.historical_code_length",
        "The historical PSGC code must be nine digits.");

    public static readonly Error ProposedByRequired = new(
        ErrorType.Validation,
        "lgu_link.proposed_by_required",
        "A proposal requires the matcher that produced it, including its version.");

    public static readonly Error NotProposed = new(
        ErrorType.Conflict,
        "lgu_link.not_proposed",
        "Only a proposed pairing can be reviewed. A decided row is reopened explicitly, never "
        + "overwritten.");

    public static readonly Error ReviewerRequired = new(
        ErrorType.Validation,
        "lgu_link.reviewer_required",
        "Confirming a pairing requires the reviewer who is accountable for it.");

    public static readonly Error EvidenceRequired = new(
        ErrorType.Validation,
        "lgu_link.evidence_required",
        "Confirming a pairing requires the evidence that was checked.");

    public static readonly Error ReasonRequiredForManualReview = new(
        ErrorType.Validation,
        "lgu_link.reason_required",
        "Confirming on manual review requires the written reason and the sources consulted.");

    public static readonly Error EditionRequired = new(
        ErrorType.Validation,
        "lgu_link.edition_required",
        "Confirming a pairing requires the register edition it was confirmed against.");

    public static readonly Error EditionNotCitable = new(
        ErrorType.Conflict,
        "lgu_link.edition_not_citable",
        "A pairing cannot be confirmed against a register edition that did not come from the PSA. A "
        + "mirror may be matched against and reported on; it cannot certify a crosswalk.");

    public static readonly Error ResliceNeedsNameAgreement = new(
        ErrorType.Conflict,
        "lgu_link.reslice_needs_name_agreement",
        "A digit re-slicing may only be confirmed where the register's own name agrees. Where the "
        + "digits re-slice and the names do not, the row goes to manual review.");

    public static readonly Error RejectionReasonRequired = new(
        ErrorType.Validation,
        "lgu_link.rejection_reason_required",
        "Rejecting a pairing requires the reason, so the rejection rate can be explained and not "
        + "merely counted.");
}

/// <summary>
/// One pairing between a canonical ten-digit unit and a historical nine-digit code.
/// </summary>
/// <remarks>
/// <para>
/// <b>The invariants here are the point of the type.</b> ADR-005 D4 forbids an algorithm from
/// establishing a crosswalk, and the way that is made real is that <see cref="Confirm"/> cannot succeed
/// without a reviewer, an evidence value, a citable register edition, and — for
/// <see cref="LguLinkEvidence.ManualReview"/> — a written reason. A half-filled confirmed row is the
/// failure mode that would make the whole gate decorative, because it would pass every read boundary
/// while carrying no evidence at all. The database repeats these as check constraints; this type is
/// where they are stated once and tested.
/// </para>
/// <para>
/// <b>Rejections are retained.</b> A refused proposal is not deleted, because the proportion of
/// proposals a reviewer rejected is a required figure in the readiness report: if a matcher proposes
/// 1,600 pairings and 40 are wrong, that number is the reason the review gate exists.
/// </para>
/// </remarks>
public sealed class LguCodeLink : AuditableEntity
{
    private LguCodeLink()
    {
    }

    private LguCodeLink(
        Guid id,
        Guid lguId,
        string historicalPsgcCode,
        string proposedBy,
        DateTimeOffset proposedAt,
        DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        LguId = lguId;
        HistoricalPsgcCode = historicalPsgcCode;
        ProposedBy = proposedBy;
        ProposedAt = proposedAt;
        Status = LguLinkStatus.Proposed;
    }

    public Guid LguId { get; private set; }

    /// <summary>The pre-2019 nine-digit code, which is an alias and never an identity.</summary>
    public string HistoricalPsgcCode { get; private set; } = string.Empty;

    /// <summary>
    /// The gazetteer row this pairing reaches, where one was matched.
    /// </summary>
    /// <remarks>
    /// Null is meaningful: the register may hold a unit the directory does not, which is a fact about
    /// Philippine administrative history rather than a defect. Populating it to make a join tidy is
    /// exactly what ADR-005 forbids.
    /// </remarks>
    public Guid? PlaceId { get; private set; }

    public LguLinkStatus Status { get; private set; }

    public LguLinkEvidence Evidence { get; private set; }

    /// <summary>What the matcher observed, so a reviewer starts from a stated basis rather than a guess.</summary>
    public string? ProposalBasis { get; private set; }

    /// <summary>Whether the register's name and the directory's name agree, as the matcher compared them.</summary>
    public bool NamesAgree { get; private set; }

    /// <summary>The matcher and its version. Required: an unattributed proposal cannot be audited.</summary>
    public string ProposedBy { get; private set; } = string.Empty;

    public DateTimeOffset ProposedAt { get; private set; }

    public string? ReviewedBy { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    /// <summary>The reviewer's written reason. Mandatory on manual review and on every rejection.</summary>
    public string? Reason { get; private set; }

    /// <summary>The register edition this pairing was confirmed against.</summary>
    public Guid? ConfirmedAgainstEditionId { get; private set; }

    /// <summary>
    /// The register edition this pairing was <em>proposed</em> against.
    /// </summary>
    /// <remarks>
    /// Recorded so a run can be superseded wholesale when a new edition arrives. Without it there would
    /// be no way to tell a proposal made against the 2022 mirror from one made against a current PSA
    /// publication, and the two are not interchangeable claims.
    /// </remarks>
    public Guid ProposedAgainstEditionId { get; private set; }

    /// <summary>
    /// Whether analytics may read this row.
    /// </summary>
    /// <remarks>
    /// Expressed here as well as at the read boundary, so a caller holding an entity cannot be in doubt.
    /// The boundary itself is a confirmed-only view and a repository that exposes no other read path —
    /// this property is a convenience, not the enforcement.
    /// </remarks>
    public bool IsReadable => Status == LguLinkStatus.Confirmed;

    /// <summary>
    /// Proposes a pairing. The only way a matcher may write.
    /// </summary>
    public static Result<LguCodeLink> Propose(
        Guid lguId,
        string historicalPsgcCode,
        Guid? placeId,
        LguLinkEvidence candidateEvidence,
        string proposalBasis,
        bool namesAgree,
        string proposedBy,
        Guid proposedAgainstEditionId,
        DateTimeOffset now)
    {
        if (lguId == Guid.Empty)
        {
            return Result<LguCodeLink>.Failure(LguCodeLinkErrors.LguRequired);
        }

        if (proposedAgainstEditionId == Guid.Empty)
        {
            return Result<LguCodeLink>.Failure(LguCodeLinkErrors.EditionRequired);
        }

        var code = historicalPsgcCode?.Trim() ?? string.Empty;

        if (code.Length == 0)
        {
            return Result<LguCodeLink>.Failure(LguCodeLinkErrors.HistoricalCodeRequired);
        }

        if (code.Length != 9 || !code.All(char.IsAsciiDigit))
        {
            return Result<LguCodeLink>.Failure(LguCodeLinkErrors.HistoricalCodeLength);
        }

        if (string.IsNullOrWhiteSpace(proposedBy))
        {
            return Result<LguCodeLink>.Failure(LguCodeLinkErrors.ProposedByRequired);
        }

        var link = new LguCodeLink(Guid.CreateVersion7(), lguId, code, proposedBy.Trim(), now, now)
        {
            PlaceId = placeId,
            ProposedAgainstEditionId = proposedAgainstEditionId,
            // The candidate evidence a matcher suggests. Carried so a reviewer can see what rule fired,
            // and deliberately not treated as established: `Confirm` re-states the evidence, because the
            // reviewer is the one who is accountable for it.
            Evidence = candidateEvidence,
            ProposalBasis = string.IsNullOrWhiteSpace(proposalBasis) ? null : proposalBasis.Trim(),
            NamesAgree = namesAgree,
        };

        return Result<LguCodeLink>.Success(link);
    }

    /// <summary>
    /// Confirms a pairing. The only way a row becomes readable.
    /// </summary>
    /// <param name="edition">
    /// The register edition confirmed against. Must be citable as authority — a mirror cannot certify a
    /// crosswalk, however faithful it looks.
    /// </param>
    public Result Confirm(
        LguLinkEvidence evidence,
        string reviewedBy,
        string? reason,
        PsgcRegisterEdition edition,
        DateTimeOffset now)
    {
        if (Status != LguLinkStatus.Proposed)
        {
            return Result.Failure(LguCodeLinkErrors.NotProposed);
        }

        if (evidence == LguLinkEvidence.Unknown)
        {
            return Result.Failure(LguCodeLinkErrors.EvidenceRequired);
        }

        if (string.IsNullOrWhiteSpace(reviewedBy))
        {
            return Result.Failure(LguCodeLinkErrors.ReviewerRequired);
        }

        if (evidence == LguLinkEvidence.ManualReview && string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(LguCodeLinkErrors.ReasonRequiredForManualReview);
        }

        // The guard that matters most in practice. Re-slicing is the one rule a matcher can apply at
        // scale, and it is the rule that fails in the capital: Quezon City is 137404000 against
        // 1381300000 and does not re-slice at all. Allowing a re-slice to be confirmed where the names
        // disagree would let the algorithm establish the crosswalk with a person's name on it.
        if (evidence == LguLinkEvidence.DigitReslice && !NamesAgree)
        {
            return Result.Failure(LguCodeLinkErrors.ResliceNeedsNameAgreement);
        }

        if (edition is null)
        {
            return Result.Failure(LguCodeLinkErrors.EditionRequired);
        }

        if (!edition.IsCitableAsAuthority)
        {
            return Result.Failure(LguCodeLinkErrors.EditionNotCitable);
        }

        Status = LguLinkStatus.Confirmed;
        Evidence = evidence;
        ReviewedBy = reviewedBy.Trim();
        ReviewedAt = now;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        ConfirmedAgainstEditionId = edition.Id;
        Touch(now);

        return Result.Success();
    }

    /// <summary>
    /// Marks an unreviewed proposal as belonging to a replaced edition.
    /// </summary>
    /// <remarks>
    /// Only a <see cref="LguLinkStatus.Proposed"/> row is superseded. A confirmed pairing keeps its
    /// status and its cited edition: it was a reviewed decision against a stated publication, and a
    /// later register arriving does not retract the review — it may make it worth revisiting, which is a
    /// judgement for a person rather than for an import.
    /// </remarks>
    public Result Supersede(DateTimeOffset now)
    {
        if (Status != LguLinkStatus.Proposed)
        {
            return Result.Failure(LguCodeLinkErrors.NotProposed);
        }

        Status = LguLinkStatus.Superseded;
        Touch(now);

        return Result.Success();
    }

    /// <summary>Refuses a pairing, with the reason that makes the rejection rate explainable.</summary>
    public Result Reject(string reviewedBy, string reason, DateTimeOffset now)
    {
        if (Status != LguLinkStatus.Proposed)
        {
            return Result.Failure(LguCodeLinkErrors.NotProposed);
        }

        if (string.IsNullOrWhiteSpace(reviewedBy))
        {
            return Result.Failure(LguCodeLinkErrors.ReviewerRequired);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(LguCodeLinkErrors.RejectionReasonRequired);
        }

        Status = LguLinkStatus.Rejected;
        ReviewedBy = reviewedBy.Trim();
        ReviewedAt = now;
        Reason = reason.Trim();
        Touch(now);

        return Result.Success();
    }
}
