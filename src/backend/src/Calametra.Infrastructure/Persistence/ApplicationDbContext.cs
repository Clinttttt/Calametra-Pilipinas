using Calametra.Application.Abstractions.Data;
using Calametra.Domain.Events;
using Calametra.Domain.Hazards;
using Calametra.Domain.Places;
using Calametra.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Infrastructure.Persistence;

/// <summary>
/// The EF Core context. Implements <see cref="IApplicationDbContext"/> so handlers
/// never name this type.
/// </summary>
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options), IApplicationDbContext
{
    /// <summary>PostGIS extension name, created by the initial migration.</summary>
    public const string PostgisExtension = "postgis";

    public DbSet<HazardEvent> HazardEvents => Set<HazardEvent>();

    public DbSet<EventObservation> EventObservations => Set<EventObservation>();

    public DbSet<EventTrackPoint> EventTrackPoints => Set<EventTrackPoint>();

    public DbSet<DataSource> DataSources => Set<DataSource>();

    public DbSet<HazardLayerDefinition> HazardLayers => Set<HazardLayerDefinition>();

    public DbSet<HazardFeature> HazardFeatures => Set<HazardFeature>();

    public DbSet<Place> Places => Set<Place>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasPostgresExtension(PostgisExtension);

        // Configurations live in Persistence/Configurations so this class holds no
        // mapping code and stays a composition root only.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        DeclareKeysAsDomainGenerated(modelBuilder);

        base.OnModelCreating(modelBuilder);
    }

    /// <summary>
    /// Tells EF Core that <c>Guid</c> primary keys are assigned by the domain, never
    /// by the database.
    /// </summary>
    /// <remarks>
    /// This is not a micro-optimisation; without it EF inserts the wrong statement.
    /// <para>
    /// EF infers <c>ValueGeneratedOnAdd</c> for <c>Guid</c> keys. Change detection then
    /// uses the key value to decide whether a newly-discovered entity is new: a
    /// non-default key means "this already exists", so it is marked <c>Modified</c>.
    /// Every Calametra entity assigns its own key in its factory via
    /// <c>Guid.CreateVersion7()</c>, so a genuinely new entity always arrives with a
    /// non-default key.
    /// </para>
    /// <para>
    /// That is harmless when a whole graph is added at the root — <c>Add(hazardEvent)</c>
    /// marks everything <c>Added</c> explicitly — but wrong when a child is attached to
    /// an aggregate that is already tracked. EF then issues
    /// <c>UPDATE event_observations SET ...</c> for a row that does not exist and fails
    /// with <c>DbUpdateConcurrencyException: expected to affect 1 row(s), but actually
    /// affected 0</c>, which names neither the entity nor the real cause.
    /// </para>
    /// <para>
    /// Applied here by convention rather than per configuration so a new entity cannot
    /// be added without it and quietly reintroduce the same defect.
    /// </para>
    /// </remarks>
    private static void DeclareKeysAsDomainGenerated(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var key = entityType.FindPrimaryKey();

            if (key?.Properties is not [{ ClrType: var clrType } property] || clrType != typeof(Guid))
            {
                continue;
            }

            property.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
        }
    }
}
