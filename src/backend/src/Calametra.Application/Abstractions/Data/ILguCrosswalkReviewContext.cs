using Calametra.Domain.Administrative;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Abstractions.Data;

/// <summary>
/// The crosswalk <b>review</b> surface. Deliberately not the analytics surface.
/// </summary>
/// <remarks>
/// <para>
/// This is the only interface in the application through which an unreviewed pairing is reachable, and
/// it exists so that reachability is a deliberate choice rather than an accident of having one large
/// context interface. Three kinds of handler take it: the matcher that writes proposals, the review
/// commands that confirm or reject them, and the readiness report that counts them.
/// </para>
/// <para>
/// <b>Nothing that renders a figure to a reader may take this interface.</b> That is the rule ADR-005 D4
/// asks for, and separating the interfaces is what makes it checkable: an architecture test can assert
/// that no query handler outside the crosswalk feature depends on it, which is a much stronger statement
/// than "no query joins to that table".
/// </para>
/// <para>
/// Registered against the same <c>ApplicationDbContext</c> instance as
/// <see cref="IApplicationDbContext"/>, never as a second context. Two change trackers per request would
/// mean writes through one were invisible to the other.
/// </para>
/// </remarks>
public interface ILguCrosswalkReviewContext
{
    /// <summary>Register editions this platform has loaded, whatever their provenance.</summary>
    DbSet<PsgcRegisterEdition> PsgcRegisterEditions { get; }

    /// <summary>Canonical units as the register defines them.</summary>
    DbSet<Lgu> Lgus { get; }

    /// <summary>
    /// Every pairing in every state, including proposals and rejections.
    /// </summary>
    /// <remarks>
    /// Rejections are retained rather than deleted: the proportion of proposals a reviewer refused is a
    /// required figure in the readiness report, because if a matcher proposes 1,600 pairings and 40 are
    /// wrong, that number is the justification for the review gate existing.
    /// </remarks>
    DbSet<LguCodeLink> LguCodeLinks { get; }

    /// <summary>
    /// Written records that one side of the crosswalk has no counterpart, with the reason each.
    /// </summary>
    /// <remarks>
    /// On the review surface because accepting an exception is a review decision, and because gate 3 —
    /// "the unmatched set is enumerated and accepted" — is answered by counting these against the units
    /// and directory rows that carry no confirmed pairing.
    /// </remarks>
    DbSet<LguCrosswalkException> LguCrosswalkExceptions { get; }

    /// <summary>
    /// Every version of every unit's outline, in force or superseded.
    /// </summary>
    /// <remarks>
    /// On the review surface because the import matches polygons to units through the confirmed crosswalk,
    /// and because a superseded boundary is retained: analytics ask for the outline in force, which is a
    /// narrower question than "every boundary ever recorded".
    /// </remarks>
    DbSet<LguBoundary> LguBoundaries { get; }

    /// <summary>
    /// Dated, hashed acquisitions of boundary geometry.
    /// </summary>
    /// <remarks>
    /// One row per extract, pointed at by every outline read from it — the same relationship
    /// <see cref="PsgcRegisterEdition"/> has with the units read from it, and for the same reason.
    /// </remarks>
    DbSet<LguBoundaryExtract> LguBoundaryExtracts { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
