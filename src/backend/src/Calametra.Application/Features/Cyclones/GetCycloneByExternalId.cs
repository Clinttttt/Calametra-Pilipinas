using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Cyclones.Shared;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Events;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Cyclones;

/// <summary>
/// One cyclone, found by the identifier the source archive published rather than by this
/// platform's own primary key.
/// </summary>
/// <remarks>
/// <para>
/// The cyclone counterpart of <see cref="Earthquakes.GetEarthquakeByExternalId"/>, and it exists
/// for the same reason: <c>HazardEvent.Id</c> is a <c>Guid.CreateVersion7()</c> minted at insert,
/// so it is stable only for the lifetime of one database. The IBTrACS import is explicitly a
/// re-runnable one-shot — best-track data is revised once a season — so a storm's internal id is
/// expected to change, and anything durable that referenced one would break silently.
/// </para>
/// <para>
/// The durable key is the IBTrACS <b>SID</b>, such as <c>2013306N07162</c> for Haiyan: season, day
/// of year, and the position of first detection. It is assigned by the compiler of the archive
/// rather than by any one agency, which is why it lives here and not per-agency — every agency's
/// fixes for one storm carry the same SID.
/// </para>
/// <para>
/// <b>Name and season are deliberately not the key.</b> International names are reused, which this
/// archive demonstrates: MERANTI appears in 2010 and 2016, GONI in 2015 and 2020, MAWAR in 2012,
/// 2017 and 2023. Name plus season disambiguates those, but it still keys durable content on a
/// label that a warning centre can retire or re-assign, and it would silently resolve to the wrong
/// storm if it ever collided. The SID cannot.
/// </para>
/// </remarks>
public static class GetCycloneByExternalId
{
    public sealed record Query : IQuery<GetCycloneTrack.CycloneTrackResponse>
    {
        /// <summary>The IBTrACS storm identifier, such as <c>2013306N07162</c>.</summary>
        public required string ExternalStormId { get; init; }
    }

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.ExternalStormId).NotEmpty().MaximumLength(64);
        }
    }

    internal sealed class Handler(IApplicationDbContext context)
        : IQueryHandler<Query, GetCycloneTrack.CycloneTrackResponse>
    {
        public async Task<Result<GetCycloneTrack.CycloneTrackResponse>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var external = request.ExternalStormId.Trim();

            // Every fix for a storm carries the same SID — 272 of them for Haiyan across four
            // agencies — so this reads the first and takes the event it belongs to. Ordered by
            // capture time rather than left unordered so the result cannot vary between runs if a
            // SID were ever to appear against more than one event.
            var eventId = await context.CycloneTrackPoints.AsNoTracking()
                .Where(trackPoint => trackPoint.ExternalStormId == external)
                .OrderBy(trackPoint => trackPoint.CapturedAt)
                .Select(trackPoint => trackPoint.HazardEventId)
                .FirstOrDefaultAsync(cancellationToken);

            if (eventId == Guid.Empty)
            {
                return Result<GetCycloneTrack.CycloneTrackResponse>.Failure(EventErrors.NotFound);
            }

            var track = await CycloneTrackLoader.Load(context, eventId, cancellationToken);

            return track is null
                ? Result<GetCycloneTrack.CycloneTrackResponse>.Failure(EventErrors.NotFound)
                : Result<GetCycloneTrack.CycloneTrackResponse>.Success(track);
        }
    }
}
