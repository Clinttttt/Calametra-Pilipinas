using Calametra.Application.Abstractions.Data;
using Calametra.Domain.Events;
using Calametra.Domain.Meteorology;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Cyclones.Shared;

/// <summary>
/// Loads one storm's per-agency tracks.
/// </summary>
/// <remarks>
/// Extracted so <c>GetCycloneTrack</c> and <c>GetCycloneByExternalId</c> share one
/// implementation, following the precedent set by <c>EarthquakeDetailLoader</c> and for the same
/// reason: the parts that must not be duplicated are the averaging-period rule and the agreement
/// note. Both encode the platform's central claim in the cyclone domain — that a peak wind speed
/// is meaningless without the interval it was averaged over — and two copies would eventually
/// disagree about when agencies disagree.
/// </remarks>
internal static class CycloneTrackLoader
{
    /// <summary>
    /// Loads one storm, or null when no cyclone with that id has any stored fix.
    /// </summary>
    public static async Task<GetCycloneTrack.CycloneTrackResponse?> Load(
        IApplicationDbContext context,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var storm = await context.HazardEvents.AsNoTracking()
            .Where(hazardEvent => hazardEvent.Id == eventId
                && hazardEvent.Type == HazardEventType.TropicalCyclone)
            .Select(hazardEvent => new
            {
                hazardEvent.Id,
                hazardEvent.Name,
                hazardEvent.LocalName,
                hazardEvent.CanonicalOccurredAt,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (storm is null)
        {
            return null;
        }

        var rows = await context.CycloneTrackPoints.AsNoTracking()
            .Where(trackPoint => trackPoint.HazardEventId == eventId)
            .Join(
                context.DataSources.AsNoTracking(),
                trackPoint => trackPoint.DataSourceId,
                dataSource => dataSource.Id,
                (trackPoint, dataSource) => new
                {
                    trackPoint.CapturedAt,
                    trackPoint.Latitude,
                    trackPoint.Longitude,
                    trackPoint.WindSpeedKnots,
                    trackPoint.WindAveragingPeriod,
                    trackPoint.MinimumPressureMillibars,
                    trackPoint.Classification,
                    trackPoint.DistanceToLandKm,
                    trackPoint.IsLandfall,
                    trackPoint.RadiusOfMaximumWindNm,
                    trackPoint.RadiusOutermostIsobarNm,
                    trackPoint.WindFieldGeometry,
                    trackPoint.GaleThresholdKnots,
                    trackPoint.GaleRadiusNorthEastNm,
                    trackPoint.GaleRadiusSouthEastNm,
                    trackPoint.GaleRadiusSouthWestNm,
                    trackPoint.GaleRadiusNorthWestNm,
                    trackPoint.GaleRadiusLongAxisNm,
                    trackPoint.GaleRadiusShortAxisNm,
                    trackPoint.GaleRadiusBearingDegrees,
                    trackPoint.StormRadiusNorthEastNm,
                    trackPoint.StormRadiusSouthEastNm,
                    trackPoint.StormRadiusSouthWestNm,
                    trackPoint.StormRadiusNorthWestNm,
                    trackPoint.HurricaneRadiusNorthEastNm,
                    trackPoint.HurricaneRadiusSouthEastNm,
                    trackPoint.HurricaneRadiusSouthWestNm,
                    trackPoint.HurricaneRadiusNorthWestNm,
                    dataSource.Agency,
                    dataSource.Slug,
                })
            .OrderBy(row => row.CapturedAt)
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return null;
        }

        var tracks = rows
            .GroupBy(row => new { row.Agency, row.Slug })
            .Select(group =>
            {
                var winds = group
                    .Where(row => row.WindSpeedKnots is not null)
                    .Select(row => row.WindSpeedKnots!.Value)
                    .ToList();

                var pressures = group
                    .Where(row => row.MinimumPressureMillibars is not null)
                    .Select(row => row.MinimumPressureMillibars!.Value)
                    .ToList();

                // The period is taken from the fixes that actually carry a wind reading.
                // A pressure-only fix records Unknown, and letting that win would erase the
                // agency's real convention.
                var period = group
                    .Where(row => row.WindAveragingPeriod != WindAveragingPeriod.Unknown)
                    .Select(row => row.WindAveragingPeriod)
                    .DefaultIfEmpty(WindAveragingPeriod.Unknown)
                    .First();

                return new GetCycloneTrack.AgencyTrackResponse(
                    group.Key.Agency,
                    group.Key.Slug,
                    period.Label(),
                    winds.Count == 0 ? null : winds.Max(),
                    pressures.Count == 0 ? null : pressures.Min(),
                    group.Select(row => new GetCycloneTrack.CycloneFixResponse(
                            row.CapturedAt,
                            row.Latitude,
                            row.Longitude,
                            row.WindSpeedKnots,
                            row.MinimumPressureMillibars,
                            string.IsNullOrWhiteSpace(row.Classification) ? null : row.Classification,
                            row.DistanceToLandKm,
                            row.IsLandfall,
                            row.RadiusOfMaximumWindNm,
                            row.RadiusOutermostIsobarNm,
                            row.WindFieldGeometry.ToString(),
                            row.GaleThresholdKnots,
                            row.GaleRadiusNorthEastNm,
                            row.GaleRadiusSouthEastNm,
                            row.GaleRadiusSouthWestNm,
                            row.GaleRadiusNorthWestNm,
                            row.GaleRadiusLongAxisNm,
                            row.GaleRadiusShortAxisNm,
                            row.GaleRadiusBearingDegrees,
                            // Computed by the domain value object rather than here, so the
                            // rule about needing all four quadrants lives in one place.
                            Round(WindField
                                .FromQuadrants(
                                    row.GaleThresholdKnots,
                                    row.GaleRadiusNorthEastNm,
                                    row.GaleRadiusSouthEastNm,
                                    row.GaleRadiusSouthWestNm,
                                    row.GaleRadiusNorthWestNm)
                                .AsymmetryRatio),
                            row.StormRadiusNorthEastNm,
                            row.StormRadiusSouthEastNm,
                            row.StormRadiusSouthWestNm,
                            row.StormRadiusNorthWestNm,
                            row.HurricaneRadiusNorthEastNm,
                            row.HurricaneRadiusSouthEastNm,
                            row.HurricaneRadiusSouthWestNm,
                            row.HurricaneRadiusNorthWestNm))
                        .ToList());
            })
            .OrderByDescending(track => track.Fixes.Count)
            .ToList();

        return new GetCycloneTrack.CycloneTrackResponse(
            storm.Id,
            storm.Name,
            storm.LocalName,
            storm.CanonicalOccurredAt.Year,
            rows.Min(row => row.CapturedAt),
            rows.Max(row => row.CapturedAt),
            rows.Exists(row => row.IsLandfall),
            tracks,
            DescribeAgreement(tracks));
    }

    private static double? Round(double? value) => value is null ? null : Math.Round(value.Value, 2);

    /// <summary>
    /// States how far the agencies diverge on peak intensity.
    /// </summary>
    /// <remarks>
    /// Generated from the readings present so it says something true about this storm rather
    /// than issuing a generic warning. Distinguishes the two cases that matter: agencies
    /// sharing an averaging period genuinely disagree about the storm, whereas agencies using
    /// different periods are not measuring the same thing at all.
    /// </remarks>
    private static string? DescribeAgreement(List<GetCycloneTrack.AgencyTrackResponse> tracks)
    {
        var withPeaks = tracks.FindAll(track => track.PeakWindKnots is not null);

        if (withPeaks.Count < 2)
        {
            return null;
        }

        var periods = withPeaks.ConvertAll(track => track.AveragingPeriod).Distinct().ToList();
        var strongest = withPeaks.MaxBy(track => track.PeakWindKnots)!;
        var weakest = withPeaks.MinBy(track => track.PeakWindKnots)!;
        var spread = strongest.PeakWindKnots!.Value - weakest.PeakWindKnots!.Value;

        if (periods.Count == 1)
        {
            return spread < 5d
                ? $"All {withPeaks.Count} agencies used a {periods[0]} average and agree to "
                    + "within 5 knots."
                : $"All {withPeaks.Count} agencies used a {periods[0]} average, so the "
                    + $"{spread:0} knot spread is a real disagreement, not a difference of method.";
        }

        // Deliberately one sentence. The full explanation of why averaging periods are not
        // interchangeable belongs to the About Data page and the intensity legend; repeating it
        // in every storm panel produced a paragraph of caveat above the data it qualified, which
        // reads as hedging rather than as rigour.
        return $"Peaks range {weakest.PeakWindKnots:0}\u2013{strongest.PeakWindKnots:0} kt across "
            + $"{periods.Count} averaging periods, so the {spread:0} knot spread is partly "
            + "method rather than storm.";
    }
}
