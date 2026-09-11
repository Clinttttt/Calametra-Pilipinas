using System.Globalization;
using Calametra.Application;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Ingestion;
using Calametra.Ingestion;
using Calametra.Infrastructure;
using Calametra.Infrastructure.Persistence.Seed;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

builder.Services.AddSingleton<BackfillRunner>();

// Backfill mode replaces the rolling worker rather than running alongside it.
// Loading a decade of history and simultaneously polling for the last week would
// contend for the same connection pool and the same upstream service for no gain.
var backfillFrom = ParseDate(builder.Configuration["Ingestion:Backfill:From"]);
var backfillTo = ParseDate(builder.Configuration["Ingestion:Backfill:To"]);
var isBackfill = backfillFrom is not null && backfillTo is not null;

// Cyclone import is its own one-shot mode, opted into explicitly:
//   dotnet run --project src/Calametra.Ingestion -- --Ingestion:Cyclones:Import=true
var isCycloneImport = bool.TryParse(
    builder.Configuration["Ingestion:Cyclones:Import"],
    out var cycloneFlag) && cycloneFlag;

var cycloneFromSeason = int.TryParse(
    builder.Configuration["Ingestion:Cyclones:FromSeason"],
    out var parsedSeason) ? parsedSeason : 1945;

// The place directory is its own one-shot mode for the same reason as cyclones, and more so:
// administrative boundaries change on a timescale of years, by legislation.
//   dotnet run --project src/Calametra.Ingestion -- --Ingestion:Places:Import=true
var isPlaceImport = bool.TryParse(
    builder.Configuration["Ingestion:Places:Import"],
    out var placeFlag) && placeFlag;

if (!isBackfill && !isCycloneImport && !isPlaceImport)
{
    builder.Services.AddHostedService<EarthquakeIngestionWorker>();
}

var host = builder.Build();

// Reference data first, always. Every observation needs a data source row and
// every stored feature needs a layer, so nothing can be ingested without it.
// Idempotent and keyed on slug.
await using (var scope = host.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<ReferenceDataSeeder>().SeedAsync();

    // Fault geometry is imported at startup rather than polled. A fault catalogue
    // is revised on a timescale of years, so polling it would be pointless traffic
    // against a volunteer-run research repository.
    var faultImport = await scope.ServiceProvider
        .GetRequiredService<IDispatcher>()
        .Send(new ImportActiveFaults.Command());

    if (faultImport.IsFailure)
    {
        // Non-fatal: earthquake ingestion is the primary job and must not be
        // blocked because a fault catalogue was unreachable at boot.
        WorkerLog.FaultImportFailed(
            host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Calametra.Ingestion.Startup"),
            faultImport.Error!.Code,
            faultImport.Error.Description);
    }
}

if (isCycloneImport)
{
    // A separate one-shot mode, deliberately not part of the rolling worker. The western
    // Pacific basin file is over 100 MB; streaming it on every boot would be pointless
    // traffic against a public archive that is revised once a season.
    await using var scope = host.Services.CreateAsyncScope();

    var logger = host.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Calametra.Ingestion.Cyclones");

    var result = await scope.ServiceProvider
        .GetRequiredService<IDispatcher>()
        .Send(new IngestCycloneTracks.Command { FromSeason = cycloneFromSeason });

    if (result.IsFailure)
    {
        WorkerLog.CycloneImportFailed(logger, result.Error!.Code, result.Error.Description);

        return 1;
    }

    // Local names are applied after import, because a storm has to exist before it can be
    // matched. Curated rather than ingested: PAGASA publishes name lists as documents, and no
    // machine-readable crosswalk from international to local name exists.
    await scope.ServiceProvider.GetRequiredService<PagasaNameSeeder>().SeedAsync();

    WorkerLog.CycloneImportCompleted(
        logger,
        result.Value.StormsCreated,
        result.Value.StormsSkipped,
        result.Value.TrackPointsCreated,
        result.Value.StormsRejected);

    return 0;
}

if (isPlaceImport)
{
    await using var scope = host.Services.CreateAsyncScope();

    var logger = host.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Calametra.Ingestion.Places");

    var result = await scope.ServiceProvider
        .GetRequiredService<IDispatcher>()
        .Send(new ImportPlaces.Command());

    if (result.IsFailure)
    {
        WorkerLog.PlaceImportFailed(logger, result.Error!.Code, result.Error.Description);

        return 1;
    }

    WorkerLog.PlaceImportCompleted(
        logger,
        result.Value.CreatedCount,
        result.Value.SkippedCount,
        result.Value.RejectedCount,
        result.Value.WithoutPsgcCodeCount,
        result.Value.UnresolvedParentCount);

    return 0;
}

if (isBackfill)
{
    var runner = host.Services.GetRequiredService<BackfillRunner>();

    var succeeded = await runner.RunAsync(backfillFrom!.Value, backfillTo!.Value, CancellationToken.None);

    // Bulletin figures are attached after the backfill, because matching needs the
    // events to exist first.
    await using (var scope = host.Services.CreateAsyncScope())
    {
        await scope.ServiceProvider.GetRequiredService<BulletinObservationSeeder>().SeedAsync();
    }

    // Non-zero on partial failure so a scripted backfill can be detected as
    // incomplete rather than silently assumed good.
    return succeeded ? 0 : 1;
}

await host.RunAsync();

return 0;

static DateTimeOffset? ParseDate(string? value) =>
    DateTimeOffset.TryParse(
        value,
        CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
        out var parsed)
        ? parsed
        : null;
