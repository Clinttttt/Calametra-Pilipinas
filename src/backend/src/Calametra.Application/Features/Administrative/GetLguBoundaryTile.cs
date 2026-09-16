using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Abstractions.Tiles;
using Calametra.Domain.Abstractions;

namespace Calametra.Application.Features.Administrative;

/// <summary>
/// One Mapbox Vector Tile of current city and municipality outlines.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tiles rather than one national payload.</b> The in-force set is 1,611 land outlines whose full
/// resolution runs to hundreds of megabytes as GeoJSON — the COD-AB source archive alone is 885 MB. Sending
/// that to a browser would be slower than the map is useful, and it would send Batanes to a reader looking
/// at Zamboanga. A tile carries only what its own extent covers, and PostGIS builds it in the database
/// rather than in .NET, so no geometry is ever materialised into managed memory.
/// </para>
/// <para>
/// <b>Only geometry in force, and only from the canonical source.</b> The query filters to
/// <c>valid_to IS NULL</c> and to rows carrying a <c>source_feature_code</c>, which is what distinguishes a
/// COD-AB land outline from the superseded OSM relations retained beside it. ADR-005 D2a forbids serving the
/// two as one concept: OSM outlines extend to municipal waters, so a tile mixing them would hand the client
/// two different meanings of "boundary" with nothing to tell them apart.
/// </para>
/// <para>
/// <b>Properties are the interaction's, not the archive's.</b> Canonical PSGC as the feature id, the
/// register's authoritative name, the level, and the area. Area travels with the outline because ADR-005 D1
/// requires any figure derived from a boundary to state the unit's area beside it, and the client cannot
/// compute it from a simplified tile. Nothing else is sent: a tile is a delivery format, not an API for the
/// register.
/// </para>
/// </remarks>
public static class GetLguBoundaryTile
{
    /// <param name="Z">Zoom. Bounded because a tile outside the pyramid is a client bug, not a query.</param>
    public sealed record Query(int Z, int X, int Y) : IQuery<LguBoundaryTile>;

    /// <param name="Content">
    /// Protobuf bytes. Empty when the tile covers no land, which is the normal case for an archipelago and
    /// is cheaper to serve than an error.
    /// </param>
    public sealed record LguBoundaryTile(byte[] Content);

    internal sealed class Handler(ILguBoundaryTileReader reader)
        : IQueryHandler<Query, LguBoundaryTile>
    {
        /// <summary>
        /// Deepest zoom served.
        /// </summary>
        /// <remarks>
        /// 14 is past the point where a municipal outline is the frame rather than a feature in it. Beyond
        /// it MapLibre reuses the level-14 tile by overzooming, which costs nothing and keeps the tile count
        /// finite.
        /// </remarks>
        private const int MaximumZoom = 14;

        /// <summary>
        /// Shallowest zoom served.
        /// </summary>
        /// <remarks>
        /// <para>
        /// 6, because ADR-005 D6 says municipality boundaries are <em>off</em> at national zoom. Serving
        /// them there would be building a tile for a band the design does not draw.
        /// </para>
        /// <para>
        /// It is also the honest contract. Measured on the tiling view, a zoom-4 tile is 189 KB and takes
        /// about seven seconds because every one of the 1,611 outlines falls inside it; zoom 6 is 76 KB in
        /// 255 ms. A layer that cannot be drawn usefully at a zoom should not be requestable at it.
        /// </para>
        /// </remarks>
        private const int MinimumZoom = 6;

        private static readonly Error OutsidePyramid = new(
            ErrorType.Validation,
            "lgu_tile.outside_pyramid",
            "The tile coordinate is outside the served pyramid.");

        public async Task<Result<LguBoundaryTile>> Handle(
            Query request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            if (request.Z < MinimumZoom || request.Z > MaximumZoom)
            {
                return Result<LguBoundaryTile>.Failure(OutsidePyramid);
            }

            var span = 1 << request.Z;

            if (request.X < 0 || request.Y < 0 || request.X >= span || request.Y >= span)
            {
                return Result<LguBoundaryTile>.Failure(OutsidePyramid);
            }

            // Simplification is per zoom and is done in web-mercator metres, so a tolerance means the same
            // ground distance everywhere rather than shrinking with latitude. At national zoom an outline
            // carries no information its simplification would destroy (D6); at local zoom none is applied,
            // because that is where the edge is the answer.
            var tolerance = ToleranceMetres(request.Z);

            var tile = await ReadTileAsync(request, tolerance, cancellationToken);

            return Result<LguBoundaryTile>.Success(new LguBoundaryTile(tile ?? []));
        }

        /// <summary>
        /// Simplification tolerance in mercator metres, by zoom.
        /// </summary>
        /// <remarks>
        /// Roughly one screen pixel of ground at each level: a tile is 4,096 units across whatever its
        /// extent, so detail finer than a pixel is bytes the reader cannot see. Held as a table rather than
        /// a formula because the useful values are not linear — the jump that matters is between "the
        /// country as a shape" and "this municipality's coast".
        /// </remarks>
        private static double ToleranceMetres(int zoom) =>
            zoom switch
            {
                <= 5 => 2_000d,
                6 => 1_000d,
                7 => 500d,
                8 => 250d,
                9 => 120d,
                10 => 60d,
                11 => 30d,
                _ => 0d,
            };

        private Task<byte[]?> ReadTileAsync(
            Query request,
            double tolerance,
            CancellationToken cancellationToken) =>
            reader.ReadAsync(request.Z, request.X, request.Y, tolerance, cancellationToken);
    }
}
