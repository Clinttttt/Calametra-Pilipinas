using Calametra.Domain.Events;
using Calametra.Domain.Places;
using Calametra.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Calametra.Infrastructure.Persistence.Configurations;

internal sealed class CycloneTrackPointConfiguration : IEntityTypeConfiguration<CycloneTrackPoint>
{
    public void Configure(EntityTypeBuilder<CycloneTrackPoint> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("cyclone_track_points");

        builder.HasKey(trackPoint => trackPoint.Id);

        builder.Property(trackPoint => trackPoint.Position)
            .HasColumnType(PostgisTypes.Point)
            .IsRequired();

        builder.Property(trackPoint => trackPoint.Classification).HasMaxLength(64);

        builder.Property(trackPoint => trackPoint.ExternalStormId)
            .HasMaxLength(64)
            .IsRequired();

        // Stored as its name rather than its numeric value. The enum's members carry the
        // interval in minutes, so an int column would look like data ("10") and read as
        // meaningful on its own; the name cannot be mistaken for a measurement.
        builder.Property(trackPoint => trackPoint.WindAveragingPeriod)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        // Stored as its name for the same reason: "1" and "2" as a geometry code would read as
        // data, whereas Quadrants and Ellipse cannot be mistaken for a measurement.
        builder.Property(trackPoint => trackPoint.WindFieldGeometry)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        // Replay walks an event's fixes in time order, so this is the access path.
        builder.HasIndex(trackPoint => new { trackPoint.HazardEventId, trackPoint.CapturedAt });

        builder.HasIndex(trackPoint => trackPoint.Position).HasMethod("gist");

        // Intensity filtering is always qualified by the averaging period, because a speed
        // threshold means different things across intervals. Indexed together so the query
        // cannot cheaply be written the wrong way round.
        builder.HasIndex(trackPoint => new
        {
            trackPoint.WindAveragingPeriod,
            trackPoint.WindSpeedKnots,
        });

        // Ingestion recognises an already-imported storm by this id, so it must be fast. Not
        // unique: every fix of a storm, from every agency, shares it.
        builder.HasIndex(trackPoint => trackPoint.ExternalStormId);

        // One fix per agency per event per timestamp. A second report for the same moment is
        // a revision, not an additional observation.
        builder.HasIndex(trackPoint => new
        {
            trackPoint.HazardEventId,
            trackPoint.DataSourceId,
            trackPoint.CapturedAt,
        }).IsUnique();

        builder.HasOne<Domain.Sources.DataSource>()
            .WithMany()
            .HasForeignKey(trackPoint => trackPoint.DataSourceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Projection over the mapped primitives; the value objects are derived.
        builder.Ignore(trackPoint => trackPoint.Wind);
        builder.Ignore(trackPoint => trackPoint.GaleField);
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

        builder.HasOne<DataSource>()
            .WithMany()
            .HasForeignKey(place => place.DataSourceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Place>()
            .WithMany()
            .HasForeignKey(place => place.ParentPlaceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(place => place.DomainEvents);
    }
}
