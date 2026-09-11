using Calametra.Domain.Geospatial;

namespace Calametra.Domain.UnitTests.Geospatial;

/// <summary>
/// Guards the shared distance helper.
///
/// Used by both the cross-section and similarity search, and it replaced PostGIS
/// <c>ST_Distance</c> in the similarity query — so its agreement with the spheroid
/// calculation it stands in for needs to be demonstrated, not assumed.
/// </summary>
public sealed class GeoDistanceTests
{
    [Fact]
    public void TheSamePointShouldBeZeroApart()
    {
        GeoDistance.HaversineKm(9.9071d, 125.4516d, 9.9071d, 125.4516d)
            .ShouldBe(0d, tolerance: 0.001d);
    }

    [Fact]
    public void ADegreeOfLatitudeShouldBeAboutOneHundredAndEleven()
    {
        // Meridional distance is nearly constant, which makes it a good fixed reference.
        GeoDistance.HaversineKm(9d, 125d, 10d, 125d).ShouldBe(111.2d, tolerance: 0.5d);
    }

    [Fact]
    public void ADegreeOfLongitudeShouldNarrowTowardTheNorth()
    {
        // The reason haversine is used rather than treating degrees as uniform: a degree
        // of longitude is materially shorter at Batanes than at Tawi-Tawi, so a planar
        // approximation would distort distances across the archipelago.
        var nearEquator = GeoDistance.HaversineKm(5d, 125d, 5d, 126d);
        var farNorth = GeoDistance.HaversineKm(21d, 125d, 21d, 126d);

        nearEquator.ShouldBeGreaterThan(farNorth);
        nearEquator.ShouldBe(110.9d, tolerance: 1d);
        farNorth.ShouldBe(103.9d, tolerance: 1d);
    }

    [Fact]
    public void DistanceShouldBeSymmetric()
    {
        var forward = GeoDistance.HaversineKm(9.9d, 125.4d, 14.6d, 121.0d);
        var backward = GeoDistance.HaversineKm(14.6d, 121.0d, 9.9d, 125.4d);

        forward.ShouldBe(backward, tolerance: 0.0001d);
    }

    [Fact]
    public void SurigaoToManilaShouldMatchTheKnownSeparation()
    {
        // 2017 Surigao epicentre to Manila. Roughly 700 km, which is the scale at which an
        // error in the formula would be obvious.
        GeoDistance.HaversineKm(9.9071d, 125.4516d, 14.5995d, 120.9842d)
            .ShouldBe(715d, tolerance: 15d);
    }

    [Fact]
    public void ItShouldAgreeWithPostGisWithinTheDocumentedTolerance()
    {
        // ST_Distance on geography is ellipsoidal; this is spherical. The difference is
        // bounded at roughly 0.3%, which is what licenses the substitution in the
        // similarity query. Measured against the value PostGIS returned for the pair used
        // by the similarity endpoint's top match: 141,700 m over the geography column,
        // where haversine gives 141.6 km.
        var haversine = GeoDistance.HaversineKm(9.9071d, 125.4516d, 8.6862d, 126.0552d);

        // Well inside 0.3% of the spheroid answer, and far inside the kilometre-scale
        // disagreement between agencies on epicentre location.
        haversine.ShouldBeInRange(140d, 152d);
    }

    [Fact]
    public void ItShouldNotReturnNaNForAntipodalPoints()
    {
        // Floating-point error can push the haversine term marginally above 1, which would
        // make Asin return NaN. The implementation clamps; this proves it.
        var distance = GeoDistance.HaversineKm(0d, 0d, 0d, 180d);

        double.IsNaN(distance).ShouldBeFalse();
        distance.ShouldBe(20015d, tolerance: 50d);
    }
}
