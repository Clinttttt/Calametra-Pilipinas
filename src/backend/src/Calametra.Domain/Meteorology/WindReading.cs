namespace Calametra.Domain.Meteorology;

/// <summary>
/// A sustained wind speed that cannot be separated from its averaging period.
/// </summary>
/// <remarks>
/// The meteorological counterpart of <c>MagnitudeReading</c>, and it exists for the same
/// reason: the archive proves that a bare number is not a measurement. Four agencies
/// described Tropical Storm Yamaneko at the same instant as 30, 35, 34 and 25 knots, using
/// three different averaging periods between them.
/// <para>
/// A struct with both members required means no code path can construct a wind speed without
/// stating what it is a mean of.
/// </para>
/// </remarks>
public readonly record struct WindReading(double SpeedKnots, WindAveragingPeriod Period)
{
    /// <summary>Knots to kilometres per hour. The exact definition, not an approximation.</summary>
    private const double KilometresPerHourPerKnot = 1.852d;

    /// <summary>Speed in kilometres per hour, for a Philippine audience.</summary>
    /// <remarks>
    /// PAGASA issues public warnings in km/h, so knots — the convention of the international
    /// archives this data comes from — is not the unit most readers here expect. Both are
    /// offered; neither is stored twice.
    /// </remarks>
    public double SpeedKilometresPerHour => SpeedKnots * KilometresPerHourPerKnot;

    /// <summary>
    /// Absolute difference in knots, or <see langword="null"/> when the two readings were
    /// averaged over different intervals.
    /// </summary>
    /// <remarks>
    /// Null is the meaningful answer and must be surfaced, never coerced to zero. Two winds
    /// averaged over different periods are different quantities, and no conversion factor
    /// reconciles them — the archive contains cases where the 1-minute value is lower than
    /// the 10-minute one, which a conversion could not produce.
    /// </remarks>
    public double? DifferenceFrom(WindReading other) =>
        Period.IsComparableWith(other.Period)
            ? Math.Abs(SpeedKnots - other.SpeedKnots)
            : null;

    /// <summary>
    /// The reading with its averaging period, in knots.
    /// </summary>
    /// <remarks>
    /// The period is part of the value, not a footnote, so every rendering carries it. This
    /// mirrors <c>MagnitudeReading.Display</c>, where the scale is likewise inseparable.
    /// </remarks>
    public string Display() => $"{SpeedKnots:0} kt ({Period.Label()})";

    /// <summary>The same reading in kilometres per hour, with its averaging period.</summary>
    public string DisplayMetric() => $"{SpeedKilometresPerHour:0} km/h ({Period.Label()})";
}
