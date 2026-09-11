using System.Globalization;

namespace Calametra.Domain.Geospatial;

/// <summary>
/// A position stated relative to a named place: "38 km ENE of Surigao City".
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a value object rather than a formatted string built where it is needed.</b> The
/// wording carries three claims — a distance, a direction and a place — and each has to be
/// qualified in the same way everywhere it appears. A description assembled per feature would
/// eventually disagree with itself about whether the distance is to the town's boundary or to a
/// point, which is the distinction that makes the phrase honest.
/// </para>
/// <para>
/// <b>The distance is to a representative point, not to the town.</b> No boundary is stored for
/// Philippine local government units, so this is the separation between the epicentre and the
/// gazetteer's single coordinate for the place. For a large municipality the edge of the built-up
/// area may be tens of kilometres nearer. This is the same convention the USGS uses for its own
/// <c>place</c> field, which is also computed against gazetteer points.
/// </para>
/// <para>
/// <b>Sixteen points, not eight.</b> Eight would round a bearing by up to 22.5°, which at 100 km
/// displaces the implied position by nearly 40 km — enough to put an epicentre on the wrong side
/// of an island. Sixteen halves that, and matches the convention a reader has already seen on
/// agency bulletins.
/// </para>
/// </remarks>
/// <param name="PlaceName">The named place, e.g. <c>Surigao City</c>.</param>
/// <param name="ContainingPlaceName">
/// The province, where known. Carried because 111 municipality names in this country are shared
/// by more than one place, so a bare name can name the wrong town.
/// </param>
/// <param name="DistanceKm">Separation from the place's representative point.</param>
/// <param name="BearingDegrees">Initial bearing from the place to the position, clockwise from north.</param>
public sealed record RelativeLocation(
    string PlaceName,
    string? ContainingPlaceName,
    double DistanceKm,
    double BearingDegrees)
{
    /// <summary>
    /// The sixteen-point compass, indexed by bearing.
    /// </summary>
    /// <remarks>
    /// Ordered from north clockwise, so the index is the bearing divided by 22.5.
    /// </remarks>
    private static readonly string[] CompassPoints =
    [
        "N", "NNE", "NE", "ENE",
        "E", "ESE", "SE", "SSE",
        "S", "SSW", "SW", "WSW",
        "W", "WNW", "NW", "NNW",
    ];

    /// <summary>The compass point the bearing falls in, e.g. <c>ENE</c>.</summary>
    public string Direction => CompassPointFor(BearingDegrees);

    /// <summary>
    /// The phrase, without the containing province.
    /// </summary>
    /// <remarks>
    /// Distances below 1 km are stated as "at", because "0.4 km NNE of" claims a precision the
    /// epicentre does not have — agencies routinely disagree about a position by more than that,
    /// and the 2017 Surigao event's two epicentres are 2.54 km apart.
    /// </remarks>
    public string Describe() =>
        DistanceKm < 1d
            ? $"At {PlaceName}"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{DistanceKm:0} km {Direction} of {PlaceName}");

    /// <summary>The phrase with the province appended, for lists where names may repeat.</summary>
    public string DescribeWithContainer() =>
        ContainingPlaceName is null ? Describe() : $"{Describe()}, {ContainingPlaceName}";

    /// <summary>
    /// Builds a description from two coordinates.
    /// </summary>
    /// <param name="placeLatitude">Latitude of the place's representative point.</param>
    /// <param name="placeLongitude">Longitude of the place's representative point.</param>
    /// <param name="latitude">Latitude of the position being described.</param>
    /// <param name="longitude">Longitude of the position being described.</param>
    public static RelativeLocation Between(
        string placeName,
        string? containingPlaceName,
        double placeLatitude,
        double placeLongitude,
        double latitude,
        double longitude) =>
        new(
            placeName,
            containingPlaceName,
            GeoDistance.HaversineKm(placeLatitude, placeLongitude, latitude, longitude),
            // From the place to the position, which is the direction the phrase states: the
            // epicentre lies east-north-east *of* the town, not the other way round.
            GeoDistance.InitialBearingDegrees(placeLatitude, placeLongitude, latitude, longitude));

    private static string CompassPointFor(double bearingDegrees)
    {
        var normalised = ((bearingDegrees % 360d) + 360d) % 360d;

        // Rounding rather than truncating, so a bearing sits in the sector it is nearest to:
        // 349° is N, not NNW.
        var index = (int)Math.Round(normalised / 22.5d, MidpointRounding.AwayFromZero) % 16;

        return CompassPoints[index];
    }
}
