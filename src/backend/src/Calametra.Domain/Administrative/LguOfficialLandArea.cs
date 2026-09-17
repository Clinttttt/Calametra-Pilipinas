using Calametra.Domain.Abstractions;

namespace Calametra.Domain.Administrative;

/// <summary>The method stated for one published LGU land-area value.</summary>
public enum OfficialLandAreaBasis
{
    /// <summary>The source edition does not classify this row more precisely.</summary>
    Unspecified = 0,
    CadastralSurvey = 1,
    Estimated = 2,
}

public static class LguOfficialLandAreaErrors
{
    public static readonly Error EditionRequired = new(
        ErrorType.Validation,
        "lgu_official_area.edition_required",
        "An official land-area record requires its source edition.");

    public static readonly Error LguRequired = new(
        ErrorType.Validation,
        "lgu_official_area.lgu_required",
        "An official land-area record requires an LGU identity.");

    public static readonly Error CanonicalCodeRequired = new(
        ErrorType.Validation,
        "lgu_official_area.canonical_code_required",
        "An official land-area record requires an exact ten-digit canonical PSGC code.");

    public static readonly Error AreaNotPositive = new(
        ErrorType.Validation,
        "lgu_official_area.area_not_positive",
        "A published land area must be greater than zero.");
}

/// <summary>
/// One PSA-published statistical land-area value, separate from any mapped boundary measurement.
/// </summary>
public sealed class LguOfficialLandArea : AuditableEntity
{
    private LguOfficialLandArea()
    {
    }

    private LguOfficialLandArea(
        Guid id,
        Guid lguId,
        Guid editionId,
        string canonicalPsgcCode,
        decimal areaSquareKm,
        OfficialLandAreaBasis basis,
        string sourceLabel,
        DateTimeOffset now)
        : base(id, now)
    {
        LguId = lguId;
        EditionId = editionId;
        CanonicalPsgcCode = canonicalPsgcCode;
        AreaSquareKm = areaSquareKm;
        Basis = basis;
        SourceLabel = sourceLabel;
    }

    public Guid LguId { get; private set; }

    public Guid EditionId { get; private set; }

    public string CanonicalPsgcCode { get; private set; } = string.Empty;

    /// <summary>The decimal value and precision published by the source.</summary>
    public decimal AreaSquareKm { get; private set; }

    public OfficialLandAreaBasis Basis { get; private set; }

    /// <summary>The source's own row label, retained for audit but never used as identity.</summary>
    public string SourceLabel { get; private set; } = string.Empty;

    public static Result<LguOfficialLandArea> Record(
        Guid lguId,
        Guid editionId,
        string canonicalPsgcCode,
        decimal areaSquareKm,
        OfficialLandAreaBasis basis,
        string sourceLabel,
        DateTimeOffset now)
    {
        if (lguId == Guid.Empty)
        {
            return Result<LguOfficialLandArea>.Failure(LguOfficialLandAreaErrors.LguRequired);
        }

        if (editionId == Guid.Empty)
        {
            return Result<LguOfficialLandArea>.Failure(LguOfficialLandAreaErrors.EditionRequired);
        }

        var code = canonicalPsgcCode?.Trim() ?? string.Empty;

        if (code.Length != 10 || !code.All(char.IsAsciiDigit))
        {
            return Result<LguOfficialLandArea>.Failure(LguOfficialLandAreaErrors.CanonicalCodeRequired);
        }

        if (areaSquareKm <= 0m)
        {
            return Result<LguOfficialLandArea>.Failure(LguOfficialLandAreaErrors.AreaNotPositive);
        }

        return Result<LguOfficialLandArea>.Success(new LguOfficialLandArea(
            Guid.CreateVersion7(),
            lguId,
            editionId,
            code,
            areaSquareKm,
            basis,
            sourceLabel.Trim(),
            now));
    }
}

/// <summary>
/// One immutable acquisition of a nationwide official/statistical LGU land-area publication.
/// </summary>
public sealed class LguOfficialLandAreaEdition : AuditableEntity
{
    private LguOfficialLandAreaEdition()
    {
    }

    private LguOfficialLandAreaEdition(
        Guid id,
        Guid sourceId,
        Guid registerEditionId,
        string label,
        string matrixId,
        string accessRoute,
        int referenceYear,
        DateTimeOffset retrievedAt,
        DateTimeOffset now)
        : base(id, now)
    {
        SourceId = sourceId;
        RegisterEditionId = registerEditionId;
        Label = label;
        MatrixId = matrixId;
        AccessRoute = accessRoute;
        ReferenceYear = referenceYear;
        RetrievedAt = retrievedAt;
    }

    public Guid SourceId { get; private set; }

    /// <summary>The active PSGC edition against which exact-code coverage was measured.</summary>
    public Guid RegisterEditionId { get; private set; }

    public string Label { get; private set; } = string.Empty;

    public string MatrixId { get; private set; } = string.Empty;

    public string AccessRoute { get; private set; } = string.Empty;

    /// <summary>The underlying government masterlist reference year, not the retrieval year.</summary>
    public int ReferenceYear { get; private set; }

    public DateTimeOffset RetrievedAt { get; private set; }

    public DateTimeOffset? SourceUpdatedAt { get; private set; }

    public string MetadataSha256 { get; private set; } = string.Empty;

    public string PayloadSha256 { get; private set; } = string.Empty;

    public int MetadataSizeBytes { get; private set; }

    public int PayloadSizeBytes { get; private set; }

    /// <summary>Exact source responses, retained so the acquisition can be audited after the API changes.</summary>
    public string MetadataSnapshot { get; private set; } = string.Empty;

    public string PayloadSnapshot { get; private set; } = string.Empty;

    public string Attribution { get; private set; } = string.Empty;

    public int SourceRowCount { get; private set; }

    public int ImportedLguCount { get; private set; }

    public int MissingActiveLguCount { get; private set; }

    public int ExtraSourceRowCount { get; private set; }

    public string? MissingCanonicalPsgcCodes { get; private set; }

    public string? ExtraSourceCodes { get; private set; }

    public DateTimeOffset? ActivatedAt { get; private set; }

    public DateTimeOffset? SupersededAt { get; private set; }

    public Guid? SupersededByEditionId { get; private set; }

    public bool IsCurrent => ActivatedAt is not null && SupersededAt is null;

    public static Result<LguOfficialLandAreaEdition> Create(
        Guid sourceId,
        Guid registerEditionId,
        string label,
        string matrixId,
        string accessRoute,
        int referenceYear,
        DateTimeOffset retrievedAt,
        DateTimeOffset now)
    {
        if (sourceId == Guid.Empty || registerEditionId == Guid.Empty
            || string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(matrixId)
            || string.IsNullOrWhiteSpace(accessRoute) || referenceYear < 1900)
        {
            return Result<LguOfficialLandAreaEdition>.Failure(LguOfficialLandAreaErrors.EditionRequired);
        }

        return Result<LguOfficialLandAreaEdition>.Success(new LguOfficialLandAreaEdition(
            Guid.CreateVersion7(),
            sourceId,
            registerEditionId,
            label.Trim(),
            matrixId.Trim(),
            accessRoute.Trim(),
            referenceYear,
            retrievedAt,
            now));
    }

    public void RecordAcquisition(
        DateTimeOffset? sourceUpdatedAt,
        string metadataSha256,
        string payloadSha256,
        string metadataSnapshot,
        string payloadSnapshot,
        string attribution)
    {
        SourceUpdatedAt = sourceUpdatedAt;
        MetadataSha256 = metadataSha256;
        PayloadSha256 = payloadSha256;
        MetadataSnapshot = metadataSnapshot;
        PayloadSnapshot = payloadSnapshot;
        MetadataSizeBytes = System.Text.Encoding.UTF8.GetByteCount(metadataSnapshot);
        PayloadSizeBytes = System.Text.Encoding.UTF8.GetByteCount(payloadSnapshot);
        Attribution = attribution.Trim();
    }

    public void RecordCoverage(
        int sourceRowCount,
        int importedLguCount,
        IReadOnlyCollection<string> missingCodes,
        IReadOnlyCollection<string> extraCodes)
    {
        SourceRowCount = sourceRowCount;
        ImportedLguCount = importedLguCount;
        MissingActiveLguCount = missingCodes.Count;
        ExtraSourceRowCount = extraCodes.Count;
        MissingCanonicalPsgcCodes = missingCodes.Count == 0 ? null : string.Join(',', missingCodes);
        ExtraSourceCodes = extraCodes.Count == 0 ? null : string.Join(',', extraCodes);
    }

    public bool Activate(DateTimeOffset now)
    {
        if (MissingActiveLguCount != 0 || ImportedLguCount == 0)
        {
            return false;
        }

        ActivatedAt ??= now;
        return true;
    }

    public void Supersede(Guid replacementId, DateTimeOffset now)
    {
        if (!IsCurrent || replacementId == Id)
        {
            return;
        }

        SupersededAt = now;
        SupersededByEditionId = replacementId;
        Touch(now);
    }
}
