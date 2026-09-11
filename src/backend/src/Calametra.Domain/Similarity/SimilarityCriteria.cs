using Calametra.Domain.Seismology;

namespace Calametra.Domain.Similarity;

/// <summary>
/// The tolerances that define "similar" for a historical event search.
/// </summary>
/// <remarks>
/// Every field is user-visible and user-adjustable. Calametra never presents an
/// unexplained similarity score, so the criteria that produced a result set travel
/// with that result set.
/// <para>
/// Defaults were chosen against measured catalogue behaviour. Searching from the
/// 2017 Surigao event (Mww 6.5) in the USGS catalogue returns 4 matches at
/// 150 km / ±0.5, and 13 matches at 150 km / ±1.0 — enough to populate a panel
/// without loosening the window to the point where results stop being meaningful.
/// </para>
/// </remarks>
public sealed record SimilarityCriteria
{
    public static readonly SimilarityCriteria Default = new();

    /// <summary>Maximum epicentral separation, in kilometres.</summary>
    public double MaxDistanceKm { get; init; } = 150d;

    /// <summary>Maximum absolute magnitude difference.</summary>
    public double MaxMagnitudeDelta { get; init; } = 0.5d;

    /// <summary>Maximum absolute depth difference in kilometres. Null disables the depth term.</summary>
    public double? MaxDepthDeltaKm { get; init; } = 25d;

    /// <summary>
    /// When true (the default), candidates must report magnitude on a comparable
    /// scale family. Turning this off produces numerically larger result sets that
    /// silently mix body-wave and moment magnitudes, so the UI must warn when it
    /// is disabled.
    /// </summary>
    public bool RequireComparableMagnitudeScale { get; init; } = true;

    /// <summary>
    /// When true, candidates whose depth was fixed to an agency default are
    /// excluded from the depth term rather than compared as though the value were
    /// measured.
    /// </summary>
    public bool ExcludeOperatorAssignedDepths { get; init; } = true;

    /// <summary>Maximum number of matches to return.</summary>
    public int Limit { get; init; } = 10;
}
