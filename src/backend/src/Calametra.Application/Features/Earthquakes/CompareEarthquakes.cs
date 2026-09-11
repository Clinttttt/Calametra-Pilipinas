using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Earthquakes.Shared;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Events;
using Calametra.Domain.Geospatial;
using Calametra.Domain.Seismology;
using FluentValidation;

namespace Calametra.Application.Features.Earthquakes;

/// <summary>
/// Two earthquakes set against each other, with every comparison qualified.
/// </summary>
/// <remarks>
/// <para>
/// Each side is the same shape as the detail view, so a reader sees both events' full
/// reading sets rather than one preferred number each. On top of that sit the three
/// cross-event differences — separation, magnitude, depth — each of which may be
/// unavailable, and each of which says so rather than substituting a figure.
/// </para>
/// <para>
/// <b>The comparison is often the interesting part precisely because it fails.</b> Two
/// Philippine earthquakes frequently cannot be compared on magnitude at all: PHIVOLCS
/// favours surface-wave magnitude for large local events while the USGS archive is 92.8%
/// body-wave, and those measure different things. A comparison view that printed a
/// difference anyway would manufacture agreement.
/// </para>
/// </remarks>
public static class CompareEarthquakes
{
    public sealed record Query : IQuery<ComparisonResponse>
    {
        public required Guid LeftEventId { get; init; }

        public required Guid RightEventId { get; init; }
    }

    /// <param name="Left">The event already in view; the comparison is expressed relative to it.</param>
    /// <param name="Right">The event brought in for comparison.</param>
    public sealed record ComparisonResponse(
        EarthquakeDetailResponse Left,
        EarthquakeDetailResponse Right,
        ComparisonDeltas Deltas);

    /// <summary>
    /// The three cross-event differences.
    /// </summary>
    /// <param name="SeparationKm">
    /// Distance between the two preferred epicentres. Always available, since every stored
    /// observation carries a position.
    /// </param>
    /// <param name="TimeApart">
    /// Human phrasing of the interval — "6 years apart", "4 hours apart". Included because a
    /// raw span in days is unreadable across a 125-year archive.
    /// </param>
    /// <param name="DaysApart">The raw interval, for a client that wants to sort or scale by it.</param>
    /// <param name="MagnitudeDelta">
    /// Null when the two preferred readings use scale families that cannot be compared.
    /// </param>
    /// <param name="MagnitudeNote">
    /// Why the magnitudes can or cannot be compared, in plain language. Present in both
    /// cases: when they are comparable it names the shared family, and when they are not it
    /// names both.
    /// </param>
    /// <param name="DepthDelta">Null when either depth was assigned rather than measured.</param>
    /// <param name="DepthNote">Why the depths can or cannot be compared.</param>
    public sealed record ComparisonDeltas(
        double SeparationKm,
        string TimeApart,
        double DaysApart,
        double? MagnitudeDelta,
        string MagnitudeNote,
        double? DepthDelta,
        string DepthNote);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.LeftEventId).NotEmpty();
            RuleFor(query => query.RightEventId).NotEmpty();

            RuleFor(query => query)
                .Must(query => query.LeftEventId != query.RightEventId)
                .WithMessage("Comparing an event with itself has no meaning.");
        }
    }

    internal sealed class Handler(IApplicationDbContext context)
        : IQueryHandler<Query, ComparisonResponse>
    {
        public async Task<Result<ComparisonResponse>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var left = await EarthquakeDetailLoader.Load(context, request.LeftEventId, cancellationToken);

            if (left is null)
            {
                return Result<ComparisonResponse>.Failure(EventErrors.NotFound);
            }

            var right = await EarthquakeDetailLoader.Load(context, request.RightEventId, cancellationToken);

            if (right is null)
            {
                return Result<ComparisonResponse>.Failure(EventErrors.NotFound);
            }

            return Result<ComparisonResponse>.Success(
                new ComparisonResponse(left, right, Compare(left, right)));
        }

        /// <summary>
        /// Derives the three differences from the two preferred readings.
        /// </summary>
        /// <remarks>
        /// The preferred reading is used rather than an average across agencies, because
        /// averaging magnitudes from different scales is exactly the error this platform
        /// exists to make visible.
        /// </remarks>
        private static ComparisonDeltas Compare(
            EarthquakeDetailResponse left,
            EarthquakeDetailResponse right)
        {
            var separationKm = GeoDistance.HaversineKm(
                left.Latitude,
                left.Longitude,
                right.Latitude,
                right.Longitude);

            var interval = right.OccurredAt - left.OccurredAt;
            var days = Math.Abs(interval.TotalDays);

            var leftPreferred = Preferred(left);
            var rightPreferred = Preferred(right);

            var (magnitudeDelta, magnitudeNote) = CompareMagnitudes(leftPreferred, rightPreferred);
            var (depthDelta, depthNote) = CompareDepths(leftPreferred, rightPreferred);

            return new ComparisonDeltas(
                Math.Round(separationKm, 1),
                DescribeInterval(days),
                Math.Round(days, 2),
                magnitudeDelta,
                magnitudeNote,
                depthDelta,
                depthNote);
        }

        private static ObservationResponse? Preferred(EarthquakeDetailResponse detail)
        {
            // Indexed loop rather than FirstOrDefault: Observations is an IReadOnlyList, and
            // the analyser rightly objects to Enumerable methods over an indexable collection.
            for (var i = 0; i < detail.Observations.Count; i++)
            {
                if (detail.Observations[i].IsPreferred)
                {
                    return detail.Observations[i];
                }
            }

            // Falls back to the first reading. An event with observations but no preferred
            // one is a seeding gap rather than a normal state, but it should still render.
            return detail.Observations.Count > 0 ? detail.Observations[0] : null;
        }

        private static (double? Delta, string Note) CompareMagnitudes(
            ObservationResponse? left,
            ObservationResponse? right)
        {
            if (left?.Magnitude is not { } leftMagnitude || right?.Magnitude is not { } rightMagnitude)
            {
                return (null, "At least one event has no reported magnitude, so there is nothing to compare.");
            }

            // Parsed back to the domain types rather than compared as strings: comparability
            // is a rule about scale families, and the rule lives in the domain.
            var leftReading = new MagnitudeReading(leftMagnitude.Value, ParseScale(leftMagnitude.Scale));
            var rightReading = new MagnitudeReading(rightMagnitude.Value, ParseScale(rightMagnitude.Scale));

            var delta = leftReading.DifferenceFrom(rightReading);

            if (delta is null)
            {
                return (
                    null,
                    $"{leftMagnitude.Display} and {rightMagnitude.Display} cannot be differenced. "
                    + $"{leftMagnitude.Scale} is a {EarthquakeDetailLoader.Describe(leftReading.Type.Family())} "
                    + $"magnitude and {rightMagnitude.Scale} is a "
                    + $"{EarthquakeDetailLoader.Describe(rightReading.Type.Family())} magnitude. They measure "
                    + "different physical quantities, and body-wave magnitude saturates near M6, so the "
                    + "larger event is not necessarily the one with the larger number.");
            }

            var family = EarthquakeDetailLoader.Describe(leftReading.Type.Family());

            return (
                Math.Round(delta.Value, 2),
                delta.Value < 0.05d
                    ? $"Both report effectively the same {family} magnitude."
                    : $"Both use a {family} magnitude, so the {delta.Value:0.0} difference is a real "
                        + "difference in size rather than an artefact of different scales.");
        }

        private static (double? Delta, string Note) CompareDepths(
            ObservationResponse? left,
            ObservationResponse? right)
        {
            var leftDepth = left?.Depth;
            var rightDepth = right?.Depth;

            if (leftDepth is null || rightDepth is null)
            {
                return (null, "At least one event has no reported depth.");
            }

            if (!leftDepth.IsMeasured || !rightDepth.IsMeasured)
            {
                // Named specifically, because "not comparable" alone would leave the reader
                // guessing which of the two is the problem.
                var culprit = !leftDepth.IsMeasured && !rightDepth.IsMeasured
                    ? "Both depths were"
                    : !leftDepth.IsMeasured
                        ? "The first event's depth was"
                        : "The second event's depth was";

                return (
                    null,
                    $"{culprit} fixed to an agency default rather than resolved from the recordings, "
                    + "so the two cannot be differenced. Roughly 43% of the archive carries such a depth.");
            }

            var delta = Math.Abs(leftDepth.Kilometres!.Value - rightDepth.Kilometres!.Value);

            return (
                Math.Round(delta, 1),
                delta < 5d
                    ? "Both were measured at a comparable depth in the crust."
                    : $"Both depths were measured, so the {delta:0.#} km difference is real.");
        }

        /// <summary>
        /// Recovers the scale enum from its label.
        /// </summary>
        /// <remarks>
        /// The response carries the label rather than the enum, so it has to be parsed back
        /// to apply the comparability rule. Unknown falls through to
        /// <see cref="MagnitudeType.Unknown"/>, which is comparable with nothing — the safe
        /// direction, since it produces a null delta rather than a fabricated one.
        /// </remarks>
        private static MagnitudeType ParseScale(string label) =>
            Enum.GetValues<MagnitudeType>()
                .FirstOrDefault(
                    scale => string.Equals(scale.Label(), label, StringComparison.OrdinalIgnoreCase),
                    MagnitudeType.Unknown);

        /// <summary>
        /// The interval in the largest unit that still reads naturally.
        /// </summary>
        /// <remarks>
        /// The archive spans 1901 to 2026, so a single unit cannot serve. "45,231 days apart"
        /// is technically precise and practically useless.
        /// </remarks>
        private static string DescribeInterval(double days) => days switch
        {
            < 1d => $"{days * 24d:0.#} hours apart",
            < 60d => $"{days:0.#} days apart",
            < 730d => $"{days / 30.44d:0.#} months apart",
            _ => $"{days / 365.25d:0.#} years apart",
        };
    }
}
