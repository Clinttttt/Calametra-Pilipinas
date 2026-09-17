using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Events;
using Calametra.Domain.Seismology;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Earthquakes;

/// <summary>
/// The whole archive in the smallest form the map can draw.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <c>SearchEarthquakes</c> because the two have genuinely different
/// jobs. The search endpoint returns display-ready records — pre-formatted magnitude
/// strings, scale families, agency and dataset names — which is right for a list or a
/// panel and wasteful repeated 27,000 times for points on a canvas.
/// </para>
/// <para>
/// Measured against the live archive: the search endpoint costs about 451 bytes per
/// event, making the full archive 12 MB uncompressed and 2.4 MB over the wire. Nearly
/// all of that is repetition the map never reads — the agency name is one of five
/// strings repeated thousands of times, and the formatted display strings are only
/// needed on click.
/// </para>
/// <para>
/// Field names are deliberately terse. That is the wrong choice for an API humans
/// consume and the right one for an array of 27,000 rows, where key names are a
/// measurable share of the payload. The detail endpoint stays fully descriptive, and
/// this shape is documented here rather than left to be inferred.
/// </para>
/// </remarks>
public static class GetEarthquakeMapData
{
    public sealed record Query : IQuery<MapDataResponse>;

    /// <summary>Compact archive for rendering, plus the count the readout needs.</summary>
    public sealed record MapDataResponse(int Count, IReadOnlyList<MapPoint> Points);

    /// <summary>
    /// One event, reduced to what a circle layer needs.
    /// </summary>
    /// <param name="I">Event id, for fetching detail on click.</param>
    /// <param name="Y">Latitude, degrees.</param>
    /// <param name="X">Longitude, degrees.</param>
    /// <param name="M">Magnitude, or null when none was reported.</param>
    /// <param name="S">Magnitude scale, short form (e.g. <c>Mww</c>).</param>
    /// <param name="D">Depth in kilometres, or null.</param>
    /// <param name="Q">
    /// Depth quality: 1 measured, 2 agency-assigned, 0 unknown. A number rather than a
    /// name because it is read by a paint expression, and 43% of the archive carries a
    /// non-measured value — the flag has to travel with every point.
    /// </param>
    /// <param name="T">Origin time as Unix epoch milliseconds, for timeline filtering.</param>
    /// <param name="N">Whether more than one agency reported this event.</param>
    public sealed record MapPoint(
        Guid I,
        double Y,
        double X,
        double? M,
        string S,
        double? D,
        int Q,
        long T,
        bool N);

    internal sealed record MapRow(
        Guid Id,
        DateTimeOffset OccurredAt,
        double Latitude,
        double Longitude,
        double? Magnitude,
        MagnitudeType MagnitudeScale,
        double? DepthKilometres,
        DepthQuality DepthQuality,
        int ObservationCount);

    /// <summary>Shared compact projection for the national archive and authoritative contained subsets.</summary>
    internal static IQueryable<MapRow> Rows(
        IApplicationDbContext context,
        IQueryable<Guid>? eventIds = null)
    {
        if (eventIds is null)
        {
            return (
                from hazardEvent in context.HazardEvents.AsNoTracking()
                join observation in context.EarthquakeObservations.AsNoTracking()
                    on hazardEvent.PreferredObservationId equals observation.Id
                where hazardEvent.Type == HazardEventType.Earthquake
                orderby hazardEvent.CanonicalOccurredAt
                select new MapRow(
                    hazardEvent.Id,
                    hazardEvent.CanonicalOccurredAt,
                    observation.Latitude,
                    observation.Longitude,
                    observation.MagnitudeValue,
                    observation.MagnitudeScale,
                    observation.DepthKilometres,
                    observation.DepthQuality,
                    hazardEvent.Observations.Count));
        }

        return
            from eventId in eventIds
            join hazardEvent in context.HazardEvents.AsNoTracking()
                on eventId equals hazardEvent.Id
            join observation in context.EarthquakeObservations.AsNoTracking()
                on hazardEvent.PreferredObservationId equals observation.Id
            where hazardEvent.Type == HazardEventType.Earthquake
            orderby hazardEvent.CanonicalOccurredAt
            select new MapRow(
                hazardEvent.Id,
                hazardEvent.CanonicalOccurredAt,
                observation.Latitude,
                observation.Longitude,
                observation.MagnitudeValue,
                observation.MagnitudeScale,
                observation.DepthKilometres,
                observation.DepthQuality,
                hazardEvent.Observations.Count);
    }

    internal static IReadOnlyList<MapPoint> ToPoints(IEnumerable<MapRow> rows) =>
        rows.Select(row => new MapPoint(
                row.Id,
                Math.Round(row.Latitude, 5),
                Math.Round(row.Longitude, 5),
                row.Magnitude,
                row.MagnitudeScale.Label(),
                row.DepthKilometres is { } depth ? Math.Round(depth, 1) : null,
                (int)row.DepthQuality,
                row.OccurredAt.ToUnixTimeMilliseconds(),
                row.ObservationCount > 1))
            .ToList();

    internal sealed class Handler(IApplicationDbContext context)
        : IQueryHandler<Query, MapDataResponse>
    {
        public async Task<Result<MapDataResponse>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            // Projected in the database so the wide entity rows are never materialised.
            var rows = await Rows(context).ToListAsync(cancellationToken);
            var points = ToPoints(rows);

            return Result<MapDataResponse>.Success(new MapDataResponse(points.Count, points));
        }
    }
}
