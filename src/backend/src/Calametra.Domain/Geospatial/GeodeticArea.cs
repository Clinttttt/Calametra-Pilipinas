using NetTopologySuite.Geometries;

namespace Calametra.Domain.Geospatial;

/// <summary>
/// Area of a lon/lat polygon on the sphere, in square kilometres.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not the planar area of the coordinates.</b> A polygon in degrees has an "area" in square degrees,
/// and converting that with a single scale factor is wrong by the cosine of the latitude — which for the
/// Philippines, spanning 4°N to 21°N, is a 4% spread between Tawi-Tawi and Batanes. ADR-005 D1 requires
/// every figure derived from a boundary to state the unit's area beside it, precisely because Philippine
/// LGUs differ in area by more than two orders of magnitude, so an area that is systematically wrong by
/// latitude would undermine the comparison it exists to make honest.
/// </para>
/// <para>
/// This is the spherical excess formula, which is exact on a sphere and within roughly 0.5% of the
/// ellipsoidal answer at these latitudes. That is well inside the precision of a volunteer-mapped
/// boundary, so the extra complexity of a geodesic implementation would be false rigour.
/// </para>
/// <para>
/// Holes are subtracted, so a municipality with an enclave reports the area it governs rather than the
/// area it encloses. The same reason the enclave was mapped as a hole in the first place.
/// </para>
/// </remarks>
public static class GeodeticArea
{
    /// <summary>Mean Earth radius, matching <see cref="GeoDistance"/> so the two agree.</summary>
    private const double EarthRadiusKm = 6371.0088;

    public static double SquareKilometres(Geometry? geometry)
    {
        if (geometry is null || geometry.IsEmpty)
        {
            return 0d;
        }

        var total = 0d;

        for (var index = 0; index < geometry.NumGeometries; index++)
        {
            if (geometry.GetGeometryN(index) is Polygon polygon)
            {
                total += PolygonSquareKilometres(polygon);
            }
        }

        return total;
    }

    private static double PolygonSquareKilometres(Polygon polygon)
    {
        var area = RingSquareKilometres(polygon.ExteriorRing.Coordinates);

        for (var index = 0; index < polygon.NumInteriorRings; index++)
        {
            area -= RingSquareKilometres(polygon.GetInteriorRingN(index).Coordinates);
        }

        return Math.Max(0d, area);
    }

    /// <summary>
    /// Spherical excess of one closed ring, unsigned.
    /// </summary>
    /// <remarks>
    /// Unsigned on purpose: the sign encodes winding order, and OSM winding is not reliable enough to
    /// derive anything from. Shells and holes are distinguished by the relation's roles instead, and this
    /// method is told which it is being given by the caller subtracting.
    /// </remarks>
    private static double RingSquareKilometres(Coordinate[] ring)
    {
        if (ring.Length < 4)
        {
            return 0d;
        }

        var sum = 0d;

        for (var index = 0; index < ring.Length - 1; index++)
        {
            var current = ring[index];
            var next = ring[index + 1];

            var lon1 = ToRadians(current.X);
            var lat1 = ToRadians(current.Y);
            var lon2 = ToRadians(next.X);
            var lat2 = ToRadians(next.Y);

            sum += (lon2 - lon1) * (2d + Math.Sin(lat1) + Math.Sin(lat2));
        }

        return Math.Abs(sum * EarthRadiusKm * EarthRadiusKm / 2d);
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
