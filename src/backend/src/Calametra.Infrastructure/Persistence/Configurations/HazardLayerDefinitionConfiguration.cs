using Calametra.Domain.Hazards;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Calametra.Infrastructure.Persistence.Configurations;

internal sealed class HazardLayerDefinitionConfiguration : IEntityTypeConfiguration<HazardLayerDefinition>
{
    public void Configure(EntityTypeBuilder<HazardLayerDefinition> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("hazard_layers");

        builder.HasKey(layer => layer.Id);

        builder.Property(layer => layer.DisplayName).HasMaxLength(160).IsRequired();
        builder.Property(layer => layer.WmsEndpoint).HasMaxLength(512);
        builder.Property(layer => layer.WmsLayerName).HasMaxLength(128);
        builder.Property(layer => layer.FeatureInfoEndpoint).HasMaxLength(512);
        builder.Property(layer => layer.Explainer).HasMaxLength(2_000);
        builder.Property(layer => layer.InterpretationNote).HasMaxLength(2_000);

        builder.Property(layer => layer.HazardType)
            .HasConversion<string>()
            .HasMaxLength(48)
            .IsRequired();

        builder.Property(layer => layer.Lens)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(layer => layer.DeliveryMode)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.HasIndex(layer => new { layer.Lens, layer.SortOrder });

        builder.HasOne<Domain.Sources.DataSource>()
            .WithMany()
            .HasForeignKey(layer => layer.DataSourceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(layer => layer.DomainEvents);
    }
}
