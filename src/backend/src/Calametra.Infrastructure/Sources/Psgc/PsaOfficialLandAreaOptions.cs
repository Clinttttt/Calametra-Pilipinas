namespace Calametra.Infrastructure.Sources.Psgc;

/// <summary>PSA OpenSTAT matrix used for current official/statistical LGU land area.</summary>
public sealed class PsaOfficialLandAreaOptions
{
    public const string Slug = "psa-openstat-lgu-land-area";
    public const string SectionName = "Sources:OfficialLandArea";
    public const string DefaultMatrixId = "1A6DLPD0";

    public string MatrixId { get; init; } = DefaultMatrixId;

    public string ApiUrl { get; init; } =
        "https://openstat.psa.gov.ph:443/PXWeb/api/v1/en/DB/1A/PO_2024/0221A6DLPD0.px";

    public int ReferenceYear { get; init; } = 2019;
}
