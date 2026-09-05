using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Abstractions;
using FluentValidation;

namespace Calametra.Application.Features.HazardLayers;

/// <summary>
/// Fetches one rendered hazard tile from the publisher's map service.
/// </summary>
/// <remarks>
/// Exists because the upstream service sends no CORS headers and because attribution,
/// caching and rate limiting have to be applied somewhere the client cannot bypass.
/// The tile is returned exactly as the publisher rendered it; Calametra does not
/// restyle official hazard imagery.
/// </remarks>
public static class GetHazardTile
{
    public sealed record Query : IQuery<HazardMapImage>
    {
        public required Guid HazardLayerId { get; init; }

        /// <summary>Bounding box in EPSG:3857 metres as <c>minX,minY,maxX,maxY</c>.</summary>
        public required string BoundingBox { get; init; }

        public int Width { get; init; } = 256;

        public int Height { get; init; } = 256;
    }

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.HazardLayerId).NotEmpty();

            RuleFor(query => query.Width).InclusiveBetween(1, 2048);
            RuleFor(query => query.Height).InclusiveBetween(1, 2048);

            // The bounding box is forwarded to a third party, so its shape is
            // validated here rather than trusted. Four comma-separated finite
            // numbers, nothing else.
            RuleFor(query => query.BoundingBox)
                .NotEmpty()
                .Must(BeFourFiniteNumbers)
                .WithMessage("BoundingBox must be four comma-separated numbers: minX,minY,maxX,maxY.");
        }

        private static bool BeFourFiniteNumbers(string? boundingBox)
        {
            if (string.IsNullOrWhiteSpace(boundingBox))
            {
                return false;
            }

            var parts = boundingBox.Split(',', StringSplitOptions.TrimEntries);

            return parts.Length == 4
                && Array.TrueForAll(parts, part =>
                    double.TryParse(part, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var value)
                    && double.IsFinite(value));
        }
    }

    internal sealed class Handler(IHazardMapService hazardMapService)
        : IQueryHandler<Query, HazardMapImage>
    {
        public Task<Result<HazardMapImage>> Handle(Query request, CancellationToken cancellationToken) =>
            hazardMapService.GetTileAsync(
                new HazardTileRequest
                {
                    HazardLayerId = request.HazardLayerId,
                    BoundingBox3857 = request.BoundingBox,
                    Width = request.Width,
                    Height = request.Height,
                },
                cancellationToken);
    }
}

/// <summary>
/// Returns the publisher's own attributes for the feature at a clicked position.
/// </summary>
/// <remarks>
/// Backs the interactive fault inspection described in the project concept. For a
/// PHIVOLCS active fault this surfaces the fault system, the segment name, the year
/// mapped and the mapping project — the authority's own words, unedited.
/// </remarks>
public static class IdentifyHazardFeature
{
    public sealed record Query : IQuery<IReadOnlyList<HazardFeatureAttributes>>
    {
        public required Guid HazardLayerId { get; init; }

        public required double Latitude { get; init; }

        public required double Longitude { get; init; }
    }

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.HazardLayerId).NotEmpty();
            RuleFor(query => query.Latitude).InclusiveBetween(-90d, 90d);
            RuleFor(query => query.Longitude).InclusiveBetween(-180d, 180d);
        }
    }

    internal sealed class Handler(IHazardMapService hazardMapService)
        : IQueryHandler<Query, IReadOnlyList<HazardFeatureAttributes>>
    {
        public Task<Result<IReadOnlyList<HazardFeatureAttributes>>> Handle(
            Query request,
            CancellationToken cancellationToken) =>
            hazardMapService.IdentifyAsync(
                new HazardIdentifyRequest
                {
                    HazardLayerId = request.HazardLayerId,
                    Latitude = request.Latitude,
                    Longitude = request.Longitude,
                },
                cancellationToken);
    }
}
