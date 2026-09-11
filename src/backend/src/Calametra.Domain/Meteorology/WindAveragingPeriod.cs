namespace Calametra.Domain.Meteorology;

/// <summary>
/// The averaging period a sustained-wind measurement was taken over.
/// </summary>
/// <remarks>
/// <para>
/// This is to wind what <c>MagnitudeType</c> is to magnitude: the unit that makes the number
/// mean something. A sustained wind is the mean speed over a defined interval, and agencies do
/// not use the same interval. The Joint Typhoon Warning Center reports a 1-minute mean; the
/// Japan Meteorological Agency, Hong Kong and Korea report a 10-minute mean; China reports a
/// 2-minute mean.
/// </para>
/// <para>
/// <b>A shorter averaging period yields a higher number for the same storm</b>, because brief
/// peaks are averaged away over ten minutes but survive over one. Comparing a 1-minute wind
/// with a 10-minute wind is therefore comparing different quantities, and the difference
/// between them is not a difference in the storm.
/// </para>
/// <para>
/// Measured in the archive rather than assumed. Tropical Storm Yamaneko at 18:00 UTC on
/// 13 November 2022 was simultaneously recorded at 30 kt by JTWC (1-minute), 35 kt by JMA
/// (10-minute), 34 kt by CMA (2-minute) and 25 kt by Hong Kong (10-minute). Note that the
/// 1-minute value is the *lower* one here, which is the wrong way round for a simple
/// conversion — these are independent analyses, not restatements of each other, so no
/// conversion factor can reconcile them.
/// </para>
/// </remarks>
public enum WindAveragingPeriod
{
    /// <summary>The source did not state an averaging period. Comparable with nothing.</summary>
    Unknown = 0,

    /// <summary>One-minute mean. JTWC and other United States agencies.</summary>
    OneMinute = 1,

    /// <summary>Two-minute mean. China Meteorological Administration.</summary>
    TwoMinute = 2,

    /// <summary>Three-minute mean. Used by India's meteorological department.</summary>
    ThreeMinute = 3,

    /// <summary>Ten-minute mean. The WMO standard, used by JMA, Hong Kong and Korea.</summary>
    TenMinute = 10,
}

public static class WindAveragingPeriodExtensions
{
    /// <summary>
    /// Whether two readings may be differenced.
    /// </summary>
    /// <remarks>
    /// Only within the same averaging period, and never when either is
    /// <see cref="WindAveragingPeriod.Unknown"/>. The same rule as
    /// <c>MagnitudeType.IsComparableWith</c>, and for the same reason: a difference between
    /// two different quantities is not a measurement of anything.
    /// </remarks>
    public static bool IsComparableWith(this WindAveragingPeriod left, WindAveragingPeriod right) =>
        left != WindAveragingPeriod.Unknown
        && right != WindAveragingPeriod.Unknown
        && left == right;

    /// <summary>Short label for display. Never omitted beside a wind speed.</summary>
    public static string Label(this WindAveragingPeriod period) => period switch
    {
        WindAveragingPeriod.OneMinute => "1-min",
        WindAveragingPeriod.TwoMinute => "2-min",
        WindAveragingPeriod.ThreeMinute => "3-min",
        WindAveragingPeriod.TenMinute => "10-min",
        _ => "unstated",
    };

    /// <summary>Phrase for prose, where an abbreviation would read badly.</summary>
    public static string Describe(this WindAveragingPeriod period) => period switch
    {
        WindAveragingPeriod.OneMinute => "one-minute sustained",
        WindAveragingPeriod.TwoMinute => "two-minute sustained",
        WindAveragingPeriod.ThreeMinute => "three-minute sustained",
        WindAveragingPeriod.TenMinute => "ten-minute sustained",
        _ => "sustained over an unstated interval",
    };
}
