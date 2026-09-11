using Calametra.Application.Abstractions.Data;
using Calametra.Application.Features.Places.Shared;
using Calametra.Domain.Seismology;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Earthquakes.Shared;

/// <summary>
/// Loads the full multi-agency reading set for an earthquake.
/// </summary>
/// <remarks>
/// Extracted so <c>GetEarthquakeDetail</c> and <c>CompareEarthquakes</c> share one
/// implementation. The disagreement detection and its plain-language explanation are the
/// part that must not be duplicated: they encode the platform's central claim — that the
/// same earthquake has more than one correct magnitude — and two copies would eventually
/// disagree with each other about when agencies disagree.
/// </remarks>
internal static class EarthquakeDetailLoader
{
    /// <summary>
    /// Loads one event, or null when it does not exist.
    /// </summary>
    public static async Task<EarthquakeDetailResponse?> Load(
        IApplicationDbContext context,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var hazardEvent = await context.HazardEvents.AsNoTracking()
            .Where(candidate => candidate.Id == eventId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.CanonicalOccurredAt,
                candidate.PreferredObservationId,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (hazardEvent is null)
        {
            return null;
        }

        var rows = await (
            from observation in context.EarthquakeObservations.AsNoTracking()
            join source in context.DataSources.AsNoTracking()
                on observation.DataSourceId equals source.Id
            where observation.HazardEventId == eventId
            // The authoritative national agency first, so a reader sees PHIVOLCS before
            // an international catalogue when both hold the event.
            orderby source.IsAuthoritativeForPhilippines descending, source.Agency
            select new
            {
                observation.Id,
                observation.ExternalEventId,
                observation.ObservedAt,
                observation.Latitude,
                observation.Longitude,
                observation.MagnitudeValue,
                observation.MagnitudeScale,
                observation.DepthKilometres,
                observation.DepthQuality,
                observation.SourceUrl,
                source.Agency,
                source.DatasetName,
                source.Slug,
            }).ToListAsync(cancellationToken);

        var observations = rows
            .Select(row => new ObservationResponse(
                row.Id,
                row.Agency,
                row.DatasetName,
                row.Slug,
                row.ExternalEventId,
                row.ObservedAt,
                row.Latitude,
                row.Longitude,
                MagnitudeResponse.From(row.MagnitudeValue, row.MagnitudeScale),
                DepthResponse.From(row.DepthKilometres, row.DepthQuality),
                row.Id == hazardEvent.PreferredObservationId,
                row.SourceUrl))
            .ToList();

        var preferred = observations.Find(observation => observation.IsPreferred)
            ?? observations.FirstOrDefault();

        var latitude = preferred?.Latitude ?? 0d;
        var longitude = preferred?.Longitude ?? 0d;

        // Named after the preferred agency's epicentre, not an average of the agencies'. Two
        // agencies place the 2017 Surigao event 2.54 km apart, and a mean position is one no
        // agency published — the same refusal the platform applies to magnitudes.
        var places = await NearestPlaceLocator.LoadAsync(context, cancellationToken);
        var location = preferred is null ? null : places.Describe(latitude, longitude);

        return new EarthquakeDetailResponse(
            hazardEvent.Id,
            hazardEvent.CanonicalOccurredAt,
            latitude,
            longitude,
            location?.DescribeWithContainer(),
            observations,
            HasDisagreement(rows.Select(row => (row.MagnitudeValue, row.MagnitudeScale))),
            ExplainDisagreement(rows.Select(row => (row.Agency, row.MagnitudeValue, row.MagnitudeScale))));
    }

    /// <summary>
    /// Whether agencies disagree beyond rounding, or report on scales that cannot
    /// be compared at all.
    /// </summary>
    public static bool HasDisagreement(IEnumerable<(double? Value, MagnitudeType Scale)> readings)
    {
        var magnitudes = readings
            .Where(reading => reading.Value is not null)
            .Select(reading => new MagnitudeReading(reading.Value!.Value, reading.Scale))
            .ToList();

        for (var i = 0; i < magnitudes.Count - 1; i++)
        {
            for (var j = i + 1; j < magnitudes.Count; j++)
            {
                var difference = magnitudes[i].DifferenceFrom(magnitudes[j]);

                // Null means the scales are not comparable, which is itself a
                // disagreement the user needs told about.
                if (difference is null || difference > 0.05d)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Plain-language reason the reported magnitudes differ.
    /// </summary>
    /// <remarks>
    /// Generated from the actual scales present rather than a fixed sentence, so
    /// it says something true about *this* event instead of a generic disclaimer.
    /// </remarks>
    public static string? ExplainDisagreement(
        IEnumerable<(string Agency, double? Value, MagnitudeType Scale)> readings)
    {
        var reported = readings
            .Where(reading => reading.Value is not null)
            .Select(reading => (reading.Agency, Reading: new MagnitudeReading(reading.Value!.Value, reading.Scale)))
            .ToList();

        if (reported.Count < 2)
        {
            return null;
        }

        var families = reported.Select(entry => entry.Reading.Type.Family()).Distinct().ToList();

        var summary = string.Join(
            " and ",
            reported.Select(entry => $"{entry.Agency} reports {entry.Reading.Display()}"));

        if (families.Count > 1)
        {
            return $"{summary}. These are not the same quantity: "
                + string.Join(
                    ", ",
                    reported.Select(entry =>
                        $"{entry.Reading.Type.Label()} is a {Describe(entry.Reading.Type.Family())} magnitude"))
                + ". Values on different scales cannot be compared directly, and neither is wrong — "
                + "they are different measurements of the same earthquake.";
        }

        return $"{summary}. Both use a {Describe(families[0])} magnitude, so the difference reflects "
            + "different seismic networks and processing rather than different scales.";
    }

    /// <summary>Family name in words, for prose rather than for a label.</summary>
    public static string Describe(MagnitudeScaleFamily family) => family switch
    {
        MagnitudeScaleFamily.BodyWave => "body-wave",
        MagnitudeScaleFamily.SurfaceWave => "surface-wave",
        MagnitudeScaleFamily.Local => "local",
        MagnitudeScaleFamily.Moment => "moment",
        _ => "unstated",
    };
}
