using Calametra.Domain.Abstractions;

namespace Calametra.Domain.Administrative;

/// <summary>
/// The administrative tiers this platform holds as identities, coarsest first.
/// </summary>
/// <remarks>
/// Barangays are deliberately absent. ADR-005 excludes them: their boundary coverage in the open data
/// this platform can use is uneven across the country, and a set that is complete in Metro Manila and
/// sparse in Caraga would make the interface most confident exactly where a national platform should
/// not be. The enum stops where the evidence stops.
/// </remarks>
public enum LguLevel
{
    Unknown = 0,
    Region = 1,
    Province = 2,
    City = 3,
    Municipality = 4,
}

public static class LguErrors
{
    public static readonly Error CanonicalCodeRequired = new(
        ErrorType.Validation,
        "lgu.canonical_code_required",
        "A local government unit requires its canonical ten-digit PSGC code.");

    public static readonly Error CanonicalCodeLength = new(
        ErrorType.Validation,
        "lgu.canonical_code_length",
        "The canonical PSGC code must be ten digits. The nine-digit form is a historical alias, not "
        + "an identity.");

    public static readonly Error NameRequired = new(
        ErrorType.Validation,
        "lgu.name_required",
        "A local government unit requires the name the register gives it.");

    public static readonly Error UnknownLevel = new(
        ErrorType.Validation,
        "lgu.unknown_level",
        "A local government unit requires a known administrative level.");

    public static readonly Error EditionRequired = new(
        ErrorType.Validation,
        "lgu.edition_required",
        "A local government unit requires the register edition it was read from.");

    public static readonly Error NotFound = new(
        ErrorType.NotFound,
        "lgu.not_found",
        "The local government unit was not found.");
}

/// <summary>
/// A local government unit as the current PSGC register defines it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Separate from <c>Place</c>, and not a replacement for it.</b> <c>Place</c> is a gazetteer row: a
/// name, an administrative level and a representative point, carrying the pre-2019 nine-digit code that
/// GeoNames publishes. This is the register's own unit, keyed on the ten-digit code the PSA publishes
/// today. They are related by a reviewed crosswalk and by nothing else — no code here is derived from a
/// code there, in either direction.
/// </para>
/// <para>
/// <b>Why not simply migrate <c>Place.PsgcCode</c> to ten digits.</b> Because the conversion does not
/// exist. For most provincial municipalities the digits re-slice — Adams is <c>012801000</c> in the old
/// edition and <c>0102801000</c> in the new — but Metro Manila was recoded wholesale, where Quezon City
/// is <c>137404000</c> against <c>1381300000</c>. A migration would therefore be right in most of the
/// country and confidently wrong in the capital, which is the failure this platform has already refused
/// once in <c>GeoNamesPlaceSource</c>.
/// </para>
/// <para>
/// Carries no geometry. ADR-005 gates polygon ingestion behind the identity work, and this type exists
/// to complete that gate rather than to anticipate it.
/// </para>
/// </remarks>
public sealed class Lgu : AuditableEntity
{
    private Lgu()
    {
    }

    private Lgu(
        Guid id,
        string canonicalPsgcCode,
        string name,
        LguLevel level,
        Guid registerEditionId,
        DateTimeOffset createdAt)
        : base(id, createdAt)
    {
        CanonicalPsgcCode = canonicalPsgcCode;
        Name = name;
        Level = level;
        RegisterEditionId = registerEditionId;
    }

    /// <summary>The ten-digit PSGC code. The identity, and the value anything durable should cite.</summary>
    public string CanonicalPsgcCode { get; private set; } = string.Empty;

    /// <summary>The name as the register gives it, not as this platform would prefer to render it.</summary>
    public string Name { get; private set; } = string.Empty;

    public LguLevel Level { get; private set; }

    /// <summary>Which edition of the register this unit was read from.</summary>
    public Guid RegisterEditionId { get; private set; }

    /// <summary>
    /// The parent unit's canonical code, forming region → province → city/municipality.
    /// </summary>
    /// <remarks>
    /// Held as a code rather than a foreign key, because a register import reads units in an order it
    /// does not control and a parent may arrive after its child. Resolved on read rather than enforced
    /// on write, which also means an unresolvable parent stays visible as a fact about the register
    /// instead of failing the import.
    /// </remarks>
    public string? ParentCanonicalPsgcCode { get; private set; }

    /// <summary>
    /// The register's own nine-digit code for this unit, where the register states one.
    /// </summary>
    /// <remarks>
    /// This is <em>evidence</em>, not identity, and it is the strongest evidence available: where the
    /// register itself pairs the two editions, a crosswalk row can be confirmed on
    /// <c>RegisterMatch</c> rather than on a re-slicing rule. Null where the register offers no pairing,
    /// which is precisely the population that has to go to manual review.
    /// </remarks>
    public string? RegisterStatedHistoricalCode { get; private set; }

    /// <summary>Whether the register classifies this unit as a city. Read, never inferred from the name.</summary>
    public bool IsCity => Level == LguLevel.City;

    public static Result<Lgu> Create(
        string canonicalPsgcCode,
        string name,
        LguLevel level,
        Guid registerEditionId,
        DateTimeOffset now)
    {
        var code = canonicalPsgcCode?.Trim() ?? string.Empty;

        if (code.Length == 0)
        {
            return Result<Lgu>.Failure(LguErrors.CanonicalCodeRequired);
        }

        // Length is checked rather than assumed. An import fed the nine-digit edition by mistake would
        // otherwise populate a canonical column with historical codes, and every crosswalk row built on
        // top of it would be meaningless while looking correct.
        if (code.Length != 10 || !code.All(char.IsAsciiDigit))
        {
            return Result<Lgu>.Failure(LguErrors.CanonicalCodeLength);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<Lgu>.Failure(LguErrors.NameRequired);
        }

        if (level == LguLevel.Unknown)
        {
            return Result<Lgu>.Failure(LguErrors.UnknownLevel);
        }

        if (registerEditionId == Guid.Empty)
        {
            return Result<Lgu>.Failure(LguErrors.EditionRequired);
        }

        return Result<Lgu>.Success(new Lgu(
            Guid.CreateVersion7(),
            code,
            name.Trim(),
            level,
            registerEditionId,
            now));
    }

    public Lgu WithHierarchy(string? parentCanonicalPsgcCode)
    {
        ParentCanonicalPsgcCode = string.IsNullOrWhiteSpace(parentCanonicalPsgcCode)
            ? null
            : parentCanonicalPsgcCode.Trim();

        return this;
    }

    /// <summary>Records the nine-digit code the register itself states for this unit, if any.</summary>
    public Lgu WithRegisterStatedHistoricalCode(string? historicalCode)
    {
        var code = historicalCode?.Trim();

        RegisterStatedHistoricalCode = string.IsNullOrEmpty(code) ? null : code;

        return this;
    }

    /// <summary>Reconciles a re-read of the same unit from a newer edition of the register.</summary>
    public void Reconcile(
        string name,
        LguLevel level,
        string? parentCanonicalPsgcCode,
        string? registerStatedHistoricalCode,
        Guid registerEditionId,
        DateTimeOffset now)
    {
        Name = name.Trim();
        Level = level;
        RegisterEditionId = registerEditionId;

        WithHierarchy(parentCanonicalPsgcCode);
        WithRegisterStatedHistoricalCode(registerStatedHistoricalCode);
        Touch(now);
    }
}
