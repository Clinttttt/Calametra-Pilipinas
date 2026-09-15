using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Geospatial;
using Calametra.Domain.Meteorology;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Cyclones;

/// <summary>
/// The portions of storm tracks that passed near a point, drawn rather than counted.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a clipped track and not a whole one.</b> A place with 91 storms within 100 km has, in whole
/// tracks, some fifteen thousand fixes spanning the western North Pacific from Micronesia to the
/// Chinese coast. Drawn in full they cover the map and answer a different question — where storms in
/// this basin go — while the question asked here is how they passed <em>this place</em>. So each track
/// is cut to the fixes near the point, which turns the same data into a picture of approach: the
/// directional grain of the tracks, where they crossed the coast, and how strong they were as they
/// went by.
/// </para>
/// <para>
/// <b>The margin is deliberate.</b> Segments are returned out to a wider radius than the one used to
/// select storms, because a line cut exactly at the ring looks like a storm that began and ended
/// there. The margin lets each track visibly enter and leave, which is what makes the direction of
/// travel readable.
/// </para>
/// <para>
/// <b>One agency per storm, and it is a choice, not an authority.</b> Agencies disagree on where the
/// centre was, so drawing all of them would put two or three parallel lines per storm on the map and
/// treble the ink for no gain in this view. The agency with the most fixes near the point is used, on
/// the stated ground that it has the finest spatial resolution <em>here</em> — not that it is right.
/// The per-agency comparison remains at <c>/api/cyclones/{eventId}</c>, which is where a reader who
/// wants to see the disagreement should be sent.
/// </para>
/// <para>
/// <b>Peak wind is per track and named as local.</b> The figure returned is the strongest reading this
/// agency recorded <em>within the returned segment</em>, and it carries the averaging period that
/// produced it. It is not the storm's peak intensity, and calling it that would overstate it by a wide
/// margin for a storm that passed the place early and intensified later.
/// </para>
/// </remarks>
public static class GetCycloneTracksNearby
{
    public sealed record Query : IQuery<NearbyTracksResponse>
    {
        public required double Latitude { get; init; }

        public required double Longitude { get; init; }

        /// <summary>Radius in kilometres that decides which storms count as nearby.</summary>
        public double RadiusKm { get; init; } = 100d;

        /// <summary>
        /// How many tracks to return, strongest local peak first.
        /// </summary>
        /// <remarks>
        /// A cap rather than paging: this feeds a figure, and a figure showing half the storms in
        /// arbitrary order would be worse than one showing the strongest and saying so. The response
        /// reports the full <c>StormCount</c> beside the number returned, so the caller can state the
        /// truncation instead of hiding it.
        /// </remarks>
        public int Limit { get; init; } = 150;
    }

    /// <param name="Tracks">Strongest local peak first, so a truncated set keeps the notable storms.</param>
    /// <param name="StormCount">
    /// Storms with at least one fix inside <paramref name="RadiusKm"/>. Matches the count the place
    /// context reports for the same point and radius, and exceeds <c>Tracks.Count</c> when the limit
    /// bit.
    /// </param>
    /// <param name="FixCount">Positions actually returned, across every track.</param>
    /// <param name="DrawnRadiusKm">
    /// The wider radius the returned segments extend to. Stated so a client can tell a track that left
    /// the drawn area from one whose record ends.
    /// </param>
    /// <param name="ReliableFromSeason">
    /// The season from which JTWC rates its own best-track record as high quality. Carried here as
    /// well as on the decade series, because a map of tracks from the 1940s is a map of weaker claims.
    /// </param>
    public sealed record NearbyTracksResponse(
        IReadOnlyList<NearbyTrack> Tracks,
        int StormCount,
        int FixCount,
        double RadiusKm,
        double DrawnRadiusKm,
        int ReliableFromSeason);

    /// <param name="Agency">Whose track this is. Never omitted: an unattributed path is not drawn.</param>
    /// <param name="AveragingPeriod">
    /// The interval this agency averages sustained wind over. A property of its method, so it is stated
    /// once for the track rather than repeated on every fix.
    /// </param>
    /// <param name="PeakKnotsNearby">
    /// The strongest wind this agency recorded within the returned segment. Local to the segment, not
    /// the storm's peak.
    /// </param>
    /// <param name="ClosestApproachKm">
    /// How near the centre came, at best-track resolution. The fixes are three- or six-hourly, so the
    /// true closest approach fell between two of them and this is an upper bound.
    /// </param>
    /// <param name="LandfallNearby">
    /// Whether a fix <em>inside the selection radius</em> is flagged as a landfall, so this agrees with
    /// the landfall count the place context reports for the same point rather than with the wider area
    /// the segment is drawn over.
    /// </param>
    public sealed record NearbyTrack(
        Guid EventId,
        string? Name,
        string? LocalName,
        int Season,
        string Agency,
        string AveragingPeriod,
        double? PeakKnotsNearby,
        double ClosestApproachKm,
        bool LandfallNearby,
        IReadOnlyList<NearbyFix> Fixes);

    /// <param name="WindKnots">Null when this agency reported a position but no wind.</param>
    public sealed record NearbyFix(
        DateTimeOffset CapturedAt,
        double Latitude,
        double Longitude,
        double? WindKnots,
        bool IsLandfall);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.Latitude).InclusiveBetween(-90d, 90d);
            RuleFor(query => query.Longitude).InclusiveBetween(-180d, 180d);

            // The same ceiling as the place context, and for the same reason: beyond 300 km a
            // "nearby" storm is most of the basin, and the figure stops meaning anything.
            RuleFor(query => query.RadiusKm).InclusiveBetween(1d, 300d);
            RuleFor(query => query.Limit).InclusiveBetween(1, 400);
        }
    }

    internal sealed class Handler(IApplicationDbContext context)
        : IQueryHandler<Query, NearbyTracksResponse>
    {
        /// <summary>
        /// How much wider than the selection radius the drawn segments reach.
        /// </summary>
        /// <remarks>
        /// 1.6 rather than a fixed number of kilometres, so the margin scales with the view: at 50 km
        /// a fixed 60 km margin would dominate the figure, and at 200 km it would be invisible.
        /// Chosen against best-track spacing — a storm moving at 15 kt covers about 80 km between
        /// six-hourly fixes, so at 100 km this margin adds roughly one fix at each end, which is
        /// exactly enough to show a direction and not enough to clutter.
        /// </remarks>
        private const double DrawnRadiusFactor = 1.6d;

        /// <summary>See <see cref="GetCycloneDecades"/>: JTWC's own assessment of its best track.</summary>
        private const int ReliableFromSeason = 1985;

        public async Task<Result<NearbyTracksResponse>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var centre = Wgs84.Point(request.Latitude, request.Longitude);
            var drawnRadiusKm = request.RadiusKm * DrawnRadiusFactor;
            var drawnRadiusMetres = drawnRadiusKm * 1_000d;

            // One spatial pass, at the wider radius. `IsWithinDistance` translates to `ST_DWithin`,
            // which answers from the GiST index on the position column; the `Distance(...) <= radius`
            // form computes a distance for every one of 256,490 rows and was measured at 6.9 s against
            // 140 ms for this. The per-row distance below is then computed only for the narrowed set,
            // because it is needed as a value rather than as a predicate.
            var rows = await context.CycloneTrackPoints.AsNoTracking()
                .Where(point => point.Position.IsWithinDistance(centre, drawnRadiusMetres))
                .Join(
                    context.DataSources.AsNoTracking(),
                    point => point.DataSourceId,
                    source => source.Id,
                    (point, source) => new FixRow
                    {
                        HazardEventId = point.HazardEventId,
                        Agency = source.Agency,
                        CapturedAt = point.CapturedAt,
                        Latitude = point.Latitude,
                        Longitude = point.Longitude,
                        WindKnots = point.WindSpeedKnots,
                        Period = point.WindAveragingPeriod,
                        IsLandfall = point.IsLandfall,
                        DistanceMetres = point.Position.Distance(centre),
                    })
                .ToListAsync(cancellationToken);

            if (rows.Count == 0)
            {
                return Result<NearbyTracksResponse>.Success(new NearbyTracksResponse(
                    [],
                    0,
                    0,
                    request.RadiusKm,
                    drawnRadiusKm,
                    ReliableFromSeason));
            }

            var radiusMetres = request.RadiusKm * 1_000d;

            // Storms qualify on the INNER radius. The outer radius only decides how much of a
            // qualifying storm's path is drawn, so a track that merely clipped the margin is not
            // counted as nearby and does not appear at all.
            var qualifying = rows
                .Where(row => row.DistanceMetres <= radiusMetres)
                .Select(row => row.HazardEventId)
                .Distinct()
                .ToHashSet();

            if (qualifying.Count == 0)
            {
                return Result<NearbyTracksResponse>.Success(new NearbyTracksResponse(
                    [],
                    0,
                    0,
                    request.RadiusKm,
                    drawnRadiusKm,
                    ReliableFromSeason));
            }

            var storms = await context.HazardEvents.AsNoTracking()
                .Where(hazardEvent => qualifying.Contains(hazardEvent.Id))
                .Select(hazardEvent => new
                {
                    hazardEvent.Id,
                    hazardEvent.Name,
                    hazardEvent.LocalName,
                    hazardEvent.CanonicalOccurredAt,
                })
                .ToListAsync(cancellationToken);

            var identities = storms.ToDictionary(
                storm => storm.Id,
                storm => new StormIdentity(
                    storm.Name,
                    storm.LocalName,
                    storm.CanonicalOccurredAt.Year));

            // Landfall is a property of the STORM'S RECORD, not of the drawn path. One agency per storm
            // is drawn, and the chosen agency may not be the one that flagged the crossing: measured
            // against Surigao City at 100 km, judging it on the drawn agency alone reported 82
            // landfalling storms where the place context said 90. So it is taken across every agency's
            // fixes inside the selection radius, which is exactly what the count beside it means.
            var landfalling = rows
                .Where(row => row.IsLandfall && row.DistanceMetres <= radiusMetres)
                .Select(row => row.HazardEventId)
                .Distinct()
                .ToHashSet();

            var tracks = rows
                .Where(row => qualifying.Contains(row.HazardEventId))
                .GroupBy(row => (row.HazardEventId, row.Agency))
                .Select(BuildTrack)
                // One agency per storm: the finest spatial resolution near this point, tie-broken on
                // agency name so the choice is stable between requests rather than dependent on the
                // order the database happened to return rows in.
                .GroupBy(candidate => candidate.EventId)
                .Select(group => group
                    .OrderByDescending(candidate => candidate.Fixes.Count)
                    .ThenBy(candidate => candidate.Agency, StringComparer.Ordinal)
                    .First())
                // Strongest local peak first, so truncation keeps the storms a reader would ask about.
                // A storm with no wind reading sorts last rather than first: an unreported wind is not
                // a weak one, but it is not a strong one either.
                .OrderByDescending(candidate => candidate.PeakKnotsNearby ?? -1d)
                .ThenBy(candidate => candidate.ClosestApproachKm)
                .Take(request.Limit)
                .Select(candidate => Describe(candidate, identities, landfalling))
                .ToList();

            return Result<NearbyTracksResponse>.Success(new NearbyTracksResponse(
                tracks,
                qualifying.Count,
                tracks.Sum(track => track.Fixes.Count),
                request.RadiusKm,
                drawnRadiusKm,
                ReliableFromSeason));
        }

        /// <summary>
        /// Assembles one agency's segment of one storm.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The averaging period is taken from the fixes that actually carry a wind reading. A
        /// position-only fix records <c>Unknown</c>, and letting that win would erase the agency's
        /// real convention — the same rule <c>CycloneTrackLoader</c> follows, for the same reason.
        /// </para>
        /// </remarks>
        private static Candidate BuildTrack(IGrouping<(Guid EventId, string Agency), FixRow> group)
        {
            var ordered = group.OrderBy(row => row.CapturedAt).ToList();

            var winds = ordered
                .Where(row => row.WindKnots is not null)
                .Select(row => row.WindKnots!.Value)
                .ToList();

            var period = ordered
                .Where(row => row.Period != WindAveragingPeriod.Unknown)
                .Select(row => row.Period)
                .DefaultIfEmpty(WindAveragingPeriod.Unknown)
                .First();

            return new Candidate
            {
                EventId = group.Key.EventId,
                Agency = group.Key.Agency,
                Period = period,
                PeakKnotsNearby = winds.Count == 0 ? null : winds.Max(),
                ClosestApproachKm = Math.Round(ordered.Min(row => row.DistanceMetres) / 1_000d, 1),
                Fixes = ordered,
            };
        }

        private static NearbyTrack Describe(
            Candidate candidate,
            Dictionary<Guid, StormIdentity> identities,
            HashSet<Guid> landfalling)
        {
            var identity = identities[candidate.EventId];

            return new NearbyTrack(
                candidate.EventId,
                identity.Name,
                identity.LocalName,
                identity.Season,
                candidate.Agency,
                candidate.Period.Label(),
                candidate.PeakKnotsNearby,
                candidate.ClosestApproachKm,
                landfalling.Contains(candidate.EventId),
                candidate.Fixes.ConvertAll(fix => new NearbyFix(
                    fix.CapturedAt,
                    fix.Latitude,
                    fix.Longitude,
                    fix.WindKnots,
                    fix.IsLandfall)));
        }

        /// <summary>How the storm is named and when it occurred, keyed by event.</summary>
        private sealed record StormIdentity(string? Name, string? LocalName, int Season);

        private sealed record FixRow
        {
            public required Guid HazardEventId { get; init; }

            public required string Agency { get; init; }

            public required DateTimeOffset CapturedAt { get; init; }

            public required double Latitude { get; init; }

            public required double Longitude { get; init; }

            public required double? WindKnots { get; init; }

            public required WindAveragingPeriod Period { get; init; }

            public required bool IsLandfall { get; init; }

            /// <summary>Metres from the requested centre, as PostGIS measured it on the spheroid.</summary>
            public required double DistanceMetres { get; init; }
        }

        private sealed record Candidate
        {
            public required Guid EventId { get; init; }

            public required string Agency { get; init; }

            public required WindAveragingPeriod Period { get; init; }

            public required double? PeakKnotsNearby { get; init; }

            public required double ClosestApproachKm { get; init; }

            public required List<FixRow> Fixes { get; init; }
        }
    }
}
