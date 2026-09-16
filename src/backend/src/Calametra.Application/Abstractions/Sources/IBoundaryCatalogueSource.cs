namespace Calametra.Application.Abstractions.Sources;

/// <summary>
/// One unit as an external, register-keyed boundary dataset lists it.
/// </summary>
/// <param name="CanonicalCode">
/// The ten-digit PSGC code the dataset carries, normalised from whatever prefix its own convention uses.
/// </param>
/// <param name="StatedAreaSquareKm">
/// The dataset's own area figure. Kept so this platform's computation can be checked against the
/// publisher's rather than trusted blindly.
/// </param>
public sealed record BoundaryCatalogueUnit(
    string CanonicalCode,
    string? Name,
    double StatedAreaSquareKm);

/// <summary>
/// Reads the <em>unit list</em> of a boundary dataset without reading its geometry.
/// </summary>
/// <remarks>
/// <para>
/// A separate port from <see cref="ILguBoundarySource"/> on purpose. Identity is settled before geometry is
/// imported — that ordering is the whole of ADR-005 — and the attribute table is a few hundred kilobytes
/// beside hundreds of megabytes of coordinates, so the edition correspondence can be proposed and reviewed
/// without paying for the polygons or storing anything at all.
/// </para>
/// <para>
/// It also keeps the review honest. A reviewer deciding which unit a legacy code means should be looking at
/// codes and names, not at a map that makes a wrong answer look convincing.
/// </para>
/// </remarks>
public interface IBoundaryCatalogueSource
{
    Task<IReadOnlyList<BoundaryCatalogueUnit>> ReadUnitsAsync(CancellationToken cancellationToken);
}
