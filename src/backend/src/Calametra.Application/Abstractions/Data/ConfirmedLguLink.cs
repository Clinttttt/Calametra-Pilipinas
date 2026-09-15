namespace Calametra.Application.Abstractions.Data;

/// <summary>
/// A reviewed pairing between a canonical ten-digit PSGC unit and a historical nine-digit code.
/// </summary>
/// <remarks>
/// <para>
/// <b>This type is the read boundary.</b> It is mapped to the <c>lgu_code_links_confirmed</c> database
/// view, which selects only rows whose status is <c>Confirmed</c>, and it is the only crosswalk shape
/// reachable from <see cref="IApplicationDbContext"/>. A handler that derives a figure for a reader
/// therefore cannot join through a proposal — not because it was told not to, but because the base
/// table is not on the interface it holds.
/// </para>
/// <para>
/// ADR-005 D4 sets out why: a proposal is a work queue, not data. An unreviewed pairing has to behave
/// exactly as an absent one, which is the same treatment this platform gives an unmeasured depth — not
/// approximated, marked. The view is the first of three layers; database permissions and the boundary
/// tests are the other two, and they fail independently.
/// </para>
/// <para>
/// Read-only by construction: no setters, no <c>SaveChanges</c> path, and EF is configured to treat it
/// as a view so a write would not compile into SQL even if one were attempted.
/// </para>
/// </remarks>
public sealed class ConfirmedLguLink
{
    public Guid Id { get; private set; }

    public Guid LguId { get; private set; }

    /// <summary>The canonical ten-digit code, carried on the view so callers need no second join.</summary>
    public string CanonicalPsgcCode { get; private set; } = string.Empty;

    public string LguName { get; private set; } = string.Empty;

    /// <summary>The register's administrative level, as its name rather than a number.</summary>
    public string LguLevel { get; private set; } = string.Empty;

    /// <summary>The pre-2019 nine-digit code. An alias, never an identity.</summary>
    public string HistoricalPsgcCode { get; private set; } = string.Empty;

    /// <summary>The gazetteer row this pairing reaches, where one was matched.</summary>
    public Guid? PlaceId { get; private set; }

    /// <summary>What was checked: <c>RegisterMatch</c>, <c>DigitReslice</c> or <c>ManualReview</c>.</summary>
    public string Evidence { get; private set; } = string.Empty;

    /// <summary>Who is accountable for this pairing. Never null on a confirmed row.</summary>
    public string ReviewedBy { get; private set; } = string.Empty;

    public DateTimeOffset ReviewedAt { get; private set; }

    /// <summary>The reviewer's written reason. Present whenever the evidence is manual review.</summary>
    public string? Reason { get; private set; }

    /// <summary>The register edition this pairing was confirmed against.</summary>
    public Guid ConfirmedAgainstEditionId { get; private set; }
}
