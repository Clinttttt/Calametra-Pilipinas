using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Abstractions.Sources;
using Calametra.Domain.Abstractions;
using Calametra.Domain.Places;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;

namespace Calametra.Application.Features.Ingestion;

/// <summary>
/// Moves each city and municipality onto its mapped town centre.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Every distance this platform states about a place is measured from one
/// point: the radius search, the earthquake counts, the nearest-fault list and the name given to
/// every epicentre. That point came from the administrative gazetteer, and measured across the
/// archive on 2026-09-12 it is not good enough for the claims made on it — 1,090 of 1,647
/// coordinates are rounded to the nearest arc-minute, which is a degrees-and-minutes table and
/// cannot be better than about 900 m; five are rounded to a quarter of a degree, about 28 km; and
/// the mean distance to the mapped town centre is 5.2 km, reaching 31 km. Carmen in Surigao del Sur
/// sat 14 km inland of the town.
/// </para>
/// <para>
/// <b>A separate step from the import, on purpose.</b> The gazetteer remains the authority for what
/// a place is called, which province contains it and what its Philippine Standard Geographic Code
/// is. This changes one field, records which source supplied it, and leaves the rest alone — so the
/// interface can state that a name came from one publisher and the position from another instead of
/// presenting a blended row with a single credit.
/// </para>
/// <para>
/// <b>Ambiguity is refused rather than guessed.</b> Philippine municipality names repeat across
/// provinces — 1,695 mapped settlements carry 1,459 distinct names — and the gazetteer error being
/// corrected reaches 31 km, so the search radius cannot be tightened until the ambiguity is gone.
/// Where two same-named candidates are comparably close, the place keeps its original coordinate and
/// is counted as ambiguous. A wrong coordinate confidently applied is worse than a coarse one
/// honestly kept.
/// </para>
/// <para>
/// Idempotent, and safe to re-run: a place already sitting on its mapped centre is left untouched.
/// </para>
/// </remarks>
public static class RefinePlaceCoordinates
{
    public sealed record Command : ICommand<CoordinateRefinementSummary>;

    /// <param name="CandidateCount">Mapped town centres offered by the source.</param>
    /// <param name="PlaceCount">Cities and municipalities considered.</param>
    /// <param name="MovedCount">Places given a better coordinate.</param>
    /// <param name="UnchangedCount">
    /// Places already within <see cref="Handler.MinimumMoveMetres"/> of their mapped centre.
    /// </param>
    /// <param name="UnmatchedCount">
    /// Places with no same-named candidate inside the search radius. These keep the gazetteer's
    /// coordinate, and the figure is the size of the remaining uncertainty rather than a failure.
    /// </param>
    /// <param name="AmbiguousCount">
    /// Places with two comparably close same-named candidates, left untouched deliberately.
    /// </param>
    /// <param name="MeanMoveKilometres">Mean correction applied, over the moved places.</param>
    /// <param name="LargestMoveKilometres">Largest correction applied.</param>
    public sealed record CoordinateRefinementSummary(
        string SourceSlug,
        int CandidateCount,
        int PlaceCount,
        int MovedCount,
        int UnchangedCount,
        int UnmatchedCount,
        int AmbiguousCount,
        double MeanMoveKilometres,
        double LargestMoveKilometres);

    internal sealed class Handler(
        IApplicationDbContext context,
        ISettlementCoordinateSource settlementSource,
        TimeProvider timeProvider,
        ILogger<Handler> logger) : ICommandHandler<Command, CoordinateRefinementSummary>
    {
        /// <summary>
        /// Below this, the coordinate is left alone.
        /// </summary>
        /// <remarks>
        /// 250 m is inside the built-up area of any Philippine poblacion, so a move smaller than
        /// this changes no answer the platform gives while still marking every row as updated and
        /// re-attributing its coordinate. Re-running the import should be quiet.
        /// </remarks>
        internal const double MinimumMoveMetres = 250d;

        /// <summary>
        /// How much closer the best candidate must be than the runner-up to be accepted.
        /// </summary>
        /// <remarks>
        /// A factor rather than a distance, because the tolerable error scales with how far the
        /// gazetteer's point already is. Two same-named towns 30 km and 35 km away are not
        /// distinguishable by proximity and the place is left alone; one at 4 km against one at
        /// 30 km plainly is.
        /// </remarks>
        internal const double AmbiguityFactor = 2d;

        private static readonly Error SourceNotRegistered = new(
            ErrorType.NotFound,
            "coordinate_refinement.source_not_registered",
            "The settlement gazetteer is not registered. Reference data must be seeded before ingestion runs.");

        public async Task<Result<CoordinateRefinementSummary>> Handle(
            Command request,
            CancellationToken cancellationToken)
        {
            var now = timeProvider.GetUtcNow();

            var source = await context.DataSources
                .FirstOrDefaultAsync(
                    candidate => candidate.Slug == settlementSource.SourceSlug,
                    cancellationToken);

            if (source is null)
            {
                return Result<CoordinateRefinementSummary>.Failure(SourceNotRegistered);
            }

            if (!source.IsRedistributable)
            {
                // The same gate the place import applies. A coordinate is data, and storing one
                // from a source whose terms do not permit storage is the thing this platform
                // refuses to do anywhere else.
                return Result<CoordinateRefinementSummary>.Failure(PlaceImportErrors.SourceNotRedistributable);
            }

            var candidates = await settlementSource.FetchAsync(cancellationToken);

            var byName = candidates
                .GroupBy(candidate => Normalise(candidate.Name), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

            var places = await context.Places
                .Where(place => place.Kind == PlaceKind.City || place.Kind == PlaceKind.Municipality)
                .ToListAsync(cancellationToken);

            var factory = new GeometryFactory(new PrecisionModel(), 4326);
            var radiusMetres = settlementSource.MatchRadiusKm * 1000d;

            var moves = new List<double>();
            var unchanged = 0;
            var unmatched = 0;
            var ambiguous = 0;

            foreach (var place in places)
            {
                if (!byName.TryGetValue(Normalise(place.Name), out var named))
                {
                    unmatched++;
                    continue;
                }

                var ranked = named
                    .Select(candidate => (candidate, metres: DistanceMetres(
                        place.Latitude, place.Longitude, candidate.Latitude, candidate.Longitude)))
                    .Where(pair => pair.metres <= radiusMetres)
                    .OrderBy(pair => pair.metres)
                    .ToArray();

                if (ranked.Length == 0)
                {
                    unmatched++;
                    continue;
                }

                if (ranked.Length > 1 && ranked[1].metres < ranked[0].metres * AmbiguityFactor)
                {
                    ambiguous++;
                    continue;
                }

                var (best, metres) = ranked[0];

                if (metres < MinimumMoveMetres)
                {
                    unchanged++;
                    continue;
                }

                // X is longitude, Y is latitude — the order NetTopologySuite uses and the one the
                // rest of this codebase writes, which is the reverse of how the figures are read
                // aloud.
                place.SetCoordinate(
                    factory.CreatePoint(new Coordinate(best.Longitude, best.Latitude)),
                    source.Id,
                    now);

                moves.Add(metres / 1000d);
            }

            await context.SaveChangesAsync(cancellationToken);

            var summary = new CoordinateRefinementSummary(
                settlementSource.SourceSlug,
                candidates.Count,
                places.Count,
                moves.Count,
                unchanged,
                unmatched,
                ambiguous,
                moves.Count == 0 ? 0d : Math.Round(moves.Average(), 2),
                moves.Count == 0 ? 0d : Math.Round(moves.Max(), 1));

            RefinePlaceCoordinatesLog.Completed(
                logger,
                summary.MovedCount,
                summary.PlaceCount,
                summary.MeanMoveKilometres,
                summary.LargestMoveKilometres,
                summary.UnmatchedCount,
                summary.AmbiguousCount);

            return Result<CoordinateRefinementSummary>.Success(summary);
        }

        /// <summary>
        /// Reduces a name to what the two sources can be expected to agree on.
        /// </summary>
        /// <remarks>
        /// The gazetteer publishes official names — <c>City of Tandag</c>, <c>Tandag City</c> — while
        /// the map carries the everyday one. Diacritics and the Spanish forms that survive in
        /// Philippine place names are folded too: <c>Peñaranda</c> against <c>Penaranda</c> is the
        /// same town, and matching them by hand is not maintainable across 1,647 rows.
        /// </remarks>
        private static string Normalise(string name)
        {
            var folded = name
                .Trim()
                .ToLowerInvariant()
                .Replace("ñ", "n", StringComparison.Ordinal)
                .Replace("’", "'", StringComparison.Ordinal);

            foreach (var prefix in (string[])["city of ", "municipality of ", "town of "])
            {
                if (folded.StartsWith(prefix, StringComparison.Ordinal))
                {
                    folded = folded[prefix.Length..];
                }
            }

            if (folded.EndsWith(" city", StringComparison.Ordinal))
            {
                folded = folded[..^" city".Length];
            }

            return folded.Replace(".", string.Empty, StringComparison.Ordinal).Trim();
        }

        /// <summary>
        /// Great-circle distance, in metres.
        /// </summary>
        /// <remarks>
        /// Computed here rather than in the database because the whole comparison is 1,647 places
        /// against 1,695 candidates grouped by name, which is a few thousand arithmetic operations
        /// in memory against as many round trips. The haversine formula on a spherical Earth is
        /// accurate to about 0.3% — metres over these distances — and the decision it feeds is a
        /// kilometre-scale threshold.
        /// </remarks>
        private static double DistanceMetres(
            double fromLatitude,
            double fromLongitude,
            double toLatitude,
            double toLongitude)
        {
            const double earthRadiusMetres = 6_371_000d;

            var latitudeDelta = ToRadians(toLatitude - fromLatitude);
            var longitudeDelta = ToRadians(toLongitude - fromLongitude);

            var a = (Math.Sin(latitudeDelta / 2d) * Math.Sin(latitudeDelta / 2d))
                + (Math.Cos(ToRadians(fromLatitude))
                    * Math.Cos(ToRadians(toLatitude))
                    * Math.Sin(longitudeDelta / 2d)
                    * Math.Sin(longitudeDelta / 2d));

            return earthRadiusMetres * 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
        }

        private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
    }
}

internal static partial class RefinePlaceCoordinatesLog
{
    [LoggerMessage(
        EventId = 6310,
        Level = LogLevel.Information,
        Message = "Refined {MovedCount} of {PlaceCount} place coordinates (mean {MeanKm} km, largest {LargestKm} km); "
            + "{UnmatchedCount} unmatched, {AmbiguousCount} ambiguous and left unchanged")]
    public static partial void Completed(
        ILogger logger,
        int movedCount,
        int placeCount,
        double meanKm,
        double largestKm,
        int unmatchedCount,
        int ambiguousCount);
}
