using Calametra.Domain.Administrative;

namespace Calametra.Application.Abstractions.Sources;

/// <summary>
/// One unit as the PSGC register publishes it.
/// </summary>
/// <remarks>
/// Both editions of the code are carried when the register states both, and that is the whole point:
/// where the register itself pairs the ten-digit and nine-digit forms, a crosswalk row can be confirmed
/// on <see cref="LguLinkEvidence.RegisterMatch"/> rather than on a re-slicing rule that fails in the
/// capital. <see cref="StatedHistoricalCode"/> is null where no pairing is published, which is precisely
/// the population that must go to manual review.
/// </remarks>
public sealed record RegisterUnit
{
    /// <summary>The current ten-digit PSGC code. The canonical identity.</summary>
    public required string CanonicalCode { get; init; }

    public required string Name { get; init; }

    public required LguLevel Level { get; init; }

    /// <summary>The parent unit's ten-digit code, or null at the coarsest level.</summary>
    public string? ParentCanonicalCode { get; init; }

    /// <summary>The nine-digit code the register itself states for this unit, where it states one.</summary>
    public string? StatedHistoricalCode { get; init; }
}

/// <summary>
/// A register edition as read, with the provenance that decides whether it can certify anything.
/// </summary>
/// <remarks>
/// The counts are what the adapter actually saw rather than what the publication claims, because that
/// is the only staleness signal that cannot be wrong: a register reporting 17 regions predates the 2024
/// creation of the Negros Island Region whatever its label says, and one showing an undivided
/// Maguindanao predates May 2022.
/// </remarks>
public sealed record RegisterSnapshot
{
    public required string Label { get; init; }

    public required RegisterProvenance Provenance { get; init; }

    public required string AccessRoute { get; init; }

    /// <summary>The transport's own last-modified stamp, where it reports one.</summary>
    public DateTimeOffset? UpstreamLastModified { get; init; }

    /// <summary>What the adapter observed about this edition. Rendered to readers verbatim.</summary>
    public string? Notes { get; init; }

    public required IReadOnlyList<RegisterUnit> Units { get; init; }
}

/// <summary>
/// Reads an edition of the Philippine Standard Geographic Code.
/// </summary>
/// <remarks>
/// A port rather than a concrete client because the register has more than one access route and they are
/// not equivalent: the PSA's own publication is the only one that satisfies ADR-005's first gate
/// condition, a third-party mirror can exercise the pipeline without certifying it, and an operator may
/// place a downloaded file on disk. The adapter states which of those it is; nothing downstream infers it.
/// </remarks>
public interface IPsgcRegisterSource
{
    /// <summary>The registered <c>DataSource</c> slug this reader belongs to.</summary>
    string SourceSlug { get; }

    Task<RegisterSnapshot> ReadAsync(CancellationToken cancellationToken);
}
