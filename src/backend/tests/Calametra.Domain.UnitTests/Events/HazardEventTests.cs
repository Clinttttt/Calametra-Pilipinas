using Calametra.Domain.Events;
using Calametra.Domain.Geospatial;
using Calametra.Domain.Seismology;

namespace Calametra.Domain.UnitTests.Events;

/// <summary>
/// Uses the real 10 February 2017 Surigao earthquake as the fixture, because it is
/// the case that motivated splitting HazardEvent from EventObservation:
/// PHIVOLCS reports Ms 6.7 at 10 km, USGS reports Mww 6.5 at 15 km
/// (event id us20008ixa). Both readings are correct.
/// </summary>
public sealed class HazardEventTests
{
    private static readonly DateTimeOffset OriginTime =
        new(2017, 2, 10, 14, 3, 43, TimeSpan.Zero);

    private static readonly DateTimeOffset Now =
        new(2026, 9, 4, 0, 0, 0, TimeSpan.Zero);

    private static readonly Guid PhivolcsSourceId = Guid.CreateVersion7();
    private static readonly Guid UsgsSourceId = Guid.CreateVersion7();

    private static HazardEvent SurigaoEvent()
    {
        var creation = HazardEvent.Create(
            HazardEventType.Earthquake,
            OriginTime,
            Wgs84.Point(9.9071d, 125.4516d),
            Now);

        creation.IsSuccess.ShouldBeTrue();

        return creation.Value;
    }

    [Fact]
    public void Create_ShouldRejectAnUnknownHazardType()
    {
        var result = HazardEvent.Create(
            HazardEventType.Unknown,
            OriginTime,
            Wgs84.Point(9.9071d, 125.4516d),
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(EventErrors.UnknownType);
    }

    [Fact]
    public void AddObservation_ShouldRequireAnExternalIdentifier()
    {
        var hazardEvent = SurigaoEvent();

        var result = hazardEvent.AddObservation(
            UsgsSourceId,
            externalEventId: "   ",
            OriginTime,
            Wgs84.Point(9.9071d, 125.4516d),
            DepthReading.Constrained(15d),
            new MagnitudeReading(6.5d, MagnitudeType.Mww),
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(EventErrors.ExternalIdRequired);
    }

    [Fact]
    public void AddObservation_ShouldRejectASecondReadingFromTheSameSource()
    {
        var hazardEvent = SurigaoEvent();
        AddUsgsObservation(hazardEvent);

        // A repeat report from the same agency is a revision, not a new observation.
        var duplicate = hazardEvent.AddObservation(
            UsgsSourceId,
            "us20008ixa",
            OriginTime,
            Wgs84.Point(9.9071d, 125.4516d),
            DepthReading.Constrained(15d),
            new MagnitudeReading(6.5d, MagnitudeType.Mww),
            Now);

        duplicate.IsFailure.ShouldBeTrue();
        duplicate.Error.ShouldBe(EventErrors.DuplicateObservation);
        hazardEvent.Observations.Count.ShouldBe(1);
    }

    [Fact]
    public void FirstObservation_ShouldBecomeThePreferredOne()
    {
        var hazardEvent = SurigaoEvent();
        var observation = AddUsgsObservation(hazardEvent);

        hazardEvent.PreferredObservationId.ShouldBe(observation.Id);
        hazardEvent.CanonicalOccurredAt.ShouldBe(OriginTime);
    }

    [Fact]
    public void SingleObservation_ShouldNotReportDisagreement()
    {
        var hazardEvent = SurigaoEvent();
        AddUsgsObservation(hazardEvent);

        hazardEvent.HasMultipleObservations.ShouldBeFalse();
        hazardEvent.HasMagnitudeDisagreement.ShouldBeFalse();
    }

    [Fact]
    public void TwoAgencies_OnIncomparableScales_ShouldReportDisagreement()
    {
        var hazardEvent = SurigaoEvent();
        AddUsgsObservation(hazardEvent);
        AddPhivolcsObservation(hazardEvent);

        hazardEvent.HasMultipleObservations.ShouldBeTrue();

        // Ms 6.7 versus Mww 6.5 differ by 0.2 numerically, but they measure
        // different quantities. Either way the UI must disclose it rather than
        // print one figure.
        hazardEvent.HasMagnitudeDisagreement.ShouldBeTrue();
        hazardEvent.Observations.Count.ShouldBe(2);
    }

    [Fact]
    public void SetPreferredObservation_ShouldRealignTheCanonicalFields()
    {
        var hazardEvent = SurigaoEvent();
        AddUsgsObservation(hazardEvent);
        var phivolcs = AddPhivolcsObservation(hazardEvent);

        var result = hazardEvent.SetPreferredObservation(phivolcs.Id, Now);

        result.IsSuccess.ShouldBeTrue();
        hazardEvent.PreferredObservationId.ShouldBe(phivolcs.Id);
        hazardEvent.PreferredObservation!.Magnitude!.Value.Type.ShouldBe(MagnitudeType.Ms);
    }

    [Fact]
    public void SetPreferredObservation_ShouldRejectAnObservationFromAnotherEvent()
    {
        var hazardEvent = SurigaoEvent();
        AddUsgsObservation(hazardEvent);

        var result = hazardEvent.SetPreferredObservation(Guid.CreateVersion7(), Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(EventErrors.ObservationNotFound);
    }

    [Fact]
    public void AddTrackPoint_ShouldBeRejectedForAnEarthquake()
    {
        var hazardEvent = SurigaoEvent();

        var result = hazardEvent.AddTrackPoint(
            UsgsSourceId,
            OriginTime,
            Wgs84.Point(9.9d, 125.4d),
            Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(EventErrors.TrackPointsNotApplicable);
    }

    private static Domain.Events.EventObservation AddUsgsObservation(HazardEvent hazardEvent)
    {
        var result = hazardEvent.AddObservation(
            UsgsSourceId,
            "us20008ixa",
            OriginTime,
            Wgs84.Point(9.9071d, 125.4516d),
            DepthReading.Constrained(15d),
            new MagnitudeReading(6.5d, MagnitudeType.Mww),
            Now,
            "https://earthquake.usgs.gov/earthquakes/eventpage/us20008ixa");

        result.IsSuccess.ShouldBeTrue();

        return result.Value;
    }

    private static Domain.Events.EventObservation AddPhivolcsObservation(HazardEvent hazardEvent)
    {
        var result = hazardEvent.AddObservation(
            PhivolcsSourceId,
            "2017_0210_1403",
            OriginTime,
            Wgs84.Point(9.93d, 125.45d),
            DepthReading.Constrained(10d),
            new MagnitudeReading(6.7d, MagnitudeType.Ms),
            Now);

        result.IsSuccess.ShouldBeTrue();

        return result.Value;
    }
}
