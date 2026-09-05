namespace Calametra.Infrastructure.Persistence;

/// <summary>
/// PostGIS column type strings, in one place so no configuration file invents its own.
/// </summary>
/// <remarks>
/// <c>geography</c> rather than <c>geometry</c> throughout. Calametra's queries are
/// almost entirely "within N kilometres" questions, and on <c>geography</c> PostGIS
/// takes and returns metres directly — <c>ST_DWithin(a, b, 50000)</c> is 50 km with
/// no projection step and no planar approximation. Using <c>geometry</c> in 4326
/// would make every distance a value in degrees, which is not a distance.
/// </remarks>
internal static class PostgisTypes
{
    public const string Point = "geography (Point, 4326)";

    public const string LineString = "geography (LineString, 4326)";

    public const string MultiPolygon = "geography (MultiPolygon, 4326)";

    /// <summary>
    /// Mixed geometry column, for tables that hold whatever shape a publisher
    /// used. Fault traces arrive as lines, susceptibility zones as polygons and
    /// volcano locations as points, all in one hazard feature table.
    /// </summary>
    public const string AnyGeometry = "geography (Geometry, 4326)";
}
