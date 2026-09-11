using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Earthquakes.Shared;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Events;
using FluentValidation;

namespace Calametra.Application.Features.Earthquakes;

/// <summary>
/// One earthquake, with every agency's reading and the disagreement made explicit.
/// </summary>
/// <remarks>
/// The point of this slice is that it does not pick a winner. An event carries as many
/// readings as agencies reported, each named, and where they differ the response says why
/// in plain language generated from the scales actually present.
/// <para>
/// The loading itself lives in <see cref="EarthquakeDetailLoader"/> because
/// <c>CompareEarthquakes</c> needs the identical shape for two events at once, and the
/// disagreement logic is the last thing that should exist in two copies.
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
        public Validator()
        {
            RuleFor(query => query.EventId).NotEmpty();
        }
    }

    internal sealed class Handler(IApplicationDbContext context)
        : IQueryHandler<Query, EarthquakeDetailResponse>
    {
        public async Task<Result<EarthquakeDetailResponse>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var detail = await EarthquakeDetailLoader.Load(context, request.EventId, cancellationToken);

            return detail is null
                ? Result<EarthquakeDetailResponse>.Failure(EventErrors.NotFound)
                : Result<EarthquakeDetailResponse>.Success(detail);
        }
    }
}
