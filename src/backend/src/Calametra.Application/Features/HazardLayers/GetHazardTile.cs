using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Abstractions;
using FluentValidation;

namespace Calametra.Application.Features.HazardLayers;

/// <summary>
/// Fetches one hazard tile from the publisher's map service.
/// </summary>
/// <remarks>
/// <para>
/// Exists because the upstream service sends no CORS headers and because attribution,
/// caching and rate limiting have to be applied somewhere the client cannot bypass.
/// The tile is returned exactly as the publisher rendered it; Calametra does not
/// restyle official hazard imagery.
/// </para>
/// <para>
/// <b>A tile is addressed one of two ways</b>, because the publishers differ. PHIVOLCS renders on
/// demand and is asked for a bounding box. DOST-MGB publishes a 24-level tile cache, and is asked
/// for <c>z/x/y</c> — measured at 60-120 ms against 18.8-19.4 seconds for the same tile rendered
/// through WMS, which is the difference between a usable layer and an unusable one.
/// </para>
/// </remarks>
public static class GetHazardTile
{
    public sealed record Query : IQuery<HazardMapImage>
    {
        public required Guid HazardLayerId { get; init; }

        /// <summary>Bounding box in EPSG:3857 metres as <c>minX,minY,maxX,maxY</c>.</summary>
        /// <remarks>Required unless a cached tile address is supplied.</remarks>
        public string? BoundingBox { get; init; }

        public int Width { get; init; } = 256;

        public int Height { get; init; } = 256;

        /// <summary>Zoom of a cached tile. Supplied with <see cref="Column"/> and <see cref="Row"/>.</summary>
        public int? Zoom { get; init; }

        public int? Column { get; init; }

        public int? Row { get; init; }

        internal bool AddressesCachedTile => Zoom is not null && Column is not null && Row is not null;
    }

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.HazardLayerId).NotEmpty();

            RuleFor(query => query.Width).InclusiveBetween(1, 2048);
            RuleFor(query => query.Height).InclusiveBetween(1, 2048);

            // 24 levels is what the MGB services publish; the web's own scheme stops at 22 or so.
            // Bounded because the value is forwarded into a third party's URL.
            RuleFor(query => query.Zoom)
                .InclusiveBetween(0, 24)
                .When(query => query.Zoom is not null);

            RuleFor(query => query.Column).GreaterThanOrEqualTo(0).When(query => query.Column is not null);
            RuleFor(query => query.Row).GreaterThanOrEqualTo(0).When(query => query.Row is not null);

            // One addressing mode or the other, and never neither. A request with a zoom but no
            // column would otherwise fall through to the rendered path with a null box.
            RuleFor(query => query)
                .Must(query => query.AddressesCachedTile || !string.IsNullOrWhiteSpace(query.BoundingBox))
                .WithMessage(
                    "Supply either a BoundingBox or a complete Zoom, Column and Row.");

            // The bounding box is forwarded to a third party, so its shape is
            // validated here rather than trusted. Four comma-separated finite
            // numbers, nothing else.
            RuleFor(query => query.BoundingBox)
                .Must(BeFourFiniteNumbers)
                .When(query => !query.AddressesCachedTile)
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
                    Zoom = request.Zoom,
                    Column = request.Column,
                    Row = request.Row,
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
