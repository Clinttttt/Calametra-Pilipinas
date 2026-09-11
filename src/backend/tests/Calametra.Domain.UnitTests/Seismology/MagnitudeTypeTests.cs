using Calametra.Domain.Seismology;

namespace Calametra.Domain.UnitTests.Seismology;

/// <summary>
/// Guards the rule that magnitudes may only be compared within a scale family.
/// </summary>
public sealed class MagnitudeTypeTests
{
    [Theory]
    [InlineData("mb", MagnitudeType.Mb)]
    [InlineData("mww", MagnitudeType.Mww)]
    [InlineData("mwb", MagnitudeType.Mwb)]
    [InlineData("ms", MagnitudeType.Ms)]
    [InlineData("ml", MagnitudeType.Ml)]
    [InlineData("MWW", MagnitudeType.Mww)]
    [InlineData("  mb  ", MagnitudeType.Mb)]
    public void Parse_ShouldRecogniseTheScalesPresentInThePhilippineCatalogue(string reported, MagnitudeType expected) =>
        MagnitudeTypeExtensions.Parse(reported).ShouldBe(expected);

    [Theory]
    [InlineData("mystery-scale")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_ShouldReturnUnknown_RatherThanThrow_ForUnrecognisedInput(string? reported) =>
        // An unfamiliar scale is a data-quality fact to record, not a reason to
        // abandon an ingestion run.
        MagnitudeTypeExtensions.Parse(reported).ShouldBe(MagnitudeType.Unknown);

    [Theory]
    [InlineData(MagnitudeType.Mb, MagnitudeScaleFamily.BodyWave)]
    [InlineData(MagnitudeType.MbLg, MagnitudeScaleFamily.BodyWave)]
    [InlineData(MagnitudeType.Ms, MagnitudeScaleFamily.SurfaceWave)]
    [InlineData(MagnitudeType.Ml, MagnitudeScaleFamily.Local)]
    [InlineData(MagnitudeType.Mww, MagnitudeScaleFamily.Moment)]
    [InlineData(MagnitudeType.Mwb, MagnitudeScaleFamily.Moment)]
    public void Family_ShouldGroupScalesByThePhysicalQuantityTheyMeasure(
        MagnitudeType type,
        MagnitudeScaleFamily expected) =>
        type.Family().ShouldBe(expected);

    [Fact]
    public void MomentMagnitudes_ShouldBeComparableWithEachOther() =>
        MagnitudeType.Mww.IsComparableWith(MagnitudeType.Mwb).ShouldBeTrue();

    [Fact]
    public void BodyWave_ShouldNotBeComparableWithMoment()
    {
        // This is the case that matters most: the Philippine USGS catalogue is 93% mb,
        // while every large event is mww. Similarity search that ignored scale
        // would compare the flagship events against a body-wave population.
        MagnitudeType.Mb.IsComparableWith(MagnitudeType.Mww).ShouldBeFalse();
    }

    [Fact]
    public void SurfaceWave_ShouldNotBeComparableWithMoment()
    {
        // PHIVOLCS reports 2017 Surigao as Ms 6.7; USGS reports it as Mww 6.5.
        MagnitudeType.Ms.IsComparableWith(MagnitudeType.Mww).ShouldBeFalse();
    }

    [Fact]
    public void Unknown_ShouldNotBeComparableEvenWithItself() =>
        // Two values of unstated provenance tell us nothing about each other.
        MagnitudeType.Unknown.IsComparableWith(MagnitudeType.Unknown).ShouldBeFalse();

    [Fact]
    public void DifferenceFrom_ShouldReturnNull_WhenScalesAreNotComparable()
    {
        var phivolcs = new MagnitudeReading(6.7d, MagnitudeType.Ms);
        var usgs = new MagnitudeReading(6.5d, MagnitudeType.Mww);

        // Naive arithmetic would report 0.2 and imply the two agencies nearly agree
        // on the same quantity. They are not measuring the same quantity.
        phivolcs.DifferenceFrom(usgs).ShouldBeNull();
    }

    [Fact]
    public void DifferenceFrom_ShouldComputeDelta_WhenScalesAreComparable()
    {
        var first = new MagnitudeReading(6.5d, MagnitudeType.Mww);
        var second = new MagnitudeReading(6.1d, MagnitudeType.Mwb);

        first.DifferenceFrom(second)!.Value.ShouldBe(0.4d, tolerance: 0.0001d);
    }

    [Fact]
    public void Display_ShouldAlwaysIncludeTheScale() =>
        new MagnitudeReading(6.5d, MagnitudeType.Mww).Display().ShouldBe("Mww 6.5");
}
