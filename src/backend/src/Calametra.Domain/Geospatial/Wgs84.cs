using NetTopologySuite.Geometries;

namespace Calametra.Domain.Geospatial;

/// <summary>
/// Factories and constants for the single spatial reference system Calametra uses.
/// </summary>
/// <remarks>
/// Every geometry in the system is WGS 84 (SRID 4326) and is persisted as a PostGIS
/// <c>geography</c> column. Rationale for <c>geography</c> over <c>geometry</c>:
/// nearly every Calametra feature is a distance-in-kilometres question ("earthquakes
/// within 50 km of Cantilan", "which cyclones passed within 100 km"). On
/// <c>geography</c>, PostGIS returns metres from <c>ST_Distance</c> directly and
/// <c>ST_DWithin</c> takes metres, so no projection step or planar approximation is
/// needed anywhere in the query layer.
/// </remarks>
public static class Wgs84
{
    /// <summary>WGS 84. The only SRID permitted in this system.</summary>
    public const int Srid = 4326;

    private static readonly GeometryFactory Factory =
        NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(Srid);

    /// <summary>Creates a point from latitude and longitude in decimal degrees.</summary>
    /// <remarks>
    /// Argument order is (latitude, longitude) to match how every upstream source
    /// reports positions. Internally this becomes <c>Coordinate(x: longitude,
    /// y: latitude)</c>, which is the opposite order — the single most common
    /// geospatial bug, so it is done exactly once, here.
    /// </remarks>
    public static Point Point(double latitude, double longitude)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(latitude, -90d);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(latitude, 90d);
        ArgumentOutOfRangeException.ThrowIfLessThan(longitude, -180d);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(longitude, 180d);

        return Factory.CreatePoint(new Coordinate(longitude, latitude));
    }

    public static LineString LineString(IEnumerable<Point> orderedPoints)
    {
        var coordinates = orderedPoints.Select(point => point.Coordinate).ToArray();

        return coordinates.Length < 2
            ? Factory.CreateLineString()
            : Factory.CreateLineString(coordinates);
    }
}
