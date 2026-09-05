using Calametra.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Calametra.Infrastructure.Persistence.Configurations;

internal sealed class DataSourceConfiguration : IEntityTypeConfiguration<DataSource>
{
    public void Configure(EntityTypeBuilder<DataSource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("data_sources");

        builder.HasKey(source => source.Id);

        builder.Property(source => source.Slug).HasMaxLength(64).IsRequired();
        builder.Property(source => source.Agency).HasMaxLength(128).IsRequired();
        builder.Property(source => source.DatasetName).HasMaxLength(256).IsRequired();
        builder.Property(source => source.Attribution).HasMaxLength(512).IsRequired();
        builder.Property(source => source.SourceUrl).HasMaxLength(512);
        builder.Property(source => source.TermsUrl).HasMaxLength(512);
        builder.Property(source => source.CoverageNotes).HasMaxLength(2_000);
        builder.Property(source => source.SourceVersion).HasMaxLength(64);
        builder.Property(source => source.LastPayloadChecksum).HasMaxLength(128);

        builder.Property(source => source.AccessKind)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        // Ingestion and seeding look sources up by slug, so it must be unique.
        builder.HasIndex(source => source.Slug).IsUnique();

        builder.Ignore(source => source.DomainEvents);
    }
}
