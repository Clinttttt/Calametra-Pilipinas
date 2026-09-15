using Calametra.Domain.Abstractions;

namespace Calametra.Domain.Administrative;

/// <summary>
/// Which side of the crosswalk has no counterpart. See ADR-005 D5.
/// </summary>
/// <remarks>
/// <para>
/// Both directions are real and they are not symmetrical.
/// </para>
/// <para>
/// A register unit with no historical code covers two situations that are one fact for the crosswalk's
/// purposes: the gazetteer has no row at all — the eight Bangsamoro Special Geographic Area
/// municipalities, created by plebiscite in 2024 — or it has a row that carries no PSGC code, which is
/// the case for BARMM, Basilan, Cotabato City and both Maguindanaos. Either way there is no nine-digit
/// code to pair, and the crosswalk pairs codes.
/// </para>
/// <para>
/// A directory row with no register unit is the reverse: a code the gazetteer publishes that no unit in
/// the register claims. Manila's fourteen districts, which the PSGC classifies below the city.
/// </para>
/// </remarks>
public enum CrosswalkExceptionKind
{
    Unknown = 0,

    /// <summary>A unit the register holds for which no nine-digit code can be paired.</summary>
    RegisterUnitHasNoHistoricalCode = 1,

    /// <summary>A directory row carrying a code no unit in the register claims.</summary>
    DirectoryRowHasNoRegisterUnit = 2,
}

public static class LguCrosswalkExceptionErrors
{
    public static readonly Error KindRequired = new(
        ErrorType.Validation,
        "crosswalk_exception.kind_required",
        "An exception must say which side of the crosswalk has no counterpart.");

    public static readonly Error SubjectRequired = new(
        ErrorType.Validation,
        "crosswalk_exception.subject_required",
        "An exception must identify the unit or directory row it excuses.");

    public static readonly Error ReasonRequired = new(
        ErrorType.Validation,
        "crosswalk_exception.reason_required",
        "An exception requires a written reason. ADR-005 D5 makes the reason the thing that is rendered "
        + "to the reader, so an exception without one is a silent gap in the crosswalk.");

    public static readonly Error ReasonTooShort = new(
        ErrorType.Validation,
        "crosswalk_exception.reason_too_short",
        "The reason must explain why the pairing cannot be made. A few characters can satisfy 'not "
        + "blank' without explaining anything, and this reason is published.");

    public static readonly Error AcceptedByRequired = new(
        ErrorType.Validation,
        "crosswalk_exception.accepted_by_required",
        "An exception is a person's decision and requires their name.");

    public static readonly Error EditionRequired = new(
        ErrorType.Validation,
        "crosswalk_exception.edition_required",
        "An exception must cite the register edition it was accepted against.");

    public static readonly Error EditionNotCitable = new(
        ErrorType.Validation,
        "crosswalk_exception.edition_not_citable",
        "Only a PSA-direct edition may certify that a unit has no counterpart. Accepting an exception "
        + "against a mirror would be excusing a gap that the mirror's own staleness may have caused.");

    public static readonly Error NotFound = new(
        ErrorType.NotFound,
        "crosswalk_exception.not_found",
        "The accepted exception was not found.");
}

/// <summary>
/// A reviewed, written record that one side of the crosswalk has no counterpart — and why.
/// </summary>
/// <remarks>
/// <para>
/// <b>ADR-005 D5 makes ambiguity first-class:</b> "An unpaired unit is valid, not broken." The failure
/// this type prevents is the tempting one — forcing a match to make a count look complete. Cotabato City
/// is in BARMM and geographically inside SOCCSKSARGEN; Maguindanao del Norte and del Sur both descend
/// from a province whose nine-digit code was never split. Any pairing invented for these would be a
/// fabricated fact about Philippine administrative geography, and it would be indistinguishable from a
/// real one once stored.
/// </para>
/// <para>
/// <b>Not a link row, and that is a deliberate departure from D5's wording.</b> D5 describes an exception
/// as "a row in the crosswalk with a <c>Reason</c>". A <see cref="LguCodeLink"/> cannot express these
/// cases: it requires a nine-digit code to pair with, and the whole assertion here is that no such code
/// exists. Storing a placeholder code to fit the shape would put a fiction in the column every join
/// reads. The reason is still first-class and still published, which is what D5 was protecting.
/// </para>
/// <para>
/// Held to the same evidentiary standard as a confirmation: a named person, a written reason, and a
/// PSA-direct edition. "No counterpart exists" is a claim about the register, so it can only be made
/// against a register entitled to be cited.
/// </para>
/// </remarks>
public sealed class LguCrosswalkException : AuditableEntity
{
    /// <summary>
    /// A reason must be at least this long. Chosen because the reasons this excuses are published to
    /// readers, and "n/a", "none" and "-" all pass a blank check while explaining nothing.
    /// </summary>
    private const int MinimumReasonLength = 20;

    private LguCrosswalkException()
    {
    }

    private LguCrosswalkException(
        Guid id,
        CrosswalkExceptionKind kind,
        Guid? lguId,
        string? canonicalPsgcCode,
        Guid? placeId,
        string? directoryPsgcCode,
        string subjectName,
        string reason,
        string acceptedBy,
        Guid registerEditionId,
        DateTimeOffset now)
        : base(id, now)
    {
        Kind = kind;
        LguId = lguId;
        CanonicalPsgcCode = canonicalPsgcCode;
        PlaceId = placeId;
        DirectoryPsgcCode = directoryPsgcCode;
        SubjectName = subjectName;
        Reason = reason;
        AcceptedBy = acceptedBy;
        AcceptedAt = now;
        RegisterEditionId = registerEditionId;
    }

    public CrosswalkExceptionKind Kind { get; private set; }

    /// <summary>The canonical unit excused, when the register side has no counterpart.</summary>
    public Guid? LguId { get; private set; }

    /// <summary>
    /// The unit's ten-digit code, held alongside the identifier.
    /// </summary>
    /// <remarks>
    /// Denormalised on purpose. The exception must stay readable after a later edition recodes or retires
    /// the unit, which is precisely the situation these exceptions arise from.
    /// </remarks>
    public string? CanonicalPsgcCode { get; private set; }

    /// <summary>The gazetteer row excused, when the directory side has no counterpart.</summary>
    public Guid? PlaceId { get; private set; }

    /// <summary>The nine-digit code that row carries.</summary>
    public string? DirectoryPsgcCode { get; private set; }

    /// <summary>What the excused thing is called, so the record reads without a join.</summary>
    public string SubjectName { get; private set; } = string.Empty;

    /// <summary>Why no pairing can be made. Published, per ADR-005 D5.</summary>
    public string Reason { get; private set; } = string.Empty;

    public string AcceptedBy { get; private set; } = string.Empty;

    public DateTimeOffset AcceptedAt { get; private set; }

    /// <summary>The PSA-direct edition this was accepted against.</summary>
    public Guid RegisterEditionId { get; private set; }

    /// <summary>
    /// Accepts that a unit the register holds has no directory row to pair with.
    /// </summary>
    public static Result<LguCrosswalkException> ForRegisterUnit(
        Lgu unit,
        string reason,
        string acceptedBy,
        PsgcRegisterEdition edition,
        DateTimeOffset now)
    {
        if (unit is null)
        {
            return Result<LguCrosswalkException>.Failure(LguCrosswalkExceptionErrors.SubjectRequired);
        }

        var guard = Validate(reason, acceptedBy, edition);

        if (guard.IsFailure)
        {
            return Result<LguCrosswalkException>.Failure(guard.Error!);
        }

        return Result<LguCrosswalkException>.Success(new LguCrosswalkException(
            Guid.CreateVersion7(),
            CrosswalkExceptionKind.RegisterUnitHasNoHistoricalCode,
            unit.Id,
            unit.CanonicalPsgcCode,
            placeId: null,
            directoryPsgcCode: null,
            unit.Name,
            reason.Trim(),
            acceptedBy.Trim(),
            edition!.Id,
            now));
    }

    /// <summary>
    /// Accepts that a directory row carries a code no unit in the register claims.
    /// </summary>
    /// <remarks>
    /// Takes the identifier and code rather than the gazetteer entity, because <c>Place</c> belongs to a
    /// different part of the model and the administrative side has no business holding a reference to it.
    /// </remarks>
    public static Result<LguCrosswalkException> ForDirectoryRow(
        Guid placeId,
        string directoryPsgcCode,
        string subjectName,
        string reason,
        string acceptedBy,
        PsgcRegisterEdition edition,
        DateTimeOffset now)
    {
        if (placeId == Guid.Empty || string.IsNullOrWhiteSpace(directoryPsgcCode))
        {
            return Result<LguCrosswalkException>.Failure(LguCrosswalkExceptionErrors.SubjectRequired);
        }

        var guard = Validate(reason, acceptedBy, edition);

        if (guard.IsFailure)
        {
            return Result<LguCrosswalkException>.Failure(guard.Error!);
        }

        return Result<LguCrosswalkException>.Success(new LguCrosswalkException(
            Guid.CreateVersion7(),
            CrosswalkExceptionKind.DirectoryRowHasNoRegisterUnit,
            lguId: null,
            canonicalPsgcCode: null,
            placeId,
            directoryPsgcCode.Trim(),
            string.IsNullOrWhiteSpace(subjectName) ? directoryPsgcCode.Trim() : subjectName.Trim(),
            reason.Trim(),
            acceptedBy.Trim(),
            edition!.Id,
            now));
    }

    private static Result Validate(string reason, string acceptedBy, PsgcRegisterEdition? edition)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(LguCrosswalkExceptionErrors.ReasonRequired);
        }

        if (reason.Trim().Length < MinimumReasonLength)
        {
            return Result.Failure(LguCrosswalkExceptionErrors.ReasonTooShort);
        }

        if (string.IsNullOrWhiteSpace(acceptedBy))
        {
            return Result.Failure(LguCrosswalkExceptionErrors.AcceptedByRequired);
        }

        if (edition is null)
        {
            return Result.Failure(LguCrosswalkExceptionErrors.EditionRequired);
        }

        if (!edition.IsCitableAsAuthority)
        {
            return Result.Failure(LguCrosswalkExceptionErrors.EditionNotCitable);
        }

        return Result.Success();
    }
}
