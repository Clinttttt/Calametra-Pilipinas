namespace Calametra.Domain.Geospatial;

/// <summary>
/// The study area: the Philippine archipelago.
/// </summary>
/// <remarks>
/// Kept in the domain because "is this event within scope" is a domain question, and
/// because ingestion, seeding, fault import and the default map camera must all agree
/// on one definition of the area.
/// <para>
/// Replaces the earlier CARAGA-only bounding box. The platform is national in scope,
/// but the region remains significant as a reference area — the 2017 Surigao
/// earthquake and the December 2023 Mindanao sequence are both there — so
/// <see cref="Caraga"/> is retained as a named sub-area for camera presets and
/// regional queries rather than as the ingestion boundary.
/// </para>
/// </remarks>
public static class PhilippineStudyArea
{
    public const string Name = "Philippines";

    /// <summary>
    /// Ingestion bounding box.
    /// </summary>
    /// <remarks>
    /// Deliberately wider than the land area. The Philippine and Manila Trenches and
    /// the Cotabato and Negros Trenches all lie offshore, and they produce much of
    /// the archipelago's seismicity — including the deep slab events that reach
    /// 667 km. Clipping to the coastline would discard exactly the events that
    /// explain why the country shakes.
    /// <para>
    /// Extends north past Batanes (~21.1°N) and south past Tawi-Tawi (~4.6°N), west
    /// past Palawan (~116.9°E) and east past the Philippine Trench axis (~127°E).
    /// </para>
    /// </remarks>
    public const double MinLatitude = 4.0;

    public const double MaxLatitude = 22.0;

    public const double MinLongitude = 115.5;

    public const double MaxLongitude = 128.0;

    /// <summary>Default map camera target, roughly the centre of the archipelago.</summary>
    public const double CentreLatitude = 12.5;

    public const double CentreLongitude = 122.5;

    public static bool Contains(double latitude, double longitude) =>
        latitude >= MinLatitude
        && latitude <= MaxLatitude
        && longitude >= MinLongitude
        && longitude <= MaxLongitude;

    /// <summary>
    /// Named sub-areas, for camera presets and regional filtering.
    /// </summary>
    /// <remarks>
    /// Not an administrative boundary set — these are viewport hints. Real
    /// administrative geometry belongs in the <c>places</c> table where it can carry
    /// a PSGC code and a boundary polygon.
    /// </remarks>
    public sealed record SubArea(
        string Name,
        double MinLatitude,
        double MaxLatitude,
        double MinLongitude,
        double MaxLongitude)
    {
        public double CentreLatitude => (MinLatitude + MaxLatitude) / 2d;

        public double CentreLongitude => (MinLongitude + MaxLongitude) / 2d;
    }

    /// <summary>Region XIII. Retained as the platform's principal reference area.</summary>
    public static readonly SubArea Caraga = new("CARAGA", 7.5, 10.5, 124.5, 127.0);

    public static readonly SubArea Luzon = new("Luzon", 12.5, 21.5, 119.5, 124.5);

    public static readonly SubArea Visayas = new("Visayas", 8.5, 13.0, 121.5, 126.5);

    public static readonly SubArea Mindanao = new("Mindanao", 4.5, 10.5, 121.5, 127.0);

    public static readonly IReadOnlyList<SubArea> SubAreas = [Luzon, Visayas, Mindanao, Caraga];
}
