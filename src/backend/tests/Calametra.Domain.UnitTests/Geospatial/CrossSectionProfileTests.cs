using Calametra.Domain.Geospatial;

namespace Calametra.Domain.UnitTests.Geospatial;

/// <summary>
/// Guards the cross-section projection.
///
/// This is the platform's signature calculation, and it is the reason the projection is
/// computed in the domain rather than pushed into SQL: an error here would put
/// hypocentres at the wrong position on the section and look entirely plausible.
/// </summary>
public sealed class CrossSectionProfileTests
{
    /// <summary>
    /// A west-to-east section across Mindanao at roughly 9.9°N, from inland Surigao out
    /// past the Philippine Trench. This is the real slice that shows the subducting slab.
    /// </summary>
    private static CrossSectionProfile SurigaoSection() =>
        CrossSectionProfile.Create(
            startLatitude: 9.9d,
            startLongitude: 125.0d,
            endLatitude: 9.9d,
            endLongitude: 127.0d,
            corridorKm: 50d);

    [Fact]
    public void LengthShouldBeMeasuredOnTheEllipsoidNotInDegrees()
    {
        var section = SurigaoSection();

        // Two degrees of longitude at 9.9°N is about 219 km, not the 222 km it would be
        // at the equator. Treating degrees as a uniform distance would stretch the axis.
        section.LengthKm.ShouldBe(219d, tolerance: 3d);
    }

    [Fact]
    public void TheStartPointShouldProjectToZero()
    {
        var section = SurigaoSection();

        var projection = section.Project(Wgs84.Point(9.9d, 125.0d));

        projection.AlongKm.ShouldBe(0d, tolerance: 0.5d);
        projection.OffsetKm.ShouldBe(0d, tolerance: 0.5d);
    }

    [Fact]
    public void TheEndPointShouldProjectToTheFullLength()
    {
        var section = SurigaoSection();

        var projection = section.Project(Wgs84.Point(9.9d, 127.0d));

        projection.AlongKm.ShouldBe(section.LengthKm, tolerance: 0.5d);
    }

    [Fact]
    public void TheMidpointShouldProjectToHalfTheLength()
    {
        var section = SurigaoSection();

        var projection = section.Project(Wgs84.Point(9.9d, 126.0d));

        projection.AlongKm.ShouldBe(section.LengthKm / 2d, tolerance: 1d);
        projection.OffsetKm.ShouldBe(0d, tolerance: 0.5d);
    }

    [Fact]
    public void AnEventOffTheLineShouldReportItsPerpendicularOffset()
    {
        var section = SurigaoSection();

        // A quarter degree north of the section is roughly 27 km.
        var projection = section.Project(Wgs84.Point(10.15d, 126.0d));

        projection.OffsetKm.ShouldBe(27d, tolerance: 3d);

        // Its along-line position is unaffected: it still sits at the midpoint
        // horizontally, which is exactly why the offset has to be reported separately.
        projection.AlongKm.ShouldBe(section.LengthKm / 2d, tolerance: 2d);
    }

    [Fact]
    public void AnEventBeyondTheEndShouldClampToTheEnd()
    {
        var section = SurigaoSection();

        // Past the eastern terminus. Projection clamps to the line's extent rather than
        // extrapolating, so nothing is ever plotted off the axis.
        var projection = section.Project(Wgs84.Point(9.9d, 128.0d));

        projection.AlongKm.ShouldBe(section.LengthKm, tolerance: 0.5d);
        projection.OffsetKm.ShouldBeGreaterThan(100d);
    }

    [Fact]
    public void The2017SurigaoEpicentreShouldSitOnASectionThroughIt()
    {
        var section = SurigaoSection();

        var projection = section.Project(Wgs84.Point(9.9071d, 125.4516d));

        // Within a kilometre of the line, since the section was drawn along its latitude.
        projection.OffsetKm.ShouldBeLessThan(2d);

        // And roughly a quarter of the way along, matching 125.45 between 125 and 127.
        projection.AlongKm.ShouldBe(section.LengthKm * 0.226d, tolerance: 5d);
    }

    [Fact]
    public void ADiagonalSectionShouldStillProjectItsEndpointsCorrectly()
    {
        // Sections will not always be axis-aligned; a north-west to south-east slice
        // across Luzon exercises both coordinates changing at once.
        var section = CrossSectionProfile.Create(
            startLatitude: 18.0d,
            startLongitude: 120.0d,
            endLatitude: 14.0d,
            endLongitude: 122.0d,
            corridorKm: 40d);

        section.Project(Wgs84.Point(18.0d, 120.0d)).AlongKm.ShouldBe(0d, tolerance: 1d);
        section.Project(Wgs84.Point(14.0d, 122.0d)).AlongKm.ShouldBe(section.LengthKm, tolerance: 1d);
        section.LengthKm.ShouldBeGreaterThan(400d);
    }
}
