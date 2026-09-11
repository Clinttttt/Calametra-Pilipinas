using Calametra.Domain.Meteorology;

namespace Calametra.Application.Abstractions.Sources;

/// <summary>
/// One agency's fix on a cyclone at one moment, as reported upstream.
/// </summary>
/// <remarks>
/// Wind arrives already wrapped in its <see cref="WindReading"/>, so the adapter is where the
/// source's averaging convention is recognised and recorded. IBTrACS states the interval only
/// in its documentation, not in the data — the column name is the sole clue that
/// <c>USA_WIND</c> is a one-minute mean and <c>TOKYO_WIND</c> a ten-minute one. Resolving that
/// in the adapter means no later layer has to know it.
/// </remarks>
public sealed record CatalogCycloneFix
{
    /// <summary>Slug of the agency that produced this fix, matching a registered data source.</summary>
    public required string SourceSlug { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }

    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    /// <summary>Wind with its averaging period, or null when this agency reported none.</summary>
    public WindReading? Wind { get; init; }

    public int? MinimumPressureMillibars { get; init; }

    /// <summary>Storm nature or intensity class, verbatim from the source.</summary>
    public string? Classification { get; init; }

    public double? DistanceToLandKm { get; init; }

    public bool IsLandfall { get; init; }

    /// <summary>Radius of maximum wind in nautical miles — the eyewall.</summary>
    public double? RadiusOfMaximumWindNm { get; init; }

    /// <summary>Radius of the outermost closed isobar in nautical miles.</summary>
    public double? RadiusOutermostIsobarNm { get; init; }

    /// <summary>
    /// The gale field, in whichever geometry this agency publishes.
    /// </summary>
    /// <remarks>
    /// Carries its own threshold and shape because the two differ by agency: JTWC publishes four
    /// quadrant radii at 34 knots, JMA and KMA an ellipse at 30. Resolved in the adapter so no
    /// later layer has to know which convention a source follows.
    /// </remarks>
    public WindField GaleField { get; init; }

    /// <summary>Storm-force (50 kt) radii per quadrant. JTWC only.</summary>
    public WindField StormField { get; init; }

    /// <summary>Hurricane-force (64 kt) radii per quadrant. JTWC only.</summary>
    public WindField HurricaneField { get; init; }
}

/// <summary>
/// A cyclone and every agency fix along its track.
/// </summary>
/// <remarks>
/// One storm, many fixes, and several fixes per timestamp — one per agency that analysed it.
/// The duplication is the payload, not noise: it is what lets the platform show that four
/// agencies described the same storm at four different intensities.
/// </remarks>
public sealed record CatalogCyclone
{
    /// <summary>IBTrACS storm identifier, e.g. <c>2013306N07162</c> for Haiyan.</summary>
    public required string ExternalId { get; init; }

    /// <summary>
    /// International name, or "UNNAMED".
    /// </summary>
    /// <remarks>
    /// The international name only. Philippine storms also carry a PAGASA local name — Haiyan
    /// was Yolanda here — and IBTrACS does not hold it. Presenting only this name to a
    /// Philippine audience is a known gap, recorded rather than hidden.
    /// </remarks>
    public required string Name { get; init; }

    /// <summary>Cyclone season, which is the calendar year in the western North Pacific.</summary>
    public required int Season { get; init; }

    /// <summary>
    /// First fix time across all agencies, used as the event's canonical time.
    /// </summary>
    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset EndedAt { get; init; }

    /// <summary>Fixes in time order, with all agencies interleaved.</summary>
    public required IReadOnlyList<CatalogCycloneFix> Fixes { get; init; }
}

/// <summary>Streams cyclone tracks from an upstream best-track archive.</summary>
public interface ICycloneTrackSource
{
    /// <summary>Slug of the archive itself, for provenance on the event.</summary>
    string SourceSlug { get; }

    /// <summary>
    /// Streams storms whose track enters the Philippine area of interest.
    /// </summary>
    /// <remarks>
    /// Asynchronous streaming rather than a list, because the western Pacific file is over
    /// 100 MB and must never be materialised. Storms are yielded as they complete, so memory
    /// stays bounded to one storm's fixes.
    /// </remarks>
    IAsyncEnumerable<CatalogCyclone> StreamAsync(
        int fromSeason,
        CancellationToken cancellationToken);
}
