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
    DbSet<EventObservation> EventObservations { get; }

    /// <summary>Positions of events that move through time.</summary>
    DbSet<EventTrackPoint> EventTrackPoints { get; }

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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
