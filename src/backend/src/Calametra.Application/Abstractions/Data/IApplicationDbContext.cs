using Calametra.Domain.Administrative;
using Calametra.Domain.Events;
using Calametra.Domain.Hazards;
using Calametra.Domain.Places;
using Calametra.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Application.Abstractions.Data;

/// <summary>
/// The persistence surface available to handlers.
/// </summary>
/// <remarks>
/// This interface takes a deliberate <c>Microsoft.EntityFrameworkCore</c> dependency
/// so handlers keep full LINQ composition, projection and <c>AsNoTracking()</c>
/// (reference architecture §10, Option A). What remains true — and is what actually
/// matters — is that this project knows nothing about <b>which</b> database is behind
/// it: no connection strings, no provider, no migrations, no Npgsql.
/// <para>
/// Registered as a scoped factory that returns the same <c>ApplicationDbContext</c>
/// instance, never as a second <c>AddDbContext</c>. Two registrations would mean two
/// change trackers per request, and writes through one would be invisible to the other.
/// </para>
/// </remarks>
public interface IApplicationDbContext
{
    /// <summary>Real-world hazard phenomena.</summary>
    DbSet<HazardEvent> HazardEvents { get; }

    /// <summary>Per-agency readings of those phenomena.</summary>
    DbSet<EarthquakeObservation> EarthquakeObservations { get; }

    /// <summary>Positions of events that move through time.</summary>
    DbSet<CycloneTrackPoint> CycloneTrackPoints { get; }

    /// <summary>Upstream dataset provenance, licensing and coverage limits.</summary>
    DbSet<DataSource> DataSources { get; }

    /// <summary>Catalogue of displayable hazard layers.</summary>
    DbSet<HazardLayerDefinition> HazardLayers { get; }

    /// <summary>
    /// Locally stored hazard geometry. Populated only for sources that permit
    /// redistribution; proxied layers have no rows here.
    /// </summary>
    DbSet<HazardFeature> HazardFeatures { get; }

    /// <summary>Administrative places used as anchors for location history.</summary>
    DbSet<Place> Places { get; }

    /// <summary>Canonical local government units, keyed on the current ten-digit PSGC code.</summary>
    DbSet<Lgu> Lgus { get; }

    /// <summary>Register editions loaded, so a figure can state which one it was reconciled to.</summary>
    DbSet<PsgcRegisterEdition> PsgcRegisterEditions { get; }

    /// <summary>
    /// Reviewed code pairings only.
    /// </summary>
    /// <remarks>
    /// Mapped to the <c>lgu_code_links_confirmed</c> view. The base table is deliberately absent from
    /// this interface: ADR-005 D4 requires that no figure rendered to a reader can be derived from an
    /// unreviewed proposal, and the cheapest way to guarantee that is to make the proposal unreachable
    /// from the surface analytics hold. Proposals live on <see cref="ILguCrosswalkReviewContext"/>.
    /// </remarks>
    DbSet<ConfirmedLguLink> ConfirmedLguLinks { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
