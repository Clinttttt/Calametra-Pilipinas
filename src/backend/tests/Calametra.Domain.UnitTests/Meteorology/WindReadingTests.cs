using Calametra.Domain.Meteorology;

namespace Calametra.Domain.UnitTests.Meteorology;

/// <summary>
/// Guards the wind reading value object.
///
/// The cases below use real IBTrACS values for Tropical Storm Yamaneko at 18:00 UTC on
/// 13 November 2022, when four agencies reported four different speeds over three different
/// averaging periods. Fixtures drawn from the archive rather than invented, so a change that
/// broke the comparability rule would fail against data the platform actually holds.
/// </summary>
public sealed class WindReadingTests
{
    private static readonly WindReading Jtwc = new(30d, WindAveragingPeriod.OneMinute);
    private static readonly WindReading Jma = new(35d, WindAveragingPeriod.TenMinute);
    private static readonly WindReading Cma = new(34d, WindAveragingPeriod.TwoMinute);
    private static readonly WindReading HongKong = new(25d, WindAveragingPeriod.TenMinute);

    [Fact]
    public void ReadingsOverTheSamePeriodShouldBeComparable()
    {
        // JMA and Hong Kong both publish a ten-minute mean, so the 10 kt gap between them is
        // a real disagreement about the storm.
        Jma.DifferenceFrom(HongKong).ShouldBe(10d);
    }

    [Fact]
    public void ReadingsOverDifferentPeriodsShouldNotBeComparable()
    {
        // A one-minute mean against a ten-minute mean is not a difference in wind speed.
        Jtwc.DifferenceFrom(Jma).ShouldBeNull();
        Jtwc.DifferenceFrom(Cma).ShouldBeNull();
        Cma.DifferenceFrom(Jma).ShouldBeNull();
    }

    [Fact]
    public void DifferenceShouldBeSymmetric()
    {
        Jma.DifferenceFrom(HongKong).ShouldBe(HongKong.DifferenceFrom(Jma));
        Jtwc.DifferenceFrom(Jma).ShouldBe(Jma.DifferenceFrom(Jtwc));
    }

    [Fact]
    public void AnUnstatedPeriodShouldBeComparableWithNothing()
    {
        var unstated = new WindReading(35d, WindAveragingPeriod.Unknown);

        // Not even with another unstated reading: two unknown intervals are not known to be
        // the same interval.
        unstated.DifferenceFrom(Jma).ShouldBeNull();
        unstated.DifferenceFrom(new WindReading(35d, WindAveragingPeriod.Unknown)).ShouldBeNull();
    }

    [Fact]
    public void TheOneMinuteValueIsNotAlwaysTheLarger()
    {
        // Worth pinning as a fact about the data. A conversion-factor approach would assume
        // the shorter averaging period always reports higher; here JTWC's one-minute 30 kt is
        // below JMA's ten-minute 35 kt. That is why comparison is refused rather than
        // converted.
        Jtwc.SpeedKnots.ShouldBeLessThan(Jma.SpeedKnots);
        Jtwc.Period.IsComparableWith(Jma.Period).ShouldBeFalse();
    }

    [Fact]
    public void MetricConversionShouldUseTheExactDefinition()
    {
        // 1 kt is exactly 1.852 km/h.
        new WindReading(100d, WindAveragingPeriod.TenMinute)
            .SpeedKilometresPerHour.ShouldBe(185.2d, tolerance: 0.001d);
    }

    [Fact]
    public void DisplayShouldAlwaysCarryTheAveragingPeriod()
    {
        Jtwc.Display().ShouldBe("30 kt (1-min)");
        Jma.Display().ShouldBe("35 kt (10-min)");
        Jma.DisplayMetric().ShouldBe("65 km/h (10-min)");
    }

    [Fact]
    public void AnUnstatedPeriodShouldSaySoRatherThanReadAsBare()
    {
        // The failure mode this guards against is a reading that looks authoritative because
        // its missing period is silently omitted.
        new WindReading(35d, WindAveragingPeriod.Unknown).Display().ShouldBe("35 kt (unstated)");
    }
}
