using Calametra.Domain.Seismology;

namespace Calametra.Domain.Similarity;

/// <summary>
/// Why one event was considered similar to another, component by component.
/// </summary>
/// <remarks>
/// This type is the answer to the project concept's "Why is this event considered
/// similar?" requirement. It is returned instead of a bare score so the UI can
/// render the reasoning rather than an opaque number, and so a reviewer can audit
/// the arithmetic.
/// <para>
/// Null components are meaningful and must be displayed as such — "depths not
/// comparable" is information, not a missing value to hide.
/// </para>
/// </remarks>
public sealed record SimilarityBreakdown
{
    /// <summary>Epicentral distance between the two events, in kilometres.</summary>
    public required double DistanceKm { get; init; }

    /// <summary>
    /// Absolute magnitude difference, or null when the two readings use scale
    /// families that cannot be compared.
    /// </summary>
    public double? MagnitudeDelta { get; init; }

    /// <summary>
    /// Absolute depth difference, or null when either depth is unknown or was
    /// fixed to an agency default.
    /// </summary>
    public double? DepthDeltaKm { get; init; }

    /// <summary>Scale family of the reference reading, for display.</summary>
    public required MagnitudeScaleFamily ReferenceScaleFamily { get; init; }

    /// <summary>Scale family of the candidate reading, for display.</summary>
    public required MagnitudeScaleFamily CandidateScaleFamily { get; init; }

    /// <summary>
    /// Composite closeness in the range 0–1, where 1 is an exact match on every
    /// available component. Components that are null are omitted from the average
    /// rather than counted as zero, so an event with unusable depth is not
    /// penalised for the catalogue's shortcoming.
    /// </summary>
    public required double Score { get; init; }

    /// <summary>Human-readable statements, one per contributing component.</summary>
    public required IReadOnlyList<string> Explanations { get; init; }

    /// <summary>
    /// Computes the breakdown for a candidate against a reference event.
    /// Pure arithmetic on already-fetched values: no I/O, no spatial queries.
    /// </summary>
    public static SimilarityBreakdown Compute(
        double distanceKm,
        MagnitudeReading? referenceMagnitude,
        MagnitudeReading? candidateMagnitude,
        DepthReading referenceDepth,
        DepthReading candidateDepth,
        SimilarityCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var explanations = new List<string>();
        var components = new List<double>();

        // Distance term — always available.
        var distanceCloseness = Normalise(distanceKm, criteria.MaxDistanceKm);
        components.Add(distanceCloseness);
        explanations.Add($"{distanceKm:0.#} km from the reference epicentre.");

        // Magnitude term — only when the scales are comparable.
        double? magnitudeDelta = null;

        if (referenceMagnitude is { } reference && candidateMagnitude is { } candidate)
        {
            magnitudeDelta = reference.DifferenceFrom(candidate);

            if (magnitudeDelta is { } delta)
            {
                components.Add(Normalise(delta, criteria.MaxMagnitudeDelta));
                explanations.Add(
                    $"Magnitude differs by {delta:0.0} ({reference.Display()} vs {candidate.Display()}).");
            }
            else
            {
                explanations.Add(
                    $"Magnitudes are not directly comparable: {reference.Type.Label()} measures "
                    + $"{reference.Type.Family()} while {candidate.Type.Label()} measures "
                    + $"{candidate.Type.Family()}.");
            }
        }
        else
        {
            explanations.Add("At least one event has no reported magnitude.");
        }

        // Depth term — only when both depths are quantitative.
        double? depthDelta = null;

        if (criteria.MaxDepthDeltaKm is { } maxDepthDelta)
        {
            depthDelta = referenceDepth.DifferenceFrom(candidateDepth);

            if (depthDelta is { } delta)
            {
                components.Add(Normalise(delta, maxDepthDelta));
                explanations.Add($"Depth differs by {delta:0.#} km.");
            }
            else
            {
                explanations.Add(
                    "Depths are not comparable: at least one was unknown or fixed to an agency "
                    + "default rather than measured.");
            }
        }

        return new SimilarityBreakdown
        {
            DistanceKm = distanceKm,
            MagnitudeDelta = magnitudeDelta,
            DepthDeltaKm = depthDelta,
            ReferenceScaleFamily = referenceMagnitude?.Type.Family() ?? MagnitudeScaleFamily.Unknown,
            CandidateScaleFamily = candidateMagnitude?.Type.Family() ?? MagnitudeScaleFamily.Unknown,
            Score = components.Count == 0 ? 0d : components.Average(),
            Explanations = explanations,
        };
    }

    /// <summary>
    /// Maps a difference onto 0–1 closeness against its tolerance. A difference of
    /// zero scores 1; a difference at or beyond the tolerance scores 0. Linear, so
    /// the number shown to a user is one they can reproduce by hand.
    /// </summary>
    private static double Normalise(double difference, double tolerance) =>
        tolerance <= 0d ? 0d : Math.Clamp(1d - (Math.Abs(difference) / tolerance), 0d, 1d);
}
