namespace Calametra.Domain.Seismology;

/// <summary>
/// A magnitude value together with the scale it was measured on.
/// </summary>
/// <remarks>
/// Magnitude is deliberately never a bare <c>double</c> anywhere in Calametra.
/// A number without its scale cannot be compared, ranked, or displayed honestly,
/// and the UI is required to render the scale alongside the value.
/// </remarks>
public readonly record struct MagnitudeReading(double Value, MagnitudeType Type)
{
    /// <summary>Formats as the UI must display it, e.g. <c>Mww 6.5</c>.</summary>
    public string Display() => $"{Type.Label()} {Value:0.0}";

    /// <summary>
    /// Absolute difference against another reading, or <see langword="null"/> when
    /// the two scales are not comparable. A null result is a meaningful answer and
    /// must be surfaced to the user, not silently treated as zero.
    /// </summary>
    public double? DifferenceFrom(MagnitudeReading other) =>
        Type.IsComparableWith(other.Type) ? Math.Abs(Value - other.Value) : null;

    public bool IsComparableWith(MagnitudeReading other) => Type.IsComparableWith(other.Type);
}
