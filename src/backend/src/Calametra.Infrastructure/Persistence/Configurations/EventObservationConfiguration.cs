using Calametra.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Calametra.Infrastructure.Persistence.Configurations;

internal sealed class EventObservationConfiguration : IEntityTypeConfiguration<EventObservation>
{
    public void Configure(EntityTypeBuilder<EventObservation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("event_observations");

        builder.HasKey(observation => observation.Id);

        builder.Property(observation => observation.ExternalEventId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(observation => observation.Epicenter)
            .HasColumnType(PostgisTypes.Point)
            .IsRequired();

        builder.Property(observation => observation.MagnitudeScale)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(observation => observation.DepthQuality)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(observation => observation.SourceUrl).HasMaxLength(512);

        // One reading per source per event. A repeat report is a revision, which is
        // why the domain rejects a second observation from the same source.
        builder.HasIndex(observation => new { observation.HazardEventId, observation.DataSourceId })
            .IsUnique();

        // Ingestion reconciles by the source's own identifier, so this must be fast
        // and must not permit two rows for the same upstream record.
        builder.HasIndex(observation => new { observation.DataSourceId, observation.ExternalEventId })
            .IsUnique();

        builder.HasIndex(observation => observation.Epicenter).HasMethod("gist");

        // Magnitude and depth filtering happen together constantly: the historical
        // explorer sliders, similarity search, and the cross-section all use them.
        builder.HasIndex(observation => new { observation.MagnitudeValue, observation.MagnitudeScale });

        // The cross-section excludes agency-assigned depths, so that predicate is
        // indexed rather than scanned.
        builder.HasIndex(observation => new { observation.DepthQuality, observation.DepthKilometres });

        builder.HasOne<Domain.Sources.DataSource>()
            .WithMany()
            .HasForeignKey(observation => observation.DataSourceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Projections over the mapped primitives; the value objects are derived.
        builder.Ignore(observation => observation.Magnitude);
        builder.Ignore(observation => observation.Depth);
        builder.Ignore(observation => observation.DomainEvents);
    }
}
