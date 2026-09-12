using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Hazards;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.HazardLayers;

/// <summary>
/// Returns the hazard layer catalogue, grouped by lens, with attribution and
/// plain-language explanations attached.
/// </summary>
/// <remarks>
/// The client builds its layer controls from this response rather than from a
/// hard-coded list, so switching a layer on, changing its explainer text, or moving
/// it from proxied to locally stored is a data change with no client release.
/// </remarks>
public static class ListHazardLayers
{
    public sealed record Query : IQuery<IReadOnlyList<HazardLayerResponse>>
    {
        /// <summary>Restrict to a single lens. Omitted returns the whole catalogue.</summary>
        public HazardLens? Lens { get; init; }
    }

    public sealed record HazardLayerResponse(
        Guid Id,
        string HazardType,
        string Lens,
        string DisplayName,
        string DeliveryMode,
        bool SupportsFeatureInfo,
        bool SupportsCachedTiles,
        bool IsEnabledByDefault,
        int SortOrder,

        /// <summary>
        /// Zoom below which the client must not request this layer, or null for no limit.
        /// </summary>
        /// <remarks>
        /// Served to the client because only the client knows the zoom: a bounding-box tile request
        /// carries none, so the proxy cannot distinguish a national tile from a local one. It is a
        /// measured property of the publisher's service — ground shaking renders a national-scale tile
        /// in 19.1 s against 2.9 s at 400 km — rather than a presentation preference.
        /// </remarks>
        int? MinimumZoom,
        string? Explainer,
        string? InterpretationNote,
        string SourceAgency,
        string SourceDatasetName,
        string Attribution,
        string? SourceUrl,
        string? TermsUrl);

    internal sealed class Handler(IApplicationDbContext context)
        : IQueryHandler<Query, IReadOnlyList<HazardLayerResponse>>
    {
        public async Task<Result<IReadOnlyList<HazardLayerResponse>>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            var query =
                from layer in context.HazardLayers.AsNoTracking()
                join source in context.DataSources.AsNoTracking()
                    on layer.DataSourceId equals source.Id
                select new { Layer = layer, Source = source };

            if (request.Lens is { } lens)
            {
                query = query.Where(row => row.Layer.Lens == lens);
            }

            var rows = await query
                .OrderBy(row => row.Layer.Lens)
                .ThenBy(row => row.Layer.SortOrder)
                .Select(row => new HazardLayerResponse(
                    row.Layer.Id,
                    row.Layer.HazardType.ToString(),
                    row.Layer.Lens.ToString(),
                    row.Layer.DisplayName,
                    row.Layer.DeliveryMode.ToString(),
                    row.Layer.SupportsFeatureInfo,
                    // Computed from whether an endpoint is stored, so the client is told what is
                    // actually available rather than a flag someone remembered to set.
                    row.Layer.CachedTileEndpoint != null,
                    row.Layer.IsEnabledByDefault,
                    row.Layer.SortOrder,
                    row.Layer.MinimumZoom,
                    row.Layer.Explainer,
                    row.Layer.InterpretationNote,
                    row.Source.Agency,
                    row.Source.DatasetName,
                    row.Source.Attribution,
                    row.Source.SourceUrl,
                    row.Source.TermsUrl))
                .ToListAsync(cancellationToken);

            return Result<IReadOnlyList<HazardLayerResponse>>.Success(rows);
        }
    }
}
