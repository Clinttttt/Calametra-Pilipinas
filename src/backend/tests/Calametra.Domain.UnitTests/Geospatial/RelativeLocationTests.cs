using Calametra.Domain.Geospatial;

namespace Calametra.Domain.UnitTests.Geospatial;

/// <summary>
/// Covers the wording a reader sees for every epicentre, and the bearing behind it.
/// </summary>
/// <remarks>
/// The coordinates are real: Surigao City's gazetteer point is 9.75°N 125.5°E and the 2017
/// PHIVOLCS epicentre is 9.93°N 125.45°E, which is the pair the platform's flagship event
/// actually produces.
/// </remarks>
public sealed class RelativeLocationTests
{
    private const double SurigaoCityLatitude = 9.75d;
    private const double SurigaoCityLongitude = 125.5d;

    [Fact]
    public void APosition_ShouldBeStatedAsDistanceDirectionAndPlace()
    {
        var location = RelativeLocation.Between(
            "Surigao City",
            "Province of Surigao del Norte",
            SurigaoCityLatitude,
            SurigaoCityLongitude,
            latitude: 9.93d,
            longitude: 125.45d);

        location.Direction.ShouldBe("NNW");
        location.DistanceKm.ShouldBeInRange(20d, 22d);
        location.Describe().ShouldBe("21 km NNW of Surigao City");
        location.DescribeWithContainer()
            .ShouldBe("21 km NNW of Surigao City, Province of Surigao del Norte");
    }

    [Theory]
    // Cardinals and the ordinals between them, each measured a degree of latitude or longitude
    // away so the bearing is unambiguous.
    [InlineData(10.75d, 125.5d, "N")]
    [InlineData(9.75d, 126.5d, "E")]
    [InlineData(8.75d, 125.5d, "S")]
    [InlineData(9.75d, 124.5d, "W")]
    [InlineData(10.45d, 126.2d, "NE")]
    [InlineData(9.05d, 126.2d, "SE")]
    [InlineData(9.05d, 124.8d, "SW")]
    [InlineData(10.45d, 124.8d, "NW")]
    public void TheDirection_ShouldBeTheCompassPointTheBearingFallsIn(
        double latitude,
        double longitude,
        string expected)
    {
        var location = RelativeLocation.Between(
            "Surigao City",
            null,
            SurigaoCityLatitude,
            SurigaoCityLongitude,
            latitude,
            longitude);

        location.Direction.ShouldBe(expected);
    }

    [Fact]
    public void ABearingJustShortOfNorth_ShouldRoundToNorthRatherThanNorthNorthWest()
    {
        // 349° is nearer to N than to NNW, and truncating instead of rounding would report NNW.
        var location = new RelativeLocation("Surigao City", null, 40d, 349d);

        location.Direction.ShouldBe("N");
    }

    [Fact]
    public void ABearingOfExactlyThreeHundredAndSixty_ShouldBeNorth()
    {
        // The index wraps: 360 / 22.5 is 16, which is past the end of the table.
        new RelativeLocation("Surigao City", null, 40d, 360d).Direction.ShouldBe("N");
    }

    [Fact]
    public void APositionInsideTheTown_ShouldNotClaimADirection()
    {
        // A sub-kilometre offset is well below the disagreement between agencies about where an
        // epicentre was — the two 2017 Surigao epicentres are 2.54 km apart — so stating
        // "0.4 km NNE of" would assert a precision the data does not carry.
        var location = RelativeLocation.Between(
            "Surigao City",
            null,
            SurigaoCityLatitude,
            SurigaoCityLongitude,
            latitude: 9.7515d,
            longitude: 125.5008d);

        location.DistanceKm.ShouldBeLessThan(1d);
        location.Describe().ShouldBe("At Surigao City");
    }

    [Fact]
    public void TheBearing_ShouldRunFromThePlaceToThePosition()
    {
        // The phrase says the epicentre lies north of the town, so the bearing is measured from
        // the town. Reversing the arguments is the defect this pins: it would report S.
        var location = RelativeLocation.Between(
            "Surigao City",
            null,
            SurigaoCityLatitude,
            SurigaoCityLongitude,
            latitude: 10.75d,
            longitude: 125.5d);

        location.BearingDegrees.ShouldBe(0d, tolerance: 0.001d);
        location.Direction.ShouldBe("N");
    }
}
