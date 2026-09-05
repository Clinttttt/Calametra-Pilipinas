using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.HazardLayers;

/// <summary>
/// Returns locally stored hazard geometry for a layer as GeoJSON.
/// </summary>
/// <remarks>
/// Serves the layers Calametra is licensed to store — currently the GEM fault
/// traces. Proxied layers have no rows and are rendered through the tile endpoint
/// instead, so the client chooses its path from the layer's
/// <c>deliveryMode</c> rather than guessing.
/// <para>
/// GeoJSON rather than vector tiles. At CARAGA scale the fault set is 19 features,
/// so a single document is smaller and simpler than a tile pyramid, and it lets
/// MapLibre style and hit-test the traces as real features — which is what makes
/// hovering a fault to inspect it possible.
/// </para>
/// </remarks>
public static class GetHazardFeatures
{
    public sealed record Query : IQuery<HazardFeatureCollection>
    {
        public required Guid HazardLayerId { get; init; }
    }

    /// <summary>A GeoJSON FeatureCollection, plus the attribution that must accompany it.</summary>
    public sealed record HazardFeatureCollection(
        string Type,
        IReadOnlyList<HazardGeoJsonFeature> Features,
        string Attribution,
        string? TermsUrl);

    public sealed record HazardGeoJsonFeature(
        string Type,
        HazardFeatureProperties Properties,
        object Geometry);

    public sealed record HazardFeatureProperties(
        Guid Id,
        string ExternalId,
        string? Name,
        string? Classification);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator() => RuleFor(query => query.HazardLayerId).NotEmpty();
    }

    internal sealed class Handler(IApplicationDbContext context)
        : IQueryHandler<Query, HazardFeatureCollection>
    {
        public async Task<Result<HazardFeatureCollection>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var layer = await context.HazardLayers
                .AsNoTracking()
                .Where(candidate => candidate.Id == request.HazardLayerId)
                .Select(candidate => new { candidate.Id, candidate.DataSourceId })
                .FirstOrDefaultAsync(cancellationToken);

            if (layer is null)
            {
                return Result<HazardFeatureCollection>.Failure(Domain.Hazards.HazardLayerErrors.NotFound);
            }

            var source = await context.DataSources
                .AsNoTracking()
                .Where(candidate => candidate.Id == layer.DataSourceId)
                .Select(candidate => new { candidate.Attribution, candidate.TermsUrl })
                .FirstAsync(cancellationToken);

            var rows = await context.HazardFeatures
                .AsNoTracking()
                .Where(feature => feature.HazardLayerId == layer.Id)
                .OrderBy(feature => feature.Name)
                .Select(feature => new
                {
                    feature.Id,
                    feature.ExternalId,
                    feature.Name,
                    feature.Classification,
                    feature.Geometry,
                })
                .ToListAsync(cancellationToken);

            var features = rows
                .Select(row => new HazardGeoJsonFeature(
                    "Feature",
                    new HazardFeatureProperties(row.Id, row.ExternalId, row.Name, row.Classification),
                    ToGeoJsonGeometry(row.Geometry)))
                .ToList();

            return Result<HazardFeatureCollection>.Success(
                new HazardFeatureCollection(
                    "FeatureCollection",
                    features,
                    source.Attribution,
                    source.TermsUrl));
        }

        /// <summary>
        /// Converts NTS geometry to a GeoJSON geometry object.
        /// </summary>
        /// <remarks>
        /// Hand-written rather than pulling in a GeoJSON serialiser. Only two shapes
        /// are needed — line traces and their multi-part form — and the coordinate
        /// order reversal (NTS is x/y, GeoJSON is longitude/latitude) is the one
        /// thing worth doing explicitly rather than trusting to a library default.
        /// </remarks>
        private static object ToGeoJsonGeometry(NetTopologySuite.Geometries.Geometry geometry) =>
            geometry switch
            {
                NetTopologySuite.Geometries.LineString line => new
                {
                    type = "LineString",
                    coordinates = line.Coordinates
                        .Select(coordinate => new[] { coordinate.X, coordinate.Y })
                        .ToArray(),
                },
                NetTopologySuite.Geometries.MultiLineString multi => new
                {
                    type = "MultiLineString",
                    coordinates = multi.Geometries
                        .OfType<NetTopologySuite.Geometries.LineString>()
                        .Select(line => line.Coordinates
                            .Select(coordinate => new[] { coordinate.X, coordinate.Y })
                            .ToArray())
                        .ToArray(),
                },
                NetTopologySuite.Geometries.Point point => new
                {
                    type = "Point",
                    coordinates = new[] { point.X, point.Y },
                },
                _ => new
                {
                    type = "GeometryCollection",
                    geometries = Array.Empty<object>(),
                },
            };
    }
}
