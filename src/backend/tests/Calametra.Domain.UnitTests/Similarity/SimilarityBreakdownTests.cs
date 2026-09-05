using Calametra.Domain.Seismology;
using Calametra.Domain.Similarity;

namespace Calametra.Domain.UnitTests.Similarity;

/// <summary>
/// The similarity result must always be explainable. These tests assert that the
/// breakdown reports what it could not compute, rather than silently substituting
/// a value.
/// </summary>
public sealed class SimilarityBreakdownTests
{
    [Fact]
    public void AnExactMatch_ShouldScoreOne()
    {
        var breakdown = SimilarityBreakdown.Compute(
            distanceKm: 0d,
            referenceMagnitude: new MagnitudeReading(6.5d, MagnitudeType.Mww),
            candidateMagnitude: new MagnitudeReading(6.5d, MagnitudeType.Mww),
            referenceDepth: DepthReading.Constrained(15d),
            candidateDepth: DepthReading.Constrained(15d),
            SimilarityCriteria.Default);

        breakdown.Score.ShouldBe(1d, tolerance: 0.0001d);
    }

    [Fact]
    public void ScoreShouldFallAsDifferencesGrow()
    {
        var near = Compute(distanceKm: 20d, candidateMagnitude: 6.4d, candidateDepthKm: 18d);
        var far = Compute(distanceKm: 140d, candidateMagnitude: 6.0d, candidateDepthKm: 40d);

        near.Score.ShouldBeGreaterThan(far.Score);
    }

    [Fact]
    public void EveryComponentShouldProduceAnExplanation()
    {
        var breakdown = Compute(distanceKm: 56d, candidateMagnitude: 6.1d, candidateDepthKm: 32d);

        breakdown.Explanations.Count.ShouldBe(3);
        breakdown.Explanations.ShouldContain(line => line.Contains("km from the reference epicentre"));
        breakdown.Explanations.ShouldContain(line => line.Contains("Magnitude differs by"));
        breakdown.Explanations.ShouldContain(line => line.Contains("Depth differs by"));
    }

    [Fact]
    public void IncomparableScales_ShouldYieldANullDeltaAndSayWhy()
    {
        var breakdown = SimilarityBreakdown.Compute(
            distanceKm: 31d,
            referenceMagnitude: new MagnitudeReading(6.5d, MagnitudeType.Mww),
            candidateMagnitude: new MagnitudeReading(6.4d, MagnitudeType.Mb),
            referenceDepth: DepthReading.Constrained(15d),
            candidateDepth: DepthReading.Constrained(20d),
            SimilarityCriteria.Default);

        breakdown.MagnitudeDelta.ShouldBeNull();
        breakdown.ReferenceScaleFamily.ShouldBe(MagnitudeScaleFamily.Moment);
        breakdown.CandidateScaleFamily.ShouldBe(MagnitudeScaleFamily.BodyWave);
        breakdown.Explanations.ShouldContain(line => line.Contains("not directly comparable"));
    }

    [Fact]
    public void AnAssignedDepth_ShouldNotBeScoredAndShouldBeDisclosed()
    {
        var breakdown = SimilarityBreakdown.Compute(
            distanceKm: 10d,
            referenceMagnitude: new MagnitudeReading(6.5d, MagnitudeType.Mww),
            candidateMagnitude: new MagnitudeReading(6.5d, MagnitudeType.Mww),
            referenceDepth: DepthReading.Constrained(15d),
            candidateDepth: DepthReading.OperatorAssigned(10d),
            SimilarityCriteria.Default);

        breakdown.DepthDeltaKm.ShouldBeNull();
        breakdown.Explanations.ShouldContain(line => line.Contains("fixed to an agency"));
    }

    [Fact]
    public void AnUnusableComponent_ShouldNotBePenalisedAsZero()
    {
        // A candidate whose depth the catalogue could not resolve should not be
        // ranked below an otherwise identical candidate. The depth term is dropped
        // from the average, not scored zero.
        var withAssignedDepth = SimilarityBreakdown.Compute(
            distanceKm: 0d,
            referenceMagnitude: new MagnitudeReading(6.5d, MagnitudeType.Mww),
            candidateMagnitude: new MagnitudeReading(6.5d, MagnitudeType.Mww),
            referenceDepth: DepthReading.Constrained(15d),
            candidateDepth: DepthReading.OperatorAssigned(10d),
            SimilarityCriteria.Default);

        withAssignedDepth.Score.ShouldBe(1d, tolerance: 0.0001d);
    }

    private static SimilarityBreakdown Compute(
        double distanceKm,
        double candidateMagnitude,
        double candidateDepthKm) =>
        SimilarityBreakdown.Compute(
            distanceKm,
            new MagnitudeReading(6.5d, MagnitudeType.Mww),
            new MagnitudeReading(candidateMagnitude, MagnitudeType.Mww),
            DepthReading.Constrained(15d),
            DepthReading.Constrained(candidateDepthKm),
            SimilarityCriteria.Default);
}
