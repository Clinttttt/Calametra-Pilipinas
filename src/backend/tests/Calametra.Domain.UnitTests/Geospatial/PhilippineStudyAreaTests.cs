using Calametra.Domain.Geospatial;

namespace Calametra.Domain.UnitTests.Geospatial;

/// <summary>
/// Locks the national bounding box.
/// </summary>
/// <remarks>
/// The study area is the ingestion boundary, so narrowing it silently drops events
/// and narrows the archive without any error. These tests name real places at the
/// extremes of the archipelago, so a regression fails with a recognisable message
/// rather than a changed number.
/// </remarks>
public sealed class PhilippineStudyAreaTests
{
    [Theory]
    // Land extremes.
    [InlineData("Itbayat, Batanes — northernmost town", 20.78d, 121.84d)]
    [InlineData("Sitangkai, Tawi-Tawi — southernmost town", 4.66d, 119.39d)]
    [InlineData("Balabac, Palawan — south-west", 7.99d, 117.06d)]
    [InlineData("Pusan Point, Davao Oriental — easternmost land", 7.28d, 126.60d)]
    // Population centres.
    [InlineData("Metro Manila", 14.60d, 120.98d)]
    [InlineData("Cebu City", 10.32d, 123.90d)]
    [InlineData("Davao City", 7.07d, 125.61d)]
    [InlineData("Surigao City", 9.79d, 125.49d)]
    public void TheStudyArea_ShouldContainThePlacesItClaimsTo(string place, double latitude, double longitude) =>
        PhilippineStudyArea.Contains(latitude, longitude)
            .ShouldBeTrue($"{place} must be inside the study area.");

    [Theory]
    // Offshore trench axes. These are the reason the box extends past the coastline:
    // much of the archipelago's seismicity, including the deepest slab events,
    // originates here rather than under land.
    [InlineData("Philippine Trench, off Surigao", 9.50d, 127.30d)]
    [InlineData("Manila Trench, west of Luzon", 15.50d, 118.50d)]
    [InlineData("Cotabato Trench, off western Mindanao", 6.50d, 122.00d)]
    [InlineData("Negros Trench", 9.50d, 122.00d)]
    public void TheStudyArea_ShouldReachTheOffshoreTrenches(string feature, double latitude, double longitude) =>
        PhilippineStudyArea.Contains(latitude, longitude)
            .ShouldBeTrue($"{feature} must be inside the study area: it is a source of Philippine seismicity.");

    [Theory]
    [InlineData("Taipei", 25.03d, 121.57d)]
    [InlineData("Ho Chi Minh City", 10.82d, 106.63d)]
    [InlineData("Palau", 7.51d, 134.58d)]
    [InlineData("Northern Sulawesi", 1.49d, 124.84d)]
    public void TheStudyArea_ShouldExcludeNeighbouringCountries(string place, double latitude, double longitude) =>
        // The box is generous offshore but must not quietly become a regional
        // catalogue: an event in Taiwan or Sulawesi is not a Philippine event.
        PhilippineStudyArea.Contains(latitude, longitude)
            .ShouldBeFalse($"{place} is outside the Philippines and must not be ingested.");

    [Fact]
    public void TheDefaultCamera_ShouldSitInsideTheStudyArea() =>
        PhilippineStudyArea.Contains(
                PhilippineStudyArea.CentreLatitude,
                PhilippineStudyArea.CentreLongitude)
            .ShouldBeTrue();

    [Fact]
    public void EverySubArea_ShouldFallInsideTheStudyArea()
    {
        foreach (var subArea in PhilippineStudyArea.SubAreas)
        {
            PhilippineStudyArea.Contains(subArea.MinLatitude, subArea.MinLongitude)
                .ShouldBeTrue($"{subArea.Name} lower corner must be inside the study area.");

            PhilippineStudyArea.Contains(subArea.MaxLatitude, subArea.MaxLongitude)
                .ShouldBeTrue($"{subArea.Name} upper corner must be inside the study area.");
        }
    }

    [Fact]
    public void Caraga_ShouldStillBeAvailableAsAReferenceArea()
    {
        // The platform is national now, but CARAGA remains its principal reference
        // area: the 2017 Surigao earthquake and the December 2023 Mindanao sequence
        // are both there, and both are used to demonstrate the platform.
        var caraga = PhilippineStudyArea.Caraga;

        caraga.Name.ShouldBe("CARAGA");

        // The 2017 Surigao epicentre.
        (9.9071d >= caraga.MinLatitude && 9.9071d <= caraga.MaxLatitude).ShouldBeTrue();
        (125.4516d >= caraga.MinLongitude && 125.4516d <= caraga.MaxLongitude).ShouldBeTrue();
    }
}
