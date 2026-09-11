using Calametra.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Calametra.Infrastructure.Persistence.Configurations;

internal sealed class HazardEventConfiguration : IEntityTypeConfiguration<HazardEvent>
{
    public void Configure(EntityTypeBuilder<HazardEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("hazard_events");

        builder.HasKey(hazardEvent => hazardEvent.Id);

        builder.Property(hazardEvent => hazardEvent.Type)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(hazardEvent => hazardEvent.CanonicalEpicenter)
            .HasColumnType(PostgisTypes.Point)
            .IsRequired();

        builder.Property(hazardEvent => hazardEvent.CanonicalOccurredAt).IsRequired();

        // Nullable: most earthquakes have no name. 96 characters is generous for a cyclone
        // name and leaves room for a compound form should a local name ever be stored beside
        // the international one.
        builder.Property(hazardEvent => hazardEvent.Name).HasMaxLength(96);

        // The national authority's name, held separately because it comes from a different
        // naming body than Name does.
        builder.Property(hazardEvent => hazardEvent.LocalName).HasMaxLength(96);

        // The crosswalk matches on name and season together, because international names are
        // reused: MERANTI appears in both 2010 and 2016, GONI in 2015 and 2020. Matching on
        // name alone would attach the wrong local name to the wrong storm.
        builder.HasIndex(hazardEvent => new { hazardEvent.Type, hazardEvent.Name });

        // Timeline scrubbing and the density histogram both scan by time within a
        // hazard type, so the composite index leads with type.
        builder.HasIndex(hazardEvent => new { hazardEvent.Type, hazardEvent.CanonicalOccurredAt });

        // GiST index: every radius search and bbox filter depends on this.
        builder.HasIndex(hazardEvent => hazardEvent.CanonicalEpicenter).HasMethod("gist");

        builder.HasMany(hazardEvent => hazardEvent.Observations)
            .WithOne()
            .HasForeignKey(observation => observation.HazardEventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(hazardEvent => hazardEvent.TrackPoints)
            .WithOne()
            .HasForeignKey(trackPoint => trackPoint.HazardEventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(hazardEvent => hazardEvent.Observations)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Navigation(hazardEvent => hazardEvent.TrackPoints)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Computed from the observation collection, never persisted.
        builder.Ignore(hazardEvent => hazardEvent.PreferredObservation);
        builder.Ignore(hazardEvent => hazardEvent.HasMultipleObservations);
        builder.Ignore(hazardEvent => hazardEvent.HasMagnitudeDisagreement);
        builder.Ignore(hazardEvent => hazardEvent.DomainEvents);
    }
}
