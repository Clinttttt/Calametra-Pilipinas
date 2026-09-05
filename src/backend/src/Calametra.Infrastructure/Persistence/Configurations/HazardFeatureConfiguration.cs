using Calametra.Domain.Hazards;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Calametra.Infrastructure.Persistence.Configurations;

internal sealed class HazardFeatureConfiguration : IEntityTypeConfiguration<HazardFeature>
{
    public void Configure(EntityTypeBuilder<HazardFeature> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("hazard_features");

        builder.HasKey(feature => feature.Id);

        builder.Property(feature => feature.ExternalId).HasMaxLength(128).IsRequired();
        builder.Property(feature => feature.Name).HasMaxLength(256);
        builder.Property(feature => feature.Classification).HasMaxLength(96);

        builder.Property(feature => feature.Geometry)
            .HasColumnType(PostgisTypes.AnyGeometry)
            .IsRequired();

        // Publisher attributes stored as jsonb so they remain queryable if a
        // specific field later turns out to matter, without a schema change.
        builder.Property(feature => feature.AttributesJson).HasColumnType("jsonb");

        // Import reconciles on the publisher's own identifier, so a re-import
        // revises rather than duplicating.
        builder.HasIndex(feature => new { feature.DataSourceId, feature.ExternalId }).IsUnique();

        // The spatial index that makes "nearest fault to this epicentre" fast.
        // This is the capability a proxied raster layer cannot provide at all.
        builder.HasIndex(feature => feature.Geometry).HasMethod("gist");

        builder.HasIndex(feature => feature.HazardLayerId);

        builder.HasOne<HazardLayerDefinition>()
            .WithMany()
            .HasForeignKey(feature => feature.HazardLayerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Domain.Sources.DataSource>()
            .WithMany()
            .HasForeignKey(feature => feature.DataSourceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(feature => feature.DomainEvents);
    }
}
