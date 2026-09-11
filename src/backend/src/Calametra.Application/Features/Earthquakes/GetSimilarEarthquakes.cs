using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Places.Shared;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Events;
using Calametra.Domain.Geospatial;
using Calametra.Domain.Seismology;
using Calametra.Domain.Similarity;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Earthquakes;

/// <summary>
/// Earthquakes resembling a given one, with the reasoning shown.
/// </summary>
/// <remarks>
/// <para>
/// The output is a <see cref="SimilarityBreakdown"/> per match, not a bare score. A single
/// number invites a reader to treat "87% similar" as a measurement; the component
/// statements make clear that similarity here means three specific comparisons, one of
/// which is frequently unavailable.
/// </para>
/// <para>
/// <b>Where the work happens.</b> PostGIS narrows candidates by distance using the GiST
/// index on the epicentre, and by magnitude scale where that is expressible in SQL. Scoring
/// then runs in memory via <see cref="SimilarityBreakdown.Compute"/>, which is pure
/// arithmetic and unit-tested. The domain deliberately keeps no I/O in the scoring path.
/// </para>
/// <para>
/// <b>Why the scale-family filter can be pushed to SQL.</b> Comparability is a property of
/// <see cref="MagnitudeType"/>, and the set of types comparable with the reference can be
/// enumerated in C# before the query runs. That becomes an <c>IN (...)</c> predicate, which
/// narrows the candidate set at the database rather than fetching a body-wave population
/// and discarding it in memory. 92.8% of the archive is <c>mb</c> while nearly every large
/// event is <c>mww</c>, so for a flagship event this filter removes most of the table.
/// </para>
/// <para>
/// <b>Matches are named after the nearest city or municipality.</b> This was previously
/// impossible: <c>HazardEvent</c> holds no descriptive location, and the sub-areas on
/// <see cref="PhilippineStudyArea"/> are viewport hints rather than administrative boundaries —
/// they overlap, so an event at 9°N 125°E falls inside Caraga, Visayas and Mindanao at once, and
/// labelling a match from them would have invented a provenance the data does not have. With the
/// gazetteer imported, the name comes from a measured distance and bearing to a published
/// coordinate, and is omitted entirely beyond 300 km.
/// </para>
/// </remarks>
public static class GetSimilarEarthquakes
{
    public sealed record Query : IQuery<SimilarEarthquakesResponse>
    {
        public required Guid EventId { get; init; }

        public double MaxDistanceKm { get; init; } = 150d;

        public double MaxMagnitudeDelta { get; init; } = 0.5d;

        /// <summary>Null disables the depth term entirely.</summary>
        public double? MaxDepthDeltaKm { get; init; } = 25d;

        /// <summary>
        /// When true (default), candidates reporting magnitude on an incomparable scale
        /// family are excluded.
        /// </summary>
        /// <remarks>
        /// Turning this off does not make such candidates comparable — the magnitude term is
        /// simply omitted from their score, leaving them ranked on distance and depth alone.
        /// The response reports how many were admitted this way so the UI can say so.
        /// </remarks>
        public bool RequireComparableMagnitudeScale { get; init; } = true;

        /// <summary>
        /// When true (default), candidates whose depth was assigned rather than measured are
        /// excluded from the results.
        /// </summary>
        /// <remarks>
        /// Distinct from the scoring behaviour: <see cref="SimilarityBreakdown.Compute"/>
        /// already omits an unusable depth from the score unconditionally, because
        /// <see cref="DepthReading.DifferenceFrom"/> returns null unless both depths are
        /// quantitative. This flag decides whether such an event appears at all. Excluded by
        /// default so a "similar depth" claim is never made about a depth nobody measured.
        /// </remarks>
        public bool ExcludeOperatorAssignedDepths { get; init; } = true;

        public int Limit { get; init; } = 10;
    }

    /// <param name="NearbyEvents">
    /// Events within the distance tolerance before any quality filter. The baseline the two
    /// exclusion counts are measured against, so they can be read as fractions of it.
    /// </param>
    /// <param name="ExcludedForIncomparableScale">
    /// Of those, how many report magnitude on a scale that cannot be compared with the
    /// reference.
    /// </param>
    /// <param name="ExcludedForAssignedDepth">
    /// Of those, how many carry a depth the agency assigned rather than measured. Overlaps
    /// with the scale count — both are measured against <paramref name="NearbyEvents"/>, not
    /// against each other.
    /// </param>
    /// <param name="CandidatesConsidered">What survived the filters and was actually scored.</param>
    public sealed record SimilarEarthquakesResponse(
        Guid ReferenceEventId,
        string ReferenceMagnitude,
        string ReferenceDepth,
        int NearbyEvents,
        int ExcludedForIncomparableScale,
        int ExcludedForAssignedDepth,
        int CandidatesConsidered,
        int AdmittedWithIncomparableMagnitude,
        IReadOnlyList<SimilarEarthquake> Matches);

    /// <param name="Score">0–1 composite. Reproducible by hand from the components below.</param>
    /// <param name="Location">
    /// The epicentre stated relative to the nearest city or municipality. Null beyond 300 km,
    /// where naming a town would describe the gazetteer's reach rather than the earthquake.
    /// </param>
    /// <param name="Explanations">One statement per component, including the unavailable ones.</param>
    public sealed record SimilarEarthquake(
        Guid EventId,
        DateTimeOffset OccurredAt,
        double Latitude,
        double Longitude,
        string? Location,
        string Agency,
        string MagnitudeDisplay,
        string DepthDisplay,
        double DistanceKm,
        double? MagnitudeDelta,
        double? DepthDeltaKm,
        bool MagnitudeComparable,
        double Score,
        IReadOnlyList<string> Explanations);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.EventId).NotEmpty();

            // 300 km upper bound, chosen from measurement rather than taste.
            //
            // Two reasons. Semantically, "similar to this earthquake" stops meaning anything
            // tectonic across hundreds of kilometres — at 300 km the candidate pool is
            // already ~8,700 events spanning multiple trench systems.
            //
            // Mechanically, there is a performance cliff just beyond it. Measured on the
            // 27,242-event archive: 400 km returns 12,008 nearby events in 0.13s, while
            // 500 km returns 16,534 in 15-23s. Nothing gradual happens in between — once
            // roughly 60% of the table satisfies the predicate, PostgreSQL abandons the GiST
            // bitmap index scan for a sequential scan, and geography ST_DWithin then runs
            // spheroid arithmetic on every row. The cap sits at 300 rather than at the
            // observed 400 km edge deliberately: if the PHIVOLCS DUA lands and the catalogue
            // grows tenfold with M2-3 events, the cliff moves inward, and a limit set at the
            // edge would quietly become a limit set past it.
            RuleFor(query => query.MaxDistanceKm).InclusiveBetween(1d, 300d);

            RuleFor(query => query.MaxMagnitudeDelta).InclusiveBetween(0.1d, 3d);

            RuleFor(query => query.MaxDepthDeltaKm)
                .InclusiveBetween(1d, 400d)
                .When(query => query.MaxDepthDeltaKm.HasValue);

            RuleFor(query => query.Limit).InclusiveBetween(1, 50);
        }
    }

    internal sealed class Handler(IApplicationDbContext context)
        : IQueryHandler<Query, SimilarEarthquakesResponse>
    {
        public async Task<Result<SimilarEarthquakesResponse>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var criteria = new SimilarityCriteria
            {
                MaxDistanceKm = request.MaxDistanceKm,
                MaxMagnitudeDelta = request.MaxMagnitudeDelta,
                MaxDepthDeltaKm = request.MaxDepthDeltaKm,
                RequireComparableMagnitudeScale = request.RequireComparableMagnitudeScale,
                ExcludeOperatorAssignedDepths = request.ExcludeOperatorAssignedDepths,
                Limit = request.Limit,
            };

            var reference = await (
                from hazardEvent in context.HazardEvents.AsNoTracking()
                join observation in context.EarthquakeObservations.AsNoTracking()
                    on hazardEvent.PreferredObservationId equals observation.Id
                where hazardEvent.Id == request.EventId
                select new
                {
                    hazardEvent.Id,
                    hazardEvent.CanonicalOccurredAt,
                    observation.Epicenter,
                    observation.Latitude,
                    observation.Longitude,
                    observation.MagnitudeValue,
                    observation.MagnitudeScale,
                    observation.DepthKilometres,
                    observation.DepthQuality,
                }).SingleOrDefaultAsync(cancellationToken);

            if (reference is null)
            {
                return Result<SimilarEarthquakesResponse>.Failure(EventErrors.NotFound);
            }

            var referenceMagnitude = reference.MagnitudeValue is { } value
                ? new MagnitudeReading(value, reference.MagnitudeScale)
                : (MagnitudeReading?)null;

            var referenceDepth = new DepthReading(reference.DepthKilometres, reference.DepthQuality);

            // ---- ONE spatial pass ---------------------------------------------------
            //
            // This shape is the product of measurement, and the earlier shapes are worth
            // recording so they are not reintroduced.
            //
            // Three separate CountAsync calls for the diagnostics cost 7.1s and 1.9s on two
            // of them, because each repeated the spatial scan. Folding them into one
            // conditional aggregate still left TWO expensive passes over the same rows —
            // once for candidates, once for the aggregate — and at a 500 km radius the pair
            // took 20-30s even though the equivalent hand-written SQL runs in 60 ms.
            //
            // The cause was never a missing index: EXPLAIN confirms the GiST index is used.
            // It is that PostGIS estimates ST_DWithin selectivity as `rows=2` when the true
            // answer is 7,907 — a 4,000x misestimate that produces bad downstream join
            // choices. Refreshing statistics with ANALYZE did not help and made the 150 km
            // case worse, because the planner cannot be argued out of a bad selectivity
            // estimate for a spatial function.
            //
            // So the fix is to stop asking the database to do more than it is good at. It
            // performs exactly one indexed ST_DWithin and returns scalar columns. No
            // ST_Distance projection, no correlated subquery for the agency, no second pass.
            // Distance, scoring and the diagnostic counts are all derived from that single
            // result set in memory, where they cost nothing measurable.
            var nearby = await context.HazardEvents.AsNoTracking()
                .Join(
                    context.EarthquakeObservations.AsNoTracking(),
                    hazardEvent => hazardEvent.PreferredObservationId,
                    observation => observation.Id,
                    (hazardEvent, observation) => new { hazardEvent, observation })
                .Where(row => row.hazardEvent.Id != request.EventId
                    && row.hazardEvent.Type == HazardEventType.Earthquake
                    && row.observation.Epicenter.IsWithinDistance(
                        reference.Epicenter,
                        request.MaxDistanceKm * 1_000d))
                .Select(row => new NearbyRow
                {
                    EventId = row.hazardEvent.Id,
                    OccurredAt = row.hazardEvent.CanonicalOccurredAt,
                    Latitude = row.observation.Latitude,
                    Longitude = row.observation.Longitude,
                    MagnitudeValue = row.observation.MagnitudeValue,
                    MagnitudeScale = row.observation.MagnitudeScale,
                    DepthKilometres = row.observation.DepthKilometres,
                    DepthQuality = row.observation.DepthQuality,
                    DataSourceId = row.observation.DataSourceId,
                })
                .ToListAsync(cancellationToken);

            // Five rows. Fetched once and joined in memory rather than as a correlated
            // subquery evaluated per candidate.
            var agencies = await context.DataSources.AsNoTracking()
                .Select(source => new { source.Id, source.Agency })
                .ToDictionaryAsync(source => source.Id, source => source.Agency, cancellationToken);

            // The place directory, on the same principle: one read, then arithmetic per match.
            // This is what closed the gap recorded on this slice — that matches carried
            // coordinates and no place name, because nothing could name them.
            var places = await NearestPlaceLocator.LoadAsync(context, cancellationToken);

            var comparableToReference = referenceMagnitude is { } referenceReading
                ? ComparableScalesWith(referenceReading.Type)
                : [];

            var excludedForAssignedDepth = 0;
            var excludedForIncomparableScale = 0;
            var admittedIncomparable = 0;
            var candidatesConsidered = 0;
            var matches = new List<SimilarEarthquake>();

            foreach (var row in nearby)
            {
                var depthUsable = row.DepthQuality == DepthQuality.Constrained;
                var scaleComparable = row.MagnitudeValue is not null
                    && Array.IndexOf(comparableToReference, row.MagnitudeScale) >= 0;

                if (!depthUsable)
                {
                    excludedForAssignedDepth++;
                }

                if (!scaleComparable)
                {
                    excludedForIncomparableScale++;
                }

                // The same predicates that previously ran as SQL filters. Applied here
                // because the rows are already in hand, and because the counts above need
                // the unfiltered set anyway.
                if (request.ExcludeOperatorAssignedDepths && !depthUsable)
                {
                    continue;
                }

                var candidateMagnitude = row.MagnitudeValue is { } candidateValue
                    ? new MagnitudeReading(candidateValue, row.MagnitudeScale)
                    : (MagnitudeReading?)null;

                if (request.RequireComparableMagnitudeScale)
                {
                    if (!scaleComparable)
                    {
                        continue;
                    }

                    // Within one family the delta is the arithmetic difference, so a reading
                    // outside the tolerance cannot score above zero on this component.
                    if (referenceMagnitude is { } magnitude
                        && Math.Abs(candidateMagnitude!.Value.Value - magnitude.Value)
                            > request.MaxMagnitudeDelta)
                    {
                        continue;
                    }
                }

                candidatesConsidered++;

                var distanceKm = GeoDistance.HaversineKm(
                    reference.Latitude,
                    reference.Longitude,
                    row.Latitude,
                    row.Longitude);

                var candidateDepth = new DepthReading(row.DepthKilometres, row.DepthQuality);

                var breakdown = SimilarityBreakdown.Compute(
                    distanceKm,
                    referenceMagnitude,
                    candidateMagnitude,
                    referenceDepth,
                    candidateDepth,
                    criteria);

                var comparable = breakdown.MagnitudeDelta is not null;

                if (!comparable)
                {
                    admittedIncomparable++;
                }

                matches.Add(new SimilarEarthquake(
                    row.EventId,
                    row.OccurredAt,
                    row.Latitude,
                    row.Longitude,
                    // Loaded once outside the loop: naming a match is a lookup against the
                    // gazetteer already in memory, not a query per row.
                    places.Describe(row.Latitude, row.Longitude)?.DescribeWithContainer(),
                    agencies.GetValueOrDefault(row.DataSourceId) ?? "Unattributed",
                    candidateMagnitude?.Display() ?? "No magnitude reported",
                    candidateDepth.Display(),
                    Math.Round(distanceKm, 1),
                    breakdown.MagnitudeDelta is { } delta ? Math.Round(delta, 2) : null,
                    breakdown.DepthDeltaKm is { } depthDelta ? Math.Round(depthDelta, 1) : null,
                    comparable,
                    Math.Round(breakdown.Score, 4),
                    breakdown.Explanations));
            }

            var ordered = matches
                .OrderByDescending(match => match.Score)
                // Distance breaks ties so the ordering is deterministic; without it, equal
                // scores would come back in whatever order the database happened to scan.
                .ThenBy(match => match.DistanceKm)
                .Take(request.Limit)
                .ToList();

            return Result<SimilarEarthquakesResponse>.Success(new SimilarEarthquakesResponse(
                reference.Id,
                referenceMagnitude?.Display() ?? "No magnitude reported",
                referenceDepth.Display(),
                nearby.Count,
                excludedForIncomparableScale,
                excludedForAssignedDepth,
                candidatesConsidered,
                admittedIncomparable,
                ordered));
        }

        /// <summary>Minimal projection: scalars only, so the query stays an index scan.</summary>
        private sealed class NearbyRow
        {
            public required Guid EventId { get; init; }

            public required DateTimeOffset OccurredAt { get; init; }

            public required double Latitude { get; init; }

            public required double Longitude { get; init; }

            public required double? MagnitudeValue { get; init; }

            public required MagnitudeType MagnitudeScale { get; init; }

            public required double? DepthKilometres { get; init; }

            public required DepthQuality DepthQuality { get; init; }

            public required Guid DataSourceId { get; init; }
        }

        /// <summary>
        /// Magnitude types comparable with the given one.
        /// </summary>
        /// <remarks>
        /// Enumerated in C# so comparability becomes an <c>IN (...)</c> predicate. The
        /// alternative — calling <c>IsComparableWith</c> per row — cannot be translated by
        /// EF Core and would force the whole candidate set into memory.
        /// </remarks>
        private static MagnitudeType[] ComparableScalesWith(MagnitudeType scale) =>
            Enum.GetValues<MagnitudeType>()
                .Where(candidate => candidate.IsComparableWith(scale))
                .ToArray();
    }
}
