using Calametra.Application.Abstractions.Data;
using Calametra.Domain.Geospatial;
using Calametra.Domain.Places;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Features.Places.Shared;

/// <summary>
/// Names positions after the nearest city or municipality.
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole gazetteer is loaded once and the nearest is found in memory.</b> 1,647 cities and
/// municipalities, each a name and two doubles — a few hundred kilobytes, and one indexed table
/// scan. The alternative is a nearest-neighbour probe per position, which is what a lateral
/// <c>ORDER BY centroid &lt;-&gt; epicentre LIMIT 1</c> would do: correct, but a spatial query per
/// row, and the panels that need names ask for ten at a time. Once the candidates are in hand,
/// distance and bearing are pure arithmetic in the domain, where they are unit-tested. This is the
/// same division the cross-section and the similarity search already use.
/// </para>
/// <para>
/// <b>Cities and municipalities only.</b> A region or a province is an area, and its gazetteer
/// coordinate is a notional centre that names nothing a reader can picture — "180 km NNE of
/// Central Visayas" is not a location. Provinces are still used, as the container that
/// disambiguates the municipality.
/// </para>
/// <para>
/// <b>Beyond 300 km nothing is named, and the figure is measured rather than chosen.</b> Across
/// all 27,241 catalogued earthquakes the nearest city or municipality is within 50 km for 15,217
/// of them, within 100 km for 21,597, within 200 km for 26,195 and within 300 km for 27,175. Only
/// 66 lie further, out to 634 km — deep-ocean events east of the Philippine Trench. For those,
/// naming a town would describe the reach of the gazetteer rather than the position of the
/// earthquake, so the coordinates stand alone.
/// </para>
/// </remarks>
internal sealed class NearestPlaceLocator
{
    /// <summary>Beyond this, a place name says more about the directory than about the event.</summary>
    public const double MaximumNamingDistanceKm = 300d;

    private readonly PlacePoint[] _places;

    private NearestPlaceLocator(PlacePoint[] places) => _places = places;

    /// <summary>True when the directory has not been imported, so nothing can be named.</summary>
    public bool IsEmpty => _places.Length == 0;

    /// <summary>
    /// Loads the gazetteer.
    /// </summary>
    /// <remarks>
    /// Built per request rather than cached in a singleton. The directory is re-importable and a
    /// stale cache would keep naming places from a superseded import — and the read is one scan of
    /// a table small enough that caching buys nothing worth that risk.
    /// </remarks>
    public static async Task<NearestPlaceLocator> LoadAsync(
        IApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        var points = await context.Places.AsNoTracking()
            .Where(place => place.Kind == PlaceKind.City || place.Kind == PlaceKind.Municipality)
            .Join(
                context.Places.AsNoTracking(),
                place => place.ParentPlaceId,
                parent => (Guid?)parent.Id,
                (place, parent) => new PlacePoint(
                    place.Name,
                    parent.Name,
                    // The materialised columns, not the geometry: the column is geography and
                    // PostGIS has no ST_Y for it, and parsing 1,647 point objects per request
                    // measured at roughly 55 ms for a value that is two doubles.
                    place.Latitude,
                    place.Longitude))
            .ToArrayAsync(cancellationToken);

        return new NearestPlaceLocator(points);
    }

    /// <summary>
    /// The nearest city or municipality to a position, or null when none is near enough.
    /// </summary>
    public RelativeLocation? Describe(double latitude, double longitude)
    {
        PlacePoint? nearest = null;
        var nearestKm = double.MaxValue;

        foreach (var place in _places)
        {
            var km = GeoDistance.HaversineKm(place.Latitude, place.Longitude, latitude, longitude);

            if (km < nearestKm)
            {
                nearestKm = km;
                nearest = place;
            }
        }

        if (nearest is null || nearestKm > MaximumNamingDistanceKm)
        {
            return null;
        }

        return RelativeLocation.Between(
            nearest.Name,
            nearest.Container,
            nearest.Latitude,
            nearest.Longitude,
            latitude,
            longitude);
    }

    private sealed record PlacePoint(
        string Name,
        string? Container,
        double Latitude,
        double Longitude);
}
