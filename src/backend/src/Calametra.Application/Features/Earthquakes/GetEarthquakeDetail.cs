using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Earthquakes.Shared;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Events;
using Calametra.Domain.Seismology;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Earthquakes;

/// <summary>
/// Returns one earthquake with every agency's reading of it.
/// </summary>
/// <remarks>
/// This is the slice that makes the platform's central point visible. The list view
/// shows one preferred reading per event because a table cannot show four; this view
/// shows all of them, side by side, with the agency and scale attached to each.
/// <para>
/// For the 10 February 2017 Surigao earthquake that means PHIVOLCS Ms 6.7 at 10 km
/// beside USGS Mww 6.5 at 15 km — two correct answers from different networks using
/// different methods. The response also carries an explanation of *why* they differ,
/// because showing a user two numbers and leaving them to guess is worse than
/// showing one.
/// </para>
/// </remarks>
public static class GetEarthquakeDetail
{
    public sealed record Query : IQuery<EarthquakeDetailResponse>
    {
        public required Guid EventId { get; init; }
    }

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator() => RuleFor(query => query.EventId).NotEmpty();
    }

    internal sealed class Handler(IApplicationDbContext context)
        : IQueryHandler<Query, EarthquakeDetailResponse>
    {
        public async Task<Result<EarthquakeDetailResponse>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var hazardEvent = await context.HazardEvents
                .AsNoTracking()
                .Where(candidate => candidate.Id == request.EventId)
                .Select(candidate => new
                {
                    candidate.Id,
                    candidate.CanonicalOccurredAt,
                    candidate.PreferredObservationId,
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (hazardEvent is null)
            {
                return Result<EarthquakeDetailResponse>.Failure(EventErrors.NotFound);
            }

            var rows = await (
                from observation in context.EventObservations.AsNoTracking()
                join source in context.DataSources.AsNoTracking()
                    on observation.DataSourceId equals source.Id
                where observation.HazardEventId == hazardEvent.Id
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

            var preferred = observations.Find(observation => observation.IsPreferred) ?? observations.FirstOrDefault();

            return Result<EarthquakeDetailResponse>.Success(new EarthquakeDetailResponse(
                hazardEvent.Id,
                hazardEvent.CanonicalOccurredAt,
                preferred?.Latitude ?? 0d,
                preferred?.Longitude ?? 0d,
                observations,
                HasDisagreement(rows.Select(row => (row.MagnitudeValue, row.MagnitudeScale))),
                ExplainDisagreement(rows.Select(row => (row.Agency, row.MagnitudeValue, row.MagnitudeScale)))));
        }

        /// <summary>
        /// Whether agencies disagree beyond rounding, or report on scales that cannot
        /// be compared at all.
        /// </summary>
        private static bool HasDisagreement(IEnumerable<(double? Value, MagnitudeType Scale)> readings)
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
        private static string? ExplainDisagreement(
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

        private static string Describe(MagnitudeScaleFamily family) => family switch
        {
            MagnitudeScaleFamily.BodyWave => "body-wave",
            MagnitudeScaleFamily.SurfaceWave => "surface-wave",
            MagnitudeScaleFamily.Local => "local",
            MagnitudeScaleFamily.Moment => "moment",
            _ => "unstated",
        };
    }
}
