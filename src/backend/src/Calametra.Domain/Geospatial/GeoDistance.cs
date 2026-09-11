namespace Calametra.Domain.Geospatial;

/// <summary>
/// Great-circle distance on the WGS84 sphere.
/// </summary>
/// <remarks>
/// <para>
/// Exists so distance can be computed away from the database. PostGIS
/// <c>ST_Distance</c> on <c>geography</c> is a spheroid calculation, and asking for it
/// per row turns a cheap indexed <c>ST_DWithin</c> filter into an expensive projection.
/// Once a candidate set has been narrowed spatially, computing separation in memory costs
/// nothing measurable and keeps the emitted SQL to an index scan plus a column fetch.
/// </para>
/// <para>
/// Haversine rather than a planar approximation, because a degree of longitude is 111 km
/// at the equator and 104 km at Batanes — treating degrees as uniform would misstate
/// distances across the archipelago by several percent.
/// </para>
/// <para>
/// Spherical rather than ellipsoidal, which differs from <c>ST_Distance</c> by up to about
/// 0.3%. At the scales this is used for — tens to hundreds of kilometres, presented to one
/// decimal place — that is far below the positional uncertainty of the epicentres
/// themselves, which routinely disagree between agencies by kilometres.
/// </para>
/// </remarks>
public static class GeoDistance
{
    /// <summary>Mean Earth radius in kilometres, matching the WGS84 authalic radius.</summary>
    private const double EarthRadiusKm = 6371.0088;

    public static double HaversineKm(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2)
    {
        var phi1 = ToRadians(latitude1);
        var phi2 = ToRadians(latitude2);
        var deltaPhi = phi2 - phi1;
        var deltaLambda = ToRadians(longitude2 - longitude1);

        var a = (Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2))
            + (Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2));

        // Asin is clamped because floating-point error can push `a` marginally above 1
        // for antipodal points, which would otherwise produce NaN.
        return EarthRadiusKm * 2 * Math.Asin(Math.Min(1d, Math.Sqrt(a)));
    }

    /// <summary>
    /// Initial bearing from the first point to the second, in degrees clockwise from north.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The forward azimuth of the great-circle path, which is what "38 km ENE of Surigao City"
    /// states. Not the same as the angle of the straight line drawn between the two points on a
    /// Web Mercator map: the projection distorts direction away from the equator, so a bearing
    /// read off the screen would disagree with this by degrees at Philippine latitudes.
    /// </para>
    /// <para>
    /// Called "initial" because it changes along a great circle. Over the tens to hundreds of
    /// kilometres this is used for, the change is a fraction of a degree — far below the
    /// resolution of the sixteen-point compass it is reported on.
    /// </para>
    /// </remarks>
    public static double InitialBearingDegrees(
        double fromLatitude,
        double fromLongitude,
        double toLatitude,
        double toLongitude)
    {
        var phi1 = ToRadians(fromLatitude);
        var phi2 = ToRadians(toLatitude);
        var deltaLambda = ToRadians(toLongitude - fromLongitude);

        var y = Math.Sin(deltaLambda) * Math.Cos(phi2);
        var x = (Math.Cos(phi1) * Math.Sin(phi2))
            - (Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(deltaLambda));

        var bearing = Math.Atan2(y, x) * 180d / Math.PI;

        // Atan2 returns -180…180; a compass bearing is 0…360.
        return (bearing + 360d) % 360d;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
