using Calametra.Domain.Geospatial;
using Calametra.Domain.Places;

namespace Calametra.Domain.UnitTests.Places;

/// <summary>
/// Covers the invariants a place has to hold, all three of which exist because of a mistake this
/// platform can actually make.
/// </summary>
public sealed class PlaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);

    private static readonly Guid Gazetteer = Guid.CreateVersion7();

    [Fact]
    public void APlace_ShouldRequireTheGazetteerItCameFrom()
    {
        // A place name, its level and its coordinate are a publisher's claim. Two gazetteers
        // already disagree about the code for the same province, so an unattributed place is not
        // storable for the same reason an unattributed magnitude is not.
        var creation = Place.Create(
            "Surigao City",
            PlaceKind.City,
            Wgs84.Point(9.75, 125.5),
            Guid.Empty,
            Now);

        creation.IsFailure.ShouldBeTrue();
        creation.Error!.Code.ShouldBe("place.source_required");
    }

    [Fact]
    public void APlace_ShouldRefuseAnUnknownAdministrativeLevel()
    {
        var creation = Place.Create(
            "Surigao City",
            PlaceKind.Unknown,
            Wgs84.Point(9.75, 125.5),
            Gazetteer,
            Now);

        creation.IsFailure.ShouldBeTrue();
        creation.Error!.Code.ShouldBe("place.unknown_kind");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void APlace_ShouldRequireAName(string name)
    {
        var creation = Place.Create(name, PlaceKind.City, Wgs84.Point(9.75, 125.5), Gazetteer, Now);

        creation.IsFailure.ShouldBeTrue();
        creation.Error!.Code.ShouldBe("place.name_required");
    }

    [Fact]
    public void APlace_ShouldCarryItsCodeAndParentSeparatelyFromItsIdentity()
    {
        // The PSGC is the durable public identifier — guardrail 8 — and the internal id is minted
        // at insert and changes on re-ingest. Both exist, and they are not interchangeable.
        var place = Place.Create(
                "Surigao City",
                PlaceKind.City,
                Wgs84.Point(9.75, 125.5),
                Gazetteer,
                Now)
            .Value;

        var province = Guid.CreateVersion7();

        place.WithHierarchy("166724000", province);

        place.PsgcCode.ShouldBe("166724000");
        place.ParentPlaceId.ShouldBe(province);
        place.DataSourceId.ShouldBe(Gazetteer);
        place.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void APopulationFigure_ShouldCarryItsOwnSourceAndVintage()
    {
        // Separate from the gazetteer deliberately: the point and the population figure come from
        // different publishers, and the population figure is worthless without its census year.
        var place = Place.Create(
                "Surigao City",
                PlaceKind.City,
                Wgs84.Point(9.75, 125.5),
                Gazetteer,
                Now)
            .Value;

        var census = Guid.CreateVersion7();
        var asOf = new DateTimeOffset(2020, 5, 1, 0, 0, 0, TimeSpan.Zero);

        place.SetPopulation(154137, census, asOf, Now);

        place.PopulationEstimate.ShouldBe(154137);
        place.PopulationDataSourceId.ShouldBe(census);
        place.PopulationAsOf.ShouldBe(asOf);
        place.DataSourceId.ShouldBe(Gazetteer);
    }
}
