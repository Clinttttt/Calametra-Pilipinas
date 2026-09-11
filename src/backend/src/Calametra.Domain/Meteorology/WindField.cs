namespace Calametra.Domain.Meteorology;

/// <summary>
/// How an agency describes the shape of a cyclone's wind field.
/// </summary>
/// <remarks>
/// Agencies do not merely disagree about the numbers; they do not describe the wind field with
/// the same geometry. In the western North Pacific the Joint Typhoon Warning Center publishes a
/// radius per compass quadrant, so its gale field is an asymmetric four-lobed shape. The Japan
/// Meteorological Agency and the Korea Meteorological Administration publish a long axis, a short
/// axis and a bearing, so theirs is an ellipse. The two cannot be rendered by the same code, and
/// averaging them would produce a shape neither agency published.
/// </remarks>
public enum WindFieldGeometry
{
    /// <summary>No wind field reported.</summary>
    None = 0,

    /// <summary>Four radii, one per compass quadrant. JTWC.</summary>
    Quadrants = 1,

    /// <summary>Long axis, short axis and the bearing of the long axis. JMA and KMA.</summary>
    Ellipse = 2,
}

/// <summary>
/// The extent of a cyclone's damaging winds, as one agency described it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The threshold is part of the measurement.</b> JTWC reports the radius at which sustained
/// wind falls to 34 knots; JMA and KMA report it at 30 knots. A radius stored without its
/// threshold would repeat exactly the mistake that <see cref="WindReading"/> exists to prevent —
/// a number that looks comparable and is not.
/// </para>
/// <para>
/// Distances are nautical miles, the unit the archive publishes. Converted for display rather
/// than on storage, so the stored value remains the one the agency issued.
/// </para>
/// </remarks>
public readonly record struct WindField
{
    private WindField(
        WindFieldGeometry geometry,
        int thresholdKnots,
        double? northEastNm,
        double? southEastNm,
        double? southWestNm,
        double? northWestNm,
        double? longAxisNm,
        double? shortAxisNm,
        int? bearingDegrees)
    {
        Geometry = geometry;
        ThresholdKnots = thresholdKnots;
        NorthEastNm = northEastNm;
        SouthEastNm = southEastNm;
        SouthWestNm = southWestNm;
        NorthWestNm = northWestNm;
        LongAxisNm = longAxisNm;
        ShortAxisNm = shortAxisNm;
        BearingDegrees = bearingDegrees;
    }

    public static readonly WindField None = new(
        WindFieldGeometry.None, 0, null, null, null, null, null, null, null);

    public WindFieldGeometry Geometry { get; }

    /// <summary>
    /// The wind speed the radii are measured at, in knots. 34 for JTWC, 30 for JMA and KMA.
    /// </summary>
    public int ThresholdKnots { get; }

    public double? NorthEastNm { get; }

    public double? SouthEastNm { get; }

    public double? SouthWestNm { get; }

    public double? NorthWestNm { get; }

    public double? LongAxisNm { get; }

    public double? ShortAxisNm { get; }

    /// <summary>Bearing of the long axis in degrees clockwise from north.</summary>
    public int? BearingDegrees { get; }

    /// <summary>Nautical miles to kilometres. The exact definition.</summary>
    private const double KilometresPerNauticalMile = 1.852d;

    /// <summary>
    /// A four-quadrant field, as JTWC publishes.
    /// </summary>
    /// <remarks>
    /// Returns <see cref="None"/> when every quadrant is absent, so a caller cannot end up with a
    /// field that claims a geometry it has no radii for.
    /// </remarks>
    public static WindField FromQuadrants(
        int thresholdKnots,
        double? northEastNm,
        double? southEastNm,
        double? southWestNm,
        double? northWestNm)
    {
        if (northEastNm is null && southEastNm is null && southWestNm is null && northWestNm is null)
        {
            return None;
        }

        return new WindField(
            WindFieldGeometry.Quadrants,
            thresholdKnots,
            northEastNm,
            southEastNm,
            southWestNm,
            northWestNm,
            null,
            null,
            null);
    }

    /// <summary>An elliptical field, as JMA and KMA publish.</summary>
    public static WindField FromEllipse(
        int thresholdKnots,
        double? longAxisNm,
        double? shortAxisNm,
        int? bearingDegrees)
    {
        if (longAxisNm is null && shortAxisNm is null)
        {
            return None;
        }

        return new WindField(
            WindFieldGeometry.Ellipse,
            thresholdKnots,
            null,
            null,
            null,
            null,
            longAxisNm,
            shortAxisNm,
            bearingDegrees);
    }

    public bool HasExtent => Geometry != WindFieldGeometry.None;

    /// <summary>
    /// The largest radius reported, in kilometres — the field's reach at its widest.
    /// </summary>
    /// <remarks>
    /// Useful for framing a camera or sizing a legend. Deliberately not offered as "the storm's
    /// size": a single figure would discard the asymmetry, which for a quadrant field can be a
    /// factor of two between one side and the other.
    /// </remarks>
    public double? WidestReachKm
    {
        get
        {
            var candidates = Geometry == WindFieldGeometry.Quadrants
                ? new[] { NorthEastNm, SouthEastNm, SouthWestNm, NorthWestNm }
                : [LongAxisNm, ShortAxisNm];

            var widest = candidates.Where(value => value is not null).Select(value => value!.Value);

            return widest.Any() ? widest.Max() * KilometresPerNauticalMile : null;
        }
    }

    /// <summary>
    /// How asymmetric the field is, as the ratio of widest to narrowest quadrant.
    /// </summary>
    /// <remarks>
    /// Null for an ellipse, where the concept is already expressed by the two axes, and null when
    /// any quadrant is missing — a ratio computed from three of four sides would understate it.
    /// The measured range in the archive reaches 2.3, which is why a circle is not an acceptable
    /// approximation of a gale field.
    /// </remarks>
    public double? AsymmetryRatio
    {
        get
        {
            if (Geometry != WindFieldGeometry.Quadrants)
            {
                return null;
            }

            double[] quadrants = [
                NorthEastNm ?? -1,
                SouthEastNm ?? -1,
                SouthWestNm ?? -1,
                NorthWestNm ?? -1,
            ];

            if (Array.Exists(quadrants, value => value < 0))
            {
                return null;
            }

            var narrowest = quadrants.Min();

            return narrowest <= 0 ? null : quadrants.Max() / narrowest;
        }
    }

    /// <summary>The threshold with its unit, so a radius is never shown bare.</summary>
    public string Display() => Geometry switch
    {
        WindFieldGeometry.Quadrants =>
            $"{ThresholdKnots} kt radius, four quadrants "
            + $"({Format(NorthEastNm)}/{Format(SouthEastNm)}/{Format(SouthWestNm)}/{Format(NorthWestNm)} nmi)",
        WindFieldGeometry.Ellipse =>
            $"{ThresholdKnots} kt radius, ellipse ({Format(LongAxisNm)} x {Format(ShortAxisNm)} nmi)",
        _ => "No wind field reported",
    };

    private static string Format(double? value) => value is null ? "-" : $"{value:0}";
}
