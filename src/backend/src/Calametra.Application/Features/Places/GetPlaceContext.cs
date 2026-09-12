using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Places.Shared;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Events;
using Calametra.Domain.Geospatial;
using Calametra.Domain.Places;
using Calametra.Domain.Seismology;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Places;

/// <summary>
/// What the archive holds around one place.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no single "largest nearby earthquake", and refusing to invent one is the point
/// of this slice.</b> Ranking events by reported magnitude across scales would compare a
/// body-wave value against a moment magnitude: <c>mb</c> saturates near M6, so the strongest
/// event by raw number is frequently not the strongest event. The strongest reading is
/// therefore returned once per scale family, exactly as cyclone peak intensity is returned
/// once per wind averaging period. Both are the same refusal applied to different hazards.
/// </para>
/// <para>
/// <b>Distance is measured from a representative point, not from the place's boundary.</b> The
/// gazetteer publishes no Philippine boundaries, so <c>Place.Boundary</c> is null for every
/// row. For a large municipality the difference is tens of kilometres, which is why the
/// response says so rather than leaving a reader to assume the circle follows the town limits.
/// </para>
/// <para>
/// <b>One spatial pass, for the reason recorded on <c>GetSimilarEarthquakes</c>.</b> PostGIS
/// performs a single indexed <c>ST_DWithin</c> returning scalar columns; every count, distance
/// and ranking is derived from that result set in memory. Separate aggregate queries repeated
/// the spatial scan and cost seconds, and PostGIS misestimates <c>ST_DWithin</c> selectivity
/// badly enough that the planner abandons the index once a large fraction of the table
/// qualifies.
/// </para>
/// </remarks>
public static class GetPlaceContext
{
    /// <summary>
    /// Magnitude above which counts may be compared between eras.
    /// </summary>
    /// <remarks>
    /// Measured, not chosen for neatness. Events per decade in the archive rise from 21 in the
    /// 1900s to 5,974 in the 2020s — a 285-fold increase that is instrumentation — while the
    /// M6.0+ rate is flat at roughly five to six per year across the same 125 years. So a raw
    /// count for a place says as much about seismometer coverage as about the ground, and the
    /// M6.0+ subset is the only one a reader may compare against another era.
    /// </remarks>
    private const double ComparableMagnitudeFloor = 6.0d;

    public sealed record Query : IQuery<PlaceContextResponse>
    {
        /// <summary>
        /// The place's Philippine Standard Geographic Code.
        /// </summary>
        /// <remarks>
        /// Keyed on the code rather than the internal id, per guardrail 8: internal ids are
        /// minted at insert and change when the directory is re-imported, so a link a reader
        /// shares or a citation in a thesis must not carry one.
        /// </remarks>
        public required string PsgcCode { get; init; }

        public double RadiusKm { get; init; } = 25d;
    }

    /// <param name="EventsWithinRadius">
    /// Every earthquake in the archive within the radius, at any magnitude.
    /// </param>
    /// <param name="EventsAtComparableMagnitude">
    /// Of those, how many are M6.0 or above — the subset that may be compared with another era.
    /// </param>
    /// <param name="EventsWithAssignedDepth">
    /// Of those, how many carry a depth an agency assigned rather than measured. Reported
    /// because 43% of the archive's depths were never measured, and a depth distribution around
    /// a town would otherwise read as observation.
    /// </param>
    /// <param name="StrongestByScaleFamily">
    /// The strongest reading in each scale family present. Never reduced to one figure.
    /// </param>
    /// <param name="NearestFaults">
    /// The nearest mapped fault traces, whether or not they fall inside the radius — a fault
    /// 60 km from a town is worth knowing about when the radius is 25 km.
    /// </param>
    /// <param name="Notes">
    /// What a reader has to know to avoid drawing a wrong conclusion from the figures above.
    /// Generated from the data actually present, so it cannot drift from the response.
    /// </param>
    public sealed record PlaceContextResponse(
        string? PsgcCode,
        string Name,
        string Kind,
        string? ContainedBy,
        string? Region,
        double Latitude,
        double Longitude,
        double RadiusKm,
        int EventsWithinRadius,
        int EventsAtComparableMagnitude,
        int EventsWithAssignedDepth,
        DateTimeOffset? EarliestEvent,
        DateTimeOffset? LatestEvent,
        IReadOnlyList<StrongestReading> StrongestByScaleFamily,
        NearbyEarthquake? MostRecentEvent,
        IReadOnlyList<NearbyFault> NearestFaults,
        IReadOnlyList<string> Notes);

    /// <param name="ScaleFamily">The family these readings belong to, e.g. <c>Moment</c>.</param>
    /// <param name="ReadingsInFamily">How many events within the radius report on this family.</param>
    public sealed record StrongestReading(
        string ScaleFamily,
        int ReadingsInFamily,
        NearbyEarthquake Event);

    public sealed record NearbyEarthquake(
        Guid EventId,
        DateTimeOffset OccurredAt,
        double Latitude,
        double Longitude,
        string MagnitudeDisplay,
        string DepthDisplay,
        string Agency,
        double DistanceKm);

    /// <param name="WithinRadius">Whether this trace falls inside the selected radius.</param>
    public sealed record NearbyFault(
        string Name,
        string? Classification,
        double DistanceKm,
        bool WithinRadius,
        string Agency,
        string Attribution);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.PsgcCode).NotEmpty().MaximumLength(16);

            // Capped at 300 km for the reason measured on GetSimilarEarthquakes: past roughly
            // 400 km the planner abandons the GiST bitmap scan and geography ST_DWithin runs
            // spheroid arithmetic over the whole table, taking 15-23 s instead of 0.13 s. The
            // cap sits below the observed edge so the cliff cannot move inside the limit if the
            // catalogue grows. The interface offers 10/25/50/100 km.
            RuleFor(query => query.RadiusKm).InclusiveBetween(1d, 300d);
        }
    }

    internal sealed class Handler(IApplicationDbContext context)
        : IQueryHandler<Query, PlaceContextResponse>
    {
        /// <summary>How many fault traces to name. Three is enough to show that proximity is plural.</summary>
        private const int FaultsToName = 3;

        public async Task<Result<PlaceContextResponse>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var code = request.PsgcCode.Trim();

            var place = await context.Places.AsNoTracking()
                .Where(candidate => candidate.PsgcCode == code)
                .Select(candidate => new
                {
                    candidate.PsgcCode,
                    candidate.Name,
                    candidate.Kind,
                    candidate.ParentPlaceId,
                    candidate.Latitude,
                    candidate.Longitude,
                    // The geometry is still needed: it is the parameter PostGIS filters the
                    // archive against, and only the coordinate read is materialised.
                    candidate.Centroid,
                    // Which source supplied the point, so the note can credit it. Null when the
                    // gazetteer's own coordinate is still in place.
                    candidate.CoordinateDataSourceId,
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (place is null)
            {
                return Result<PlaceContextResponse>.Failure(PlaceErrors.NotFound);
            }

            var latitude = place.Latitude;
            var longitude = place.Longitude;

            var radiusMetres = request.RadiusKm * 1_000d;

            var nearby = await context.HazardEvents.AsNoTracking()
                .Join(
                    context.EarthquakeObservations.AsNoTracking(),
                    hazardEvent => hazardEvent.PreferredObservationId,
                    observation => observation.Id,
                    (hazardEvent, observation) => new { hazardEvent, observation })
                .Where(row => row.hazardEvent.Type == HazardEventType.Earthquake
                    && row.observation.Epicenter.IsWithinDistance(place.Centroid, radiusMetres))
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

            var agencies = await context.DataSources.AsNoTracking()
                .Select(source => new { source.Id, source.Agency, source.Attribution })
                .ToListAsync(cancellationToken);

            var agencyNames = agencies.ToDictionary(source => source.Id, source => source.Agency);

            // The coordinate need not come from the source that named the place, so its attribution
            // is looked up separately. Null when the place still carries the gazetteer's own point —
            // the note then says so rather than crediting a source that supplied nothing.
            var coordinateAttribution = place.CoordinateDataSourceId is null
                ? null
                : agencies
                    .FirstOrDefault(source => source.Id == place.CoordinateDataSourceId)
                    ?.Attribution;

            var faults = await LoadNearestFaultsAsync(
                place.Centroid,
                radiusMetres,
                agencies.ToDictionary(source => source.Id, source => (source.Agency, source.Attribution)),
                cancellationToken);

            var chain = await PlaceHierarchyLoader.LoadAsync(context, [place.ParentPlaceId], cancellationToken);
            var (containedBy, region) = PlaceHierarchyLoader.Describe(place.ParentPlaceId, chain);

            var described = nearby
                .Select(row => Describe(row, latitude, longitude, agencyNames))
                .ToList();

            var assignedDepths = nearby.Count(row => row.DepthQuality != DepthQuality.Constrained);

            var comparable = nearby.Count(row => row.MagnitudeValue >= ComparableMagnitudeFloor);

            var strongest = nearby
                .Where(row => row.MagnitudeValue is not null
                    && row.MagnitudeScale.Family() != MagnitudeScaleFamily.Unknown)
                .GroupBy(row => row.MagnitudeScale.Family())
                .Select(family => new StrongestReading(
                    family.Key.ToString(),
                    family.Count(),
                    Describe(
                        family.MaxBy(row => row.MagnitudeValue!.Value)!,
                        latitude,
                        longitude,
                        agencyNames)))
                // Ordered by how many readings each family holds, so the family that actually
                // characterises this area leads. Not by magnitude: that would rank the families
                // against each other, which is the comparison this slice refuses to make.
                .OrderByDescending(family => family.ReadingsInFamily)
                .ThenBy(family => family.ScaleFamily, StringComparer.Ordinal)
                .ToList();

            var response = new PlaceContextResponse(
                place.PsgcCode,
                place.Name,
                place.Kind.ToString(),
                containedBy,
                region,
                latitude,
                longitude,
                request.RadiusKm,
                nearby.Count,
                comparable,
                assignedDepths,
                described.Count == 0 ? null : described.Min(row => row.OccurredAt),
                described.Count == 0 ? null : described.Max(row => row.OccurredAt),
                strongest,
                described.Count == 0 ? null : described.MaxBy(row => row.OccurredAt),
                faults,
                BuildNotes(
                    place.Name,
                    request.RadiusKm,
                    nearby.Count,
                    comparable,
                    assignedDepths,
                    faults,
                    coordinateAttribution));

            return Result<PlaceContextResponse>.Success(response);
        }

        private static NearbyEarthquake Describe(
            NearbyRow row,
            double placeLatitude,
            double placeLongitude,
            IReadOnlyDictionary<Guid, string> agencies)
        {
            var magnitude = row.MagnitudeValue is { } value
                ? new MagnitudeReading(value, row.MagnitudeScale)
                : (MagnitudeReading?)null;

            var depth = new DepthReading(row.DepthKilometres, row.DepthQuality);

            // Haversine in the domain rather than ST_Distance in the query, for the reason
            // recorded on GeoDistance: projecting a distance column turns the single indexed
            // pass into a per-row spheroid calculation.
            var distanceKm = GeoDistance.HaversineKm(
                placeLatitude,
                placeLongitude,
                row.Latitude,
                row.Longitude);

            return new NearbyEarthquake(
                row.EventId,
                row.OccurredAt,
                row.Latitude,
                row.Longitude,
                magnitude?.Display() ?? "No magnitude reported",
                depth.Display(),
                agencies.GetValueOrDefault(row.DataSourceId) ?? "Unattributed",
                Math.Round(distanceKm, 1));
        }

        /// <summary>
        /// The nearest stored fault traces, with the distance PostGIS measures on the spheroid.
        /// </summary>
        /// <remarks>
        /// Not filtered by the radius. A fault is a line, and the nearest one to a town is worth
        /// stating whatever radius the reader has selected — suppressing it below 10 km would
        /// imply there is none. Only 155 traces are stored, so ordering by distance is a
        /// sequential scan over a table small enough that it does not matter.
        /// </remarks>
        private async Task<List<NearbyFault>> LoadNearestFaultsAsync(
            NetTopologySuite.Geometries.Point centroid,
            double radiusMetres,
            Dictionary<Guid, (string Agency, string Attribution)> sources,
            CancellationToken cancellationToken)
        {
            var traces = await context.HazardFeatures.AsNoTracking()
                .Select(feature => new
                {
                    feature.Name,
                    feature.Classification,
                    feature.DataSourceId,
                    Metres = feature.Geometry.Distance(centroid),
                })
                .OrderBy(trace => trace.Metres)
                .Take(FaultsToName)
                .ToListAsync(cancellationToken);

            return traces
                .Select(trace =>
                {
                    var attributed = sources.TryGetValue(trace.DataSourceId, out var source);

                    return new NearbyFault(
                        // An unnamed trace is stated as unnamed rather than dropped: the
                        // catalogue genuinely holds segments without a published name, and
                        // hiding them would understate how many traces are near a place.
                        trace.Name ?? "Unnamed trace",
                        trace.Classification,
                        Math.Round(trace.Metres / 1_000d, 1),
                        trace.Metres <= radiusMetres,
                        attributed ? source.Agency : "Unattributed",
                        attributed ? source.Attribution : string.Empty);
                })
                .ToList();
        }

        /// <summary>
        /// The caveats that apply to the figures returned, generated from those figures.
        /// </summary>
        /// <remarks>
        /// Composed here rather than in the client for the same reason the cyclone agreement note
        /// is: these sentences are claims about the data, and the layer holding the data is the
        /// one that can make them without drifting.
        /// </remarks>
        private static List<string> BuildNotes(
            string name,
            double radiusKm,
            int eventCount,
            int comparableCount,
            int assignedDepths,
            List<NearbyFault> faults,
            string? coordinateAttribution)
        {
            var notes = new List<string>
            {
                // Stated as the town centre now rather than a bare "representative point", because
                // that is what it is: 1,520 of 1,647 cities and municipalities were moved onto their
                // mapped poblacion, a mean correction of 5.3 km and up to 31.6 km. The remaining
                // caveat is the one that still holds — a point is not a boundary.
                coordinateAttribution is null
                    ? $"Distances are measured from a representative point for {name}, not from its "
                        + "boundary. No boundary is stored for Philippine local government units, so "
                        + "for a large municipality the edge of the built-up area may be tens of "
                        + "kilometres from this point."
                    : $"Distances are measured from the mapped town centre of {name}, not from its "
                        + "boundary. No boundary is stored for Philippine local government units, so "
                        + "for a large municipality its edge may be tens of kilometres from this "
                        + $"point. {coordinateAttribution}",
            };

            if (eventCount > 0)
            {
                notes.Add(
                    $"{eventCount} earthquakes within {radiusKm:0.#} km is not a count of "
                    + "earthquakes that happened. The catalogue holds effectively nothing below "
                    + "magnitude 4.0 anywhere in the archipelago, and detection improved 285-fold "
                    + $"across the last century. The {comparableCount} at magnitude 6.0 and above "
                    + "are the only figure comparable with another era or another place.");
            }

            if (assignedDepths > 0)
            {
                notes.Add(
                    $"{assignedDepths} of these events carry a depth the agency assigned rather "
                    + "than measured — a fixed 33, 10, 35 or 15 km standing in for a value that "
                    + "could not be resolved. Those depths mark that an earthquake occurred, not "
                    + "how deep it was.");
            }

            if (faults.Count > 0)
            {
                notes.Add(
                    "A mapped fault near this place is not established as the source of any of "
                    + "these earthquakes. Attribution is a determination only the responsible "
                    + "agency can make, and the fault that ruptures is sometimes one no map "
                    + "showed — the 2013 Bohol earthquake came from a fault that was in no "
                    + "database until afterwards.");
            }

            return notes;
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
    }
}
