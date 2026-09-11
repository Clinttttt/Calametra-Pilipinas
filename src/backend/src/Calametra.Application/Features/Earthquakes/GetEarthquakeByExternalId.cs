using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Earthquakes.Shared;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Events;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Earthquakes;

/// <summary>
/// One earthquake, found by the identifier the reporting agency published rather than by this
/// platform's own primary key.
/// </summary>
/// <remarks>
/// <para>
/// Every <c>HazardEvent.Id</c> is a <c>Guid.CreateVersion7()</c> minted at insert, so it is stable
/// only for the lifetime of one database. Drop and re-ingest the archive — which this platform
/// expects to happen repeatedly as sources are refreshed and as the PHIVOLCS agreement lands — and
/// every internal id changes.
/// </para>
/// <para>
/// That makes an internal id unusable in anything durable. Curated story content referenced these
/// Guids and would have silently lost its event data on the next re-ingest: the fetch would 404, the
/// readings panel would render empty, and nothing would report an error. An agency's own identifier
/// (<c>us20008ixa</c>, <c>iscgem913230</c>) survives re-ingestion, is citable in a publication, and
/// resolves to the same event in anyone else's copy of the catalogue.
/// </para>
/// <para>
/// The identifier lives on <see cref="EarthquakeObservation"/> rather than on the event, because it
/// is the *agency's* id for its own reading — two agencies reporting one earthquake supply two. So a
/// lookup finds the observation and returns the event it belongs to.
/// </para>
/// </remarks>
public static class GetEarthquakeByExternalId
{
    public sealed record Query : IQuery<EarthquakeDetailResponse>
    {
        /// <summary>The reporting agency's own event identifier, such as <c>us20008ixa</c>.</summary>
        public required string ExternalEventId { get; init; }
    }

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.ExternalEventId).NotEmpty().MaximumLength(128);
        }
    }

    internal sealed class Handler(IApplicationDbContext context)
        : IQueryHandler<Query, EarthquakeDetailResponse>
    {
        public async Task<Result<EarthquakeDetailResponse>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var external = request.ExternalEventId.Trim();

            // Ordered so the result is deterministic when more than one observation carries the same
            // external id — legitimate, since a re-analysis can be filed under the same agency id.
            // Earliest wins: it is the reading that first established this event in the archive.
            var eventId = await context.EarthquakeObservations.AsNoTracking()
                .Where(observation => observation.ExternalEventId == external)
                .OrderBy(observation => observation.ObservedAt)
                .Select(observation => observation.HazardEventId)
                .FirstOrDefaultAsync(cancellationToken);

            if (eventId == Guid.Empty)
            {
                return Result<EarthquakeDetailResponse>.Failure(EventErrors.NotFound);
            }

            var detail = await EarthquakeDetailLoader.Load(context, eventId, cancellationToken);

            return detail is null
                ? Result<EarthquakeDetailResponse>.Failure(EventErrors.NotFound)
                : Result<EarthquakeDetailResponse>.Success(detail);
        }
    }
}
