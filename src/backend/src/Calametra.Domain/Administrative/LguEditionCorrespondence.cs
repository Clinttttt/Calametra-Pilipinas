using Calametra.Domain.Abstractions;

namespace Calametra.Domain.Administrative;

public static class LguEditionCorrespondenceErrors
{
    public static readonly Error LegacyCodeRequired = new(
        ErrorType.Validation,
        "edition_correspondence.legacy_code_required",
        "A correspondence requires the ten-digit code the earlier register edition used.");

    public static readonly Error LegacyCodeLength = new(
        ErrorType.Validation,
        "edition_correspondence.legacy_code_length",
        "The legacy code must be ten digits. A correspondence pairs two editions of the ten-digit "
        + "register; a nine-digit code is the crosswalk's business, not this one's.");

    public static readonly Error CurrentUnitRequired = new(
        ErrorType.Validation,
        "edition_correspondence.current_unit_required",
        "A correspondence requires the current unit it resolves to.");

    public static readonly Error SameCode = new(
        ErrorType.Validation,
        "edition_correspondence.same_code",
        "A code does not correspond to itself. If the active edition already uses this code, geometry "
        + "attaches directly and no correspondence is needed.");

    public static readonly Error ProposedByRequired = new(
        ErrorType.Validation,
        "edition_correspondence.proposed_by_required",
        "A proposal must name what produced it.");

    public static readonly Error EditionRequired = new(
        ErrorType.Validation,
        "edition_correspondence.edition_required",
        "A correspondence is a claim about two specific register editions and must name the one it was "
        + "proposed against.");

    public static readonly Error NotProposed = new(
        ErrorType.Validation,
        "edition_correspondence.not_proposed",
        "Only a proposed correspondence can be reviewed. Confirmation is one-way.");

    public static readonly Error EvidenceRequired = new(
        ErrorType.Validation,
        "edition_correspondence.evidence_required",
        "Confirming a correspondence requires stating what was actually checked.");

    public static readonly Error EvidenceNotApplicable = new(
        ErrorType.Validation,
        "edition_correspondence.evidence_not_applicable",
        "A correspondence may only be confirmed on EditionCorrespondence or ManualReview. RegisterMatch "
        + "and DigitReslice describe a nine-digit pairing, which is a different claim.");

    public static readonly Error ReviewerRequired = new(
        ErrorType.Validation,
        "edition_correspondence.reviewer_required",
        "A correspondence is a person's decision and requires their name.");

    public static readonly Error BridgeRequired = new(
        ErrorType.Validation,
        "edition_correspondence.bridge_required",
        "EditionCorrespondence evidence means the register itself published the link — the same nine-digit "
        + "code for both editions, resolved through a confirmed pairing. A proposal that reached its "
        + "target by no such bridge cannot be confirmed on it.");

    public static readonly Error NamesMustAgree = new(
        ErrorType.Validation,
        "edition_correspondence.names_must_agree",
        "EditionCorrespondence may only be confirmed where the two editions' names agree. Where they do "
        + "not, a person decides from named sources and records why — that is ManualReview.");

    public static readonly Error ReasonRequiredForManualReview = new(
        ErrorType.Validation,
        "edition_correspondence.reason_required",
        "Manual review requires a written reason. It is the only record of why this pairing was accepted "
        + "when the mechanical evidence did not settle it.");

    public static readonly Error ReasonTooShort = new(
        ErrorType.Validation,
        "edition_correspondence.reason_too_short",
        "The reason must explain the decision. A few characters satisfy 'not blank' without explaining "
        + "anything, and this reason is the entire justification for the pairing.");

    public static readonly Error RejectionNeedsReason = new(
        ErrorType.Validation,
        "edition_correspondence.rejection_needs_reason",
        "A rejected correspondence needs a reason, so the refusal rate stays explainable.");

    public static readonly Error NotFound = new(
        ErrorType.NotFound,
        "edition_correspondence.not_found",
        "The proposed correspondence was not found.");
}

/// <summary>
/// A reviewed claim that a ten-digit code from an earlier register edition means a unit in this one.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The crosswalk pairs the current ten-digit register with the pre-2019 nine-digit
/// one. It cannot help when an external dataset is keyed to a <em>different edition of the ten-digit
/// register</em> — which is the ordinary case, because the PSA recodes units whenever the map of regions
/// changes. Between the edition the national boundary set uses and PSA 2Q 2026, the Negros Island Region
/// was created, Sulu moved to Region IX, the highly urbanised cities were recoded out of their provinces,
/// and Maguindanao's halves were renumbered. 142 codes changed while the places did not.
/// </para>
/// <para>
/// <b>Why it is reviewed rather than computed.</b> The recodings look regular enough to invite a prefix
/// rule, and a prefix rule would have been wrong about the capital: the boundary set codes the City of
/// Manila <c>1303901000</c>, whose re-slice is <c>133901000</c> — Tondo, one district of it, and an
/// accepted sub-city exception in this platform. A rule would have attached Manila's outline to Tondo and
/// looked entirely plausible doing it.
/// </para>
/// <para>
/// <b>What counts as evidence.</b> <see cref="LguLinkEvidence.EditionCorrespondence"/> means the register
/// published the link itself: both editions state the same nine-digit code for the unit, and a
/// <em>confirmed</em> pairing resolves that code to a current unit. The arithmetic is only how the
/// candidate was found; the reviewed pairing is what makes it true, and the names must agree.
/// <see cref="LguLinkEvidence.ManualReview"/> is a person deciding from named sources with a written
/// reason, and it is the only route where the names differ.
/// </para>
/// </remarks>
public sealed class LguEditionCorrespondence : AuditableEntity
{
    private const int MinimumReasonLength = 20;

    private LguEditionCorrespondence()
    {
    }

    private LguEditionCorrespondence(
        Guid id,
        string legacyCanonicalPsgcCode,
        string? legacyName,
        Guid currentLguId,
        string currentCanonicalPsgcCode,
        string proposalBasis,
        bool namesAgree,
        bool hasRegisterBridge,
        string proposedBy,
        Guid proposedAgainstEditionId,
        DateTimeOffset now)
        : base(id, now)
    {
        LegacyCanonicalPsgcCode = legacyCanonicalPsgcCode;
        LegacyName = legacyName;
        CurrentLguId = currentLguId;
        CurrentCanonicalPsgcCode = currentCanonicalPsgcCode;
        ProposalBasis = proposalBasis;
        NamesAgree = namesAgree;
        HasRegisterBridge = hasRegisterBridge;
        ProposedBy = proposedBy;
        ProposedAgainstEditionId = proposedAgainstEditionId;
        ProposedAt = now;
        Status = LguLinkStatus.Proposed;
    }

    /// <summary>The ten-digit code the earlier edition used.</summary>
    public string LegacyCanonicalPsgcCode { get; private set; } = string.Empty;

    /// <summary>What the legacy dataset calls the unit. Corroboration, never identity.</summary>
    public string? LegacyName { get; private set; }

    public Guid CurrentLguId { get; private set; }

    /// <summary>The current unit's code, held alongside the identifier so the row reads without a join.</summary>
    public string CurrentCanonicalPsgcCode { get; private set; } = string.Empty;

    public LguLinkStatus Status { get; private set; }

    public LguLinkEvidence Evidence { get; private set; }

    /// <summary>How the candidate was found, in the matcher's words.</summary>
    public string ProposalBasis { get; private set; } = string.Empty;

    /// <summary>Whether the two editions' names agree after folding.</summary>
    public bool NamesAgree { get; private set; }

    /// <summary>
    /// Whether a unit this platform itself held under the earlier edition supplied the nine-digit code.
    /// </summary>
    /// <remarks>
    /// The stronger of the two bridges, because it is this platform's own record of the earlier register
    /// rather than an arithmetic guess at what that register would have said.
    /// </remarks>
    public bool HasRegisterBridge { get; private set; }

    public string ProposedBy { get; private set; } = string.Empty;

    public DateTimeOffset ProposedAt { get; private set; }

    public Guid ProposedAgainstEditionId { get; private set; }

    public string? ReviewedBy { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    /// <summary>Mandatory on manual review and on rejection.</summary>
    public string? Reason { get; private set; }

    public Guid? ConfirmedAgainstEditionId { get; private set; }

    /// <summary>The only state in which geometry may be attached through this correspondence.</summary>
    public bool IsUsable => Status == LguLinkStatus.Confirmed;

    public static Result<LguEditionCorrespondence> Propose(
        string legacyCanonicalPsgcCode,
        string? legacyName,
        Guid currentLguId,
        string currentCanonicalPsgcCode,
        string proposalBasis,
        bool namesAgree,
        bool hasRegisterBridge,
        string proposedBy,
        Guid proposedAgainstEditionId,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(legacyCanonicalPsgcCode))
        {
            return Result<LguEditionCorrespondence>.Failure(
                LguEditionCorrespondenceErrors.LegacyCodeRequired);
        }

        var legacy = legacyCanonicalPsgcCode.Trim();

        if (legacy.Length != 10 || !legacy.All(char.IsAsciiDigit))
        {
            return Result<LguEditionCorrespondence>.Failure(
                LguEditionCorrespondenceErrors.LegacyCodeLength);
        }

        if (currentLguId == Guid.Empty || string.IsNullOrWhiteSpace(currentCanonicalPsgcCode))
        {
            return Result<LguEditionCorrespondence>.Failure(
                LguEditionCorrespondenceErrors.CurrentUnitRequired);
        }

        var current = currentCanonicalPsgcCode.Trim();

        if (string.Equals(legacy, current, StringComparison.Ordinal))
        {
            return Result<LguEditionCorrespondence>.Failure(LguEditionCorrespondenceErrors.SameCode);
        }

        if (string.IsNullOrWhiteSpace(proposedBy))
        {
            return Result<LguEditionCorrespondence>.Failure(
                LguEditionCorrespondenceErrors.ProposedByRequired);
        }

        if (proposedAgainstEditionId == Guid.Empty)
        {
            return Result<LguEditionCorrespondence>.Failure(
                LguEditionCorrespondenceErrors.EditionRequired);
        }

        return Result<LguEditionCorrespondence>.Success(new LguEditionCorrespondence(
            Guid.CreateVersion7(),
            legacy,
            string.IsNullOrWhiteSpace(legacyName) ? null : legacyName.Trim(),
            currentLguId,
            current,
            string.IsNullOrWhiteSpace(proposalBasis) ? "not recorded" : proposalBasis.Trim(),
            namesAgree,
            hasRegisterBridge,
            proposedBy.Trim(),
            proposedAgainstEditionId,
            now));
    }

    /// <summary>
    /// Accepts the correspondence. The only path by which legacy-keyed geometry becomes attachable.
    /// </summary>
    public Result Confirm(
        LguLinkEvidence evidence,
        string reviewedBy,
        string? reason,
        PsgcRegisterEdition edition,
        DateTimeOffset now)
    {
        if (Status != LguLinkStatus.Proposed)
        {
            return Result.Failure(LguEditionCorrespondenceErrors.NotProposed);
        }

        if (evidence == LguLinkEvidence.Unknown)
        {
            return Result.Failure(LguEditionCorrespondenceErrors.EvidenceRequired);
        }

        if (evidence is not (LguLinkEvidence.EditionCorrespondence or LguLinkEvidence.ManualReview))
        {
            return Result.Failure(LguEditionCorrespondenceErrors.EvidenceNotApplicable);
        }

        if (string.IsNullOrWhiteSpace(reviewedBy))
        {
            return Result.Failure(LguEditionCorrespondenceErrors.ReviewerRequired);
        }

        if (evidence == LguLinkEvidence.EditionCorrespondence)
        {
            // The guard that keeps this from becoming a prefix rule. EditionCorrespondence asserts the
            // register published the link; a proposal that reached its target by arithmetic alone, or whose
            // two editions disagree about the name, has not shown that.
            if (!HasRegisterBridge && !NamesAgree)
            {
                return Result.Failure(LguEditionCorrespondenceErrors.BridgeRequired);
            }

            if (!NamesAgree)
            {
                return Result.Failure(LguEditionCorrespondenceErrors.NamesMustAgree);
            }
        }

        if (evidence == LguLinkEvidence.ManualReview)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return Result.Failure(LguEditionCorrespondenceErrors.ReasonRequiredForManualReview);
            }

            if (reason.Trim().Length < MinimumReasonLength)
            {
                return Result.Failure(LguEditionCorrespondenceErrors.ReasonTooShort);
            }
        }

        if (edition is null)
        {
            return Result.Failure(LguEditionCorrespondenceErrors.EditionRequired);
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

    public Result Reject(string reviewedBy, string reason, DateTimeOffset now)
    {
        if (Status != LguLinkStatus.Proposed)
        {
            return Result.Failure(LguEditionCorrespondenceErrors.NotProposed);
        }

        if (string.IsNullOrWhiteSpace(reviewedBy))
        {
            return Result.Failure(LguEditionCorrespondenceErrors.ReviewerRequired);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(LguEditionCorrespondenceErrors.RejectionNeedsReason);
        }

        Status = LguLinkStatus.Rejected;
        ReviewedBy = reviewedBy.Trim();
        ReviewedAt = now;
        Reason = reason.Trim();
        Touch(now);

        return Result.Success();
    }

    /// <summary>Retires an unreviewed proposal when the edition it was made against is replaced.</summary>
    public Result Supersede(DateTimeOffset now)
    {
        if (Status != LguLinkStatus.Proposed)
        {
            return Result.Failure(LguEditionCorrespondenceErrors.NotProposed);
        }

        Status = LguLinkStatus.Superseded;
        Touch(now);

        return Result.Success();
    }
}
