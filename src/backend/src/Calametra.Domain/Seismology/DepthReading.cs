namespace Calametra.Domain.Seismology;

/// <summary>
/// How much confidence a reported hypocentre depth deserves.
/// </summary>
/// <remarks>
/// This enum exists because of a measured property of the USGS catalogue. For the
/// CARAGA bounding box (2015-01-01 to 2026-09-01, M4.0+, 1,991 events):
/// <list type="bullet">
///   <item><description>376 events (18.9%) report depth as exactly 10.00 km</description></item>
///   <item><description>175 events (8.8%) report depth as exactly 35.00 km</description></item>
///   <item><description>the next most common exact value has 4 events</description></item>
/// </list>
/// Together that is 27.7% of the catalogue sitting on two values. 10 km and 35 km
/// are long-standing NEIC default depths, assigned when the depth cannot be
/// resolved from the available phase data. The cliff from 175 events to 4 events
/// confirms these are placeholders rather than genuine clustering.
/// <para>
/// Critically, the <c>depthError</c> field is populated for all 376 of those
/// events, so a depth placeholder <b>cannot</b> be detected by inspecting the
/// error field. Detection relies on the exact-value convention instead.
/// </para>
/// <para>
/// Consequence: the seismic cross-section — Calametra's signature depth
/// visualisation — would render more than a quarter of all events as two
/// perfectly flat horizontal lines unless these are styled and disclosed
/// distinctly. Recording the quality alongside the value makes that failure mode
/// impossible to overlook.
/// </para>
/// </remarks>
public enum DepthQuality
{
    /// <summary>The source reported no usable depth.</summary>
    Unknown = 0,

    /// <summary>Depth was resolved from observations and may be plotted normally.</summary>
    Constrained = 1,

    /// <summary>
    /// Depth was fixed to a conventional default by the reporting agency because
    /// it could not be resolved. Must be visually distinguished and excludable.
    /// </summary>
    OperatorAssigned = 2,
}

/// <summary>A hypocentre depth together with how much it can be trusted.</summary>
public readonly record struct DepthReading(double? Kilometres, DepthQuality Quality)
{
    public static readonly DepthReading Unknown = new(null, DepthQuality.Unknown);

    /// <summary>Depth resolved from observations.</summary>
    public static DepthReading Constrained(double kilometres) =>
        new(kilometres, DepthQuality.Constrained);

    /// <summary>Depth fixed to an agency default because it could not be resolved.</summary>
    public static DepthReading OperatorAssigned(double kilometres) =>
        new(kilometres, DepthQuality.OperatorAssigned);

    /// <summary>
    /// True when this depth may be used in quantitative analysis such as
    /// cross-section plotting or depth-difference similarity scoring.
    /// </summary>
    public bool IsQuantitative => Quality == DepthQuality.Constrained && Kilometres is not null;

    public string Display() => Kilometres is null
        ? "Depth unknown"
        : Quality == DepthQuality.OperatorAssigned
            ? $"{Kilometres:0.#} km (assigned)"
            : $"{Kilometres:0.#} km";

    /// <summary>
    /// Absolute depth difference, or <see langword="null"/> when either depth is
    /// not quantitative. As with magnitude, null is an answer to surface.
    /// </summary>
    public double? DifferenceFrom(DepthReading other) =>
        IsQuantitative && other.IsQuantitative
            ? Math.Abs(Kilometres!.Value - other.Kilometres!.Value)
            : null;
}
