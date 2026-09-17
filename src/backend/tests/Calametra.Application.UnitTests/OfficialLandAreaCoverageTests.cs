using Calametra.Application.Abstractions.Sources;
using Calametra.Application.Features.Ingestion;

namespace Calametra.Application.UnitTests;

public sealed class OfficialLandAreaCoverageTests
{
    [Fact]
    public void Matches_only_exact_ten_digit_psgc_codes_and_never_names()
    {
        var active = new[] { "1606810000" };
        var rows = new[]
        {
            new OfficialLandAreaSourceRow("9999999999", "Lanuza", 292.27m),
        };

        var result = OfficialLandAreaCoverage.Measure(active, rows);

        result.MissingActiveCodes.ShouldBe(new[] { "1606810000" });
        result.ExtraSourceCodes.ShouldBe(new[] { "9999999999" });
    }

    [Fact]
    public void Accounts_for_every_active_lgu_and_reports_aggregate_rows_as_extras()
    {
        var active = Enumerable.Range(1, 1642)
            .Select(index => index.ToString("D10", System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
        var rows = active
            .Select(code => new OfficialLandAreaSourceRow(code, $"LGU {code}", 1m))
            .Append(new OfficialLandAreaSourceRow("0000000000", "PHILIPPINES", 300000m))
            .Append(new OfficialLandAreaSourceRow("1300000000", "NCR", 619.54m))
            .ToArray();

        var result = OfficialLandAreaCoverage.Measure(active, rows);

        result.MissingActiveCodes.ShouldBeEmpty();
        result.ExtraSourceCodes.ShouldBe(new[] { "0000000000", "1300000000" });
    }
}
