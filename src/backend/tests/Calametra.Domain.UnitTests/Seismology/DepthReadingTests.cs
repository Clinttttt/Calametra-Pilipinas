using Calametra.Domain.Seismology;

namespace Calametra.Domain.UnitTests.Seismology;

/// <summary>
/// Guards the handling of agency-assigned default depths, which account for 43% of
/// the Philippine archive and would otherwise corrupt the seismic cross-section and
/// every depth-based similarity score.
/// </summary>
public sealed class DepthReadingTests
{
    [Fact]
    public void Constrained_ShouldBeUsableForQuantitativeAnalysis() =>
        DepthReading.Constrained(46.378d).IsQuantitative.ShouldBeTrue();

    [Fact]
    public void OperatorAssigned_ShouldNotBeUsableForQuantitativeAnalysis()
    {
        // Modern NEIC defaults.
        DepthReading.OperatorAssigned(10d).IsQuantitative.ShouldBeFalse();
        DepthReading.OperatorAssigned(35d).IsQuantitative.ShouldBeFalse();
    }

    [Theory]
    // The four fixed-depth conventions present in the Philippine record, with the
    // counts that identify them. Measured over 1901-2026, 27,241 events: 11,790
    // depths (43.3%) sit on one of these four values.
    [InlineData(10d, "modern NEIC shallow default — 4,089 events")]
    [InlineData(35d, "modern NEIC default — 1,794 events")]
    [InlineData(33d, "historical NEIC 'normal depth' assumption — 5,581 events")]
    [InlineData(15d, "historical re-analysis fixed depth — 326 events")]
    public void EveryKnownAssignedDepth_ShouldBeExcludedFromAnalysis(double kilometres, string convention)
    {
        // 33 km is the one that matters most and is easiest to miss: it is the single
        // most common depth in the archive, and it only appears once the backfill
        // reaches back past 1990. A detector written against the modern record alone
        // plots 5,581 events as measured depths that were never measured.
        DepthReading.OperatorAssigned(kilometres)
            .IsQuantitative
            .ShouldBeFalse($"{kilometres} km is a {convention}");
    }

    [Fact]
    public void Unknown_ShouldNotBeUsableForQuantitativeAnalysis() =>
        DepthReading.Unknown.IsQuantitative.ShouldBeFalse();

    [Fact]
    public void DifferenceFrom_ShouldComputeDelta_WhenBothDepthsAreConstrained() =>
        DepthReading.Constrained(32d)
            .DifferenceFrom(DepthReading.Constrained(19d))!
            .Value
            .ShouldBe(13d, tolerance: 0.0001d);

    [Fact]
    public void DifferenceFrom_ShouldReturnNull_WhenEitherDepthWasAssigned()
    {
        // Comparing a measured 32 km against an assigned 35 km would report a
        // 3 km difference that means nothing.
        DepthReading.Constrained(32d)
            .DifferenceFrom(DepthReading.OperatorAssigned(35d))
            .ShouldBeNull();

        DepthReading.OperatorAssigned(10d)
            .DifferenceFrom(DepthReading.Constrained(15d))
            .ShouldBeNull();
    }

    [Fact]
    public void DifferenceFrom_ShouldReturnNull_WhenEitherDepthIsUnknown() =>
        DepthReading.Constrained(15d).DifferenceFrom(DepthReading.Unknown).ShouldBeNull();

    [Fact]
    public void Display_ShouldMarkAssignedDepths_SoTheUiCannotPresentThemAsMeasured()
    {
        DepthReading.Constrained(15d).Display().ShouldBe("15 km");
        DepthReading.OperatorAssigned(10d).Display().ShouldBe("10 km (assigned)");
        DepthReading.Unknown.Display().ShouldBe("Depth unknown");
    }
}
