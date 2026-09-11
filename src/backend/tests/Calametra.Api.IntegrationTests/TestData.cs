using Calametra.Domain.Events;
using Calametra.Domain.Geospatial;
using Calametra.Domain.Places;
using Calametra.Domain.Seismology;
using Calametra.Domain.Sources;
using Calametra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Calametra.Api.IntegrationTests;

/// <summary>
/// Arranges the minimum a test needs, through the domain rather than by writing SQL.
/// </summary>
/// <remarks>
/// Going through the aggregates means the fixtures cannot contain a state the domain would refuse
/// — a magnitude without a scale, a depth without a quality, an unattributed reading. A test that
/// arranged rows with raw SQL could assert on data the application can never produce.
/// </remarks>
internal static class TestData
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Finds or creates a source, so tests can be arranged in any order.</summary>
    public static async Task<DataSource> EnsureSourceAsync(
        ApplicationDbContext context,
        string slug,
        string agency = "Test Agency")
    {
        var existing = await context.DataSources.FirstOrDefaultAsync(source => source.Slug == slug);

        if (existing is not null)
        {
            return existing;
        }

        var created = DataSource.Create(
                slug,
                agency,
                datasetName: "Integration test dataset",
                SourceAccessKind.RestApi,
                attribution: "Test data.",
                Now)
            .Value;

        created.WithPermissions(isRedistributable: true, isAuthoritativeForPhilippines: false);

        context.DataSources.Add(created);
        await context.SaveChangesAsync();

        return created;
    }

    /// <summary>Adds a city with the given coordinate, attributed to a gazetteer source.</summary>
    public static async Task<Place> AddPlaceAsync(
        ApplicationDbContext context,
        string name,
        double latitude,
        double longitude,
        string? psgcCode = null,
        PlaceKind kind = PlaceKind.City,
        Place? parent = null)
    {
        var gazetteer = await EnsureSourceAsync(context, "test-gazetteer", "Test Gazetteer");

        var place = Place.Create(name, kind, Wgs84.Point(latitude, longitude), gazetteer.Id, Now)
            .Value
            .WithHierarchy(psgcCode, parent?.Id);

        context.Places.Add(place);
        await context.SaveChangesAsync();

        return place;
    }

    /// <summary>
    /// Adds an earthquake with one agency reading.
    /// </summary>
    /// <param name="depthQuality">
    /// Defaults to <see cref="DepthQuality.Constrained"/>. Pass
    /// <see cref="DepthQuality.OperatorAssigned"/> to arrange the case that 43% of the real
    /// archive is in.
    /// </param>
    public static async Task<HazardEvent> AddEarthquakeAsync(
        ApplicationDbContext context,
        double latitude,
        double longitude,
        double magnitude,
        MagnitudeType scale,
        double depthKm,
        DepthQuality depthQuality = DepthQuality.Constrained,
        DateTimeOffset? occurredAt = null,
        string? externalId = null,
        string sourceSlug = "test-catalogue")
    {
        var source = await EnsureSourceAsync(context, sourceSlug);
        var when = occurredAt ?? Now;
        var epicentre = Wgs84.Point(latitude, longitude);

        var hazardEvent = HazardEvent.Create(HazardEventType.Earthquake, when, epicentre, Now).Value;

        hazardEvent.AddObservation(
            source.Id,
            externalId ?? $"test-{Guid.CreateVersion7()}",
            when,
            epicentre,
            new DepthReading(depthKm, depthQuality),
            new MagnitudeReading(magnitude, scale),
            Now);

        context.HazardEvents.Add(hazardEvent);
        await context.SaveChangesAsync();

        return hazardEvent;
    }
}
