using Calametra.Domain.Events;
using Calametra.Domain.Places;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Calametra.Infrastructure.Persistence.Configurations;

internal sealed class EventTrackPointConfiguration : IEntityTypeConfiguration<EventTrackPoint>
{
    public void Configure(EntityTypeBuilder<EventTrackPoint> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("event_track_points");

        builder.HasKey(trackPoint => trackPoint.Id);

        builder.Property(trackPoint => trackPoint.Position)
            .HasColumnType(PostgisTypes.Point)
            .IsRequired();

        builder.Property(trackPoint => trackPoint.Classification).HasMaxLength(64);

        // Replay walks an event's fixes in time order, so this is the access path.
        builder.HasIndex(trackPoint => new { trackPoint.HazardEventId, trackPoint.CapturedAt });

        builder.HasIndex(trackPoint => trackPoint.Position).HasMethod("gist");

        builder.HasOne<Domain.Sources.DataSource>()
            .WithMany()
            .HasForeignKey(trackPoint => trackPoint.DataSourceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(trackPoint => trackPoint.DomainEvents);
    }
}

internal sealed class PlaceConfiguration : IEntityTypeConfiguration<Place>
{
    public void Configure(EntityTypeBuilder<Place> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("places");

        builder.HasKey(place => place.Id);

        builder.Property(place => place.Name).HasMaxLength(160).IsRequired();
        builder.Property(place => place.PsgcCode).HasMaxLength(16);

        builder.Property(place => place.Kind)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(place => place.Centroid)
            .HasColumnType(PostgisTypes.Point)
            .IsRequired();

        builder.Property(place => place.Boundary).HasColumnType(PostgisTypes.MultiPolygon);

        builder.HasIndex(place => place.PsgcCode).IsUnique().HasFilter("psgc_code IS NOT NULL");
        builder.HasIndex(place => new { place.Kind, place.Name });
        builder.HasIndex(place => place.Centroid).HasMethod("gist");
        builder.HasIndex(place => place.Boundary).HasMethod("gist");

        builder.HasOne<Place>()
            .WithMany()
            .HasForeignKey(place => place.ParentPlaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(place => place.DomainEvents);
    }
}
