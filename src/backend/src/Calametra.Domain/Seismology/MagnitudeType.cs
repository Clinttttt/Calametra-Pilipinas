namespace Calametra.Domain.Seismology;

/// <summary>
/// The magnitude scale a reported value was measured on.
/// </summary>
/// <remarks>
/// This exists because magnitude is not a single quantity. Measured against the
/// USGS catalogue for the CARAGA bounding box (2015-01-01 to 2026-09-01, M4.0+,
/// 1,991 events) the distribution is:
/// <list type="bullet">
///   <item><description><c>mb</c> — 1,874 events (94.1%)</description></item>
///   <item><description><c>mww</c> — 116 events (5.8%)</description></item>
///   <item><description><c>mwb</c> — 1 event</description></item>
/// </list>
/// <c>mb</c> saturates around M6.0–6.5 and diverges systematically from moment
/// magnitude, so comparing an <c>mb</c> value against an <c>mww</c> value as
/// though they were the same number is a scientific error. Because the catalogue
/// is 94% <c>mb</c> while every large event is <c>mww</c>, similarity search that
/// ignores scale would compare the flagship events against a body-wave
/// population — which is precisely the mistake this type prevents.
/// </remarks>
public enum MagnitudeType
{
    /// <summary>Scale not reported by the source. Never comparable to anything.</summary>
    Unknown = 0,

    /// <summary>Short-period body-wave magnitude. Dominant in the CARAGA catalogue.</summary>
    Mb = 1,

    /// <summary>Short-period body-wave magnitude, Lg phase.</summary>
    MbLg = 2,

    /// <summary>Surface-wave magnitude. PHIVOLCS reports the 2017 Surigao event as Ms 6.7.</summary>
    Ms = 10,

    /// <summary>Surface-wave magnitude, 20-second period, vertical component.</summary>
    MsZ = 11,

    /// <summary>Local (Richter) magnitude.</summary>
    Ml = 20,

    /// <summary>Duration magnitude.</summary>
    Md = 21,

    /// <summary>Ad-hoc / human-reviewed local magnitude.</summary>
    Mh = 22,

    /// <summary>Moment magnitude, scale unspecified by the source.</summary>
    Mw = 30,

    /// <summary>Moment magnitude from W-phase inversion. USGS reports 2017 Surigao as Mww 6.5.</summary>
    Mww = 31,

    /// <summary>Moment magnitude from body-wave inversion.</summary>
    Mwb = 32,

    /// <summary>Moment magnitude from centroid moment tensor.</summary>
    Mwc = 33,

    /// <summary>Moment magnitude from regional moment tensor.</summary>
    Mwr = 34,
}

/// <summary>
/// Groups magnitude types that measure the same physical quantity and may
/// therefore be compared numerically.
/// </summary>
public enum MagnitudeScaleFamily
{
    Unknown = 0,
    BodyWave = 1,
    SurfaceWave = 2,
    Local = 3,
    Moment = 4,
}

public static class MagnitudeTypeExtensions
{
    /// <summary>Maps a reported scale onto the family of quantities it belongs to.</summary>
    public static MagnitudeScaleFamily Family(this MagnitudeType type) => type switch
    {
        MagnitudeType.Mb or MagnitudeType.MbLg => MagnitudeScaleFamily.BodyWave,
        MagnitudeType.Ms or MagnitudeType.MsZ => MagnitudeScaleFamily.SurfaceWave,
        MagnitudeType.Ml or MagnitudeType.Md or MagnitudeType.Mh => MagnitudeScaleFamily.Local,
        MagnitudeType.Mw or MagnitudeType.Mww or MagnitudeType.Mwb
            or MagnitudeType.Mwc or MagnitudeType.Mwr => MagnitudeScaleFamily.Moment,
        _ => MagnitudeScaleFamily.Unknown,
    };

    /// <summary>
    /// Whether two reported magnitudes may be compared numerically.
    /// <see cref="MagnitudeScaleFamily.Unknown"/> is never comparable, including
    /// to itself: two values of unstated provenance tell us nothing.
    /// </summary>
    public static bool IsComparableWith(this MagnitudeType left, MagnitudeType right)
    {
        var leftFamily = left.Family();

        return leftFamily != MagnitudeScaleFamily.Unknown && leftFamily == right.Family();
    }

    /// <summary>The conventional display label, e.g. <c>Mww</c>.</summary>
    public static string Label(this MagnitudeType type) =>
        type == MagnitudeType.Unknown ? "M" : type.ToString();

    /// <summary>
    /// Parses the lower-case magnitude type strings used by the USGS FDSN event
    /// service and the PHIVOLCS bulletins. Unrecognised input becomes
    /// <see cref="MagnitudeType.Unknown"/> rather than throwing: an unfamiliar
    /// scale is a data-quality fact to record, not an ingestion failure.
    /// </summary>
    public static MagnitudeType Parse(string? reported) => reported?.Trim().ToLowerInvariant() switch
    {
        "mb" => MagnitudeType.Mb,
        "mb_lg" or "mblg" => MagnitudeType.MbLg,
        "ms" => MagnitudeType.Ms,
        "ms20" or "msz" => MagnitudeType.MsZ,
        "ml" => MagnitudeType.Ml,
        "md" => MagnitudeType.Md,
        "mh" => MagnitudeType.Mh,
        "mw" => MagnitudeType.Mw,
        "mww" => MagnitudeType.Mww,
        "mwb" => MagnitudeType.Mwb,
        "mwc" => MagnitudeType.Mwc,
        "mwr" => MagnitudeType.Mwr,
        _ => MagnitudeType.Unknown,
    };
}
