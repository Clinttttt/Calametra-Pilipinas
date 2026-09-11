using NetTopologySuite.Geometries;
using NetTopologySuite.LinearReferencing;

namespace Calametra.Domain.Geospatial;

/// <summary>
/// A vertical slice through the crust, defined by a line drawn on the map.
/// </summary>
/// <remarks>
/// <para>
/// A cross-section answers a question a map cannot: how deep are these earthquakes, and
/// how does depth change across a region. Plotting hypocentres against distance along a
/// line reveals the subducting slab — the dipping plane of seismicity beneath the
/// archipelago — which is invisible in plan view no matter how the colours are chosen.
/// </para>
/// <para>
/// Two measurements matter for each event. <b>Along-line distance</b> is its position on
/// the horizontal axis. <b>Perpendicular offset</b> is how far it sits from the line
/// itself, and it must be surfaced rather than discarded: an event 40 km off the section
/// is being shown at a position it does not really occupy, and a reader deserves to know
/// which points are on the slice and which were swept in by the corridor.
/// </para>
/// <para>
/// The projection is computed here rather than in SQL. PostGIS does the corridor filter,
/// which is indexed and reduces tens of thousands of events to a few hundred; the
/// per-event maths then runs on that small set where it can be unit tested. Pushing
/// <c>ST_LineLocatePoint</c> into the query would need raw SQL and would put the
/// platform's signature calculation somewhere it cannot be verified.
/// </para>
/// </remarks>
public sealed class CrossSectionProfile
{
    private readonly LengthIndexedLine _indexedLine;
    private readonly LineString _line;

    private CrossSectionProfile(LineString line, double corridorKm)
    {
        _line = line;
        _indexedLine = new LengthIndexedLine(line);
        CorridorKm = corridorKm;
    }

    /// <summary>Half-width of the corridor either side of the line, in kilometres.</summary>
    public double CorridorKm { get; }

    /// <summary>The section line, for spatial filtering.</summary>
    public LineString Line => _line;

    /// <summary>
    /// Length of the section in kilometres, measured on the ellipsoid.
    /// </summary>
    /// <remarks>
    /// Computed with the haversine formula rather than from the line's planar length.
    /// The line's coordinates are degrees, and a degree of longitude is 111 km at the
    /// equator but 104 km at Batanes — treating degrees as a uniform distance would
    /// stretch the horizontal axis by several percent across the archipelago.
    /// </remarks>
    public double LengthKm { get; private init; }

    public static CrossSectionProfile Create(
        double startLatitude,
        double startLongitude,
        double endLatitude,
        double endLongitude,
        double corridorKm)
    {
        var line = Wgs84.LineString([
            Wgs84.Point(startLatitude, startLongitude),
            Wgs84.Point(endLatitude, endLongitude),
        ]);

        return new CrossSectionProfile(line, corridorKm)
        {
            LengthKm = HaversineKm(startLatitude, startLongitude, endLatitude, endLongitude),
        };
    }

    /// <summary>
    /// Where an event falls on the section.
    /// </summary>
    /// <param name="AlongKm">
    /// Distance from the section's start point, in kilometres. The horizontal axis.
    /// </param>
    /// <param name="OffsetKm">
    /// Perpendicular distance from the line. Surfaced so a reader can tell an event on
    /// the slice from one swept in at the corridor's edge.
    /// </param>
    public readonly record struct Projection(double AlongKm, double OffsetKm);

    /// <summary>Projects a point onto the section.</summary>
    public Projection Project(Point point)
    {
        ArgumentNullException.ThrowIfNull(point);

        // Fraction along the line, 0 at the start and 1 at the end. Converted to
        // kilometres using the ellipsoidal length so the axis stays true to ground
        // distance.
        var index = _indexedLine.Project(point.Coordinate);
        var endIndex = _indexedLine.EndIndex;
        var fraction = endIndex == 0d ? 0d : index / endIndex;

        var nearest = _indexedLine.ExtractPoint(index);

        return new Projection(
            fraction * LengthKm,
            HaversineKm(point.Y, point.X, nearest.Y, nearest.X));
    }

    /// <summary>Great-circle distance in kilometres.</summary>
    private static double HaversineKm(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2) =>
        GeoDistance.HaversineKm(latitude1, longitude1, latitude2, longitude2);
}
