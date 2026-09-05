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

if (!isBackfill)
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
