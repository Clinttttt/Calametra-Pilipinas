using System.Globalization;
using Calametra.Application;
using Calametra.Application.Abstractions.Messaging;
using Calametra.Application.Features.Administrative;
using Calametra.Application.Features.Ingestion;
using Calametra.Domain.Administrative;
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

// Correcting where the places are is its own mode, separate from deciding which exist:
//   dotnet run --project src/Calametra.Ingestion -- --Ingestion:Places:RefineCoordinates=true
var isCoordinateRefinement = bool.TryParse(
    builder.Configuration["Ingestion:Places:RefineCoordinates"],
    out var refineFlag) && refineFlag;

// Applying Philippine local names is its own mode too:
//   dotnet run --project src/Calametra.Ingestion -- --Ingestion:Cyclones:ApplyNames=true
//
// It runs at the end of the cyclone import as well, because a storm must exist before it can be
// matched. But the crosswalk is curated and grows as pairings are researched, and re-streaming a
// 100 MB basin file to apply a name nobody's track data changed would be pointless traffic against
// a public archive.
var isNameApplication = bool.TryParse(
    builder.Configuration["Ingestion:Cyclones:ApplyNames"],
    out var nameFlag) && nameFlag;

// ── ADR-005 PHASE 1 AND 2: ADMINISTRATIVE IDENTITY ──────────────────────────
//
// Three modes rather than one, because they are three decisions and the middle one must not be able
// to make the third. Importing the register creates canonical identities; proposing pairings fills a
// review queue and can establish nothing; the readiness report answers the four-condition gate that
// stands between this work and any polygon ingestion.
//
//   dotnet run --project src/Calametra.Ingestion -- --Ingestion:Lgu:ImportRegister=true
//   dotnet run --project src/Calametra.Ingestion -- --Ingestion:Lgu:ProposeLinks=true
//   dotnet run --project src/Calametra.Ingestion -- --Ingestion:Lgu:Readiness=true
var isRegisterImport = bool.TryParse(
    builder.Configuration["Ingestion:Lgu:ImportRegister"],
    out var registerFlag) && registerFlag;

var isLinkProposal = bool.TryParse(
    builder.Configuration["Ingestion:Lgu:ProposeLinks"],
    out var proposeFlag) && proposeFlag;

var isReadinessReport = bool.TryParse(
    builder.Configuration["Ingestion:Lgu:Readiness"],
    out var readinessFlag) && readinessFlag;

// ── ADR-005 D4 AND D5: THE REVIEW PATH ───────────────────────────────────────
//
// Three separate modes because they are three separate acts. Showing the queue decides nothing.
// Confirming a class is one attributed judgement about one kind of evidence. Accepting an exception
// is a written finding that no pairing exists. Collapsing them into one command would be the bulk
// promotion D4 refuses, wearing a different name.
var isReviewQueue = bool.TryParse(
    builder.Configuration["Ingestion:Lgu:ReviewQueue"],
    out var queueFlag) && queueFlag;

var confirmClass = builder.Configuration["Ingestion:Lgu:ConfirmClass"];
var isClassConfirmation = !string.IsNullOrWhiteSpace(confirmClass);

var isExceptionAcceptance = bool.TryParse(
    builder.Configuration["Ingestion:Lgu:AcceptExceptions"],
    out var exceptionFlag) && exceptionFlag;

var rejectLinkId = builder.Configuration["Ingestion:Lgu:RejectLink"];
var isLinkRejection = !string.IsNullOrWhiteSpace(rejectLinkId);

var isOneShot = isBackfill
    || isCycloneImport
    || isPlaceImport
    || isCoordinateRefinement
    || isNameApplication
    || isRegisterImport
    || isLinkProposal
    || isReadinessReport
    || isReviewQueue
    || isClassConfirmation
    || isExceptionAcceptance
    || isLinkRejection;

if (!isOneShot)
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

    var startupLogger = host.Services
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("Calametra.Ingestion.Startup");

    // The administrative modes read a PSGC publication and the place directory. Neither consults fault
    // geometry, so fetching it would be a minute of pointless traffic and one more upstream service that
    // could fail a run with nothing to do with faults — which is exactly what it did.
    var needsFaultGeometry = !isRegisterImport
        && !isLinkProposal
        && !isReadinessReport
        && !isReviewQueue
        && !isClassConfirmation
        && !isExceptionAcceptance
        && !isLinkRejection;

    if (needsFaultGeometry)
    {
        // Fault geometry is imported at startup rather than polled. A fault catalogue
        // is revised on a timescale of years, so polling it would be pointless traffic
        // against a volunteer-run research repository.
        //
        // Wrapped: the policy below is that an unreachable catalogue must not stop the run, and a
        // transport that dies mid-response throws rather than returning a failed Result. Handling only
        // the Result left the declared policy true of half the ways this can fail.
        try
        {
            var faultImport = await scope.ServiceProvider
                .GetRequiredService<IDispatcher>()
                .Send(new ImportActiveFaults.Command());

            if (faultImport.IsFailure)
            {
                // Non-fatal: earthquake ingestion is the primary job and must not be
                // blocked because a fault catalogue was unreachable at boot.
                WorkerLog.FaultImportFailed(
                    startupLogger,
                    faultImport.Error!.Code,
                    faultImport.Error.Description);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or HttpIOException or TaskCanceledException)
        {
            WorkerLog.FaultImportFailed(startupLogger, exception.GetType().Name, exception.Message);
        }
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

if (isNameApplication)
{
    await using var scope = host.Services.CreateAsyncScope();

    // Idempotent: the seeder matches on international name and season and leaves a storm that
    // already carries its local name untouched, so re-running after adding pairings is safe.
    await scope.ServiceProvider.GetRequiredService<PagasaNameSeeder>().SeedAsync();

    return 0;
}

if (isCoordinateRefinement){
    // Separate from the place import, and rightly so: the import decides which places exist and is
    // driven by legislation, while this corrects where they are and is driven by a volunteer map
    // that improves continuously. Re-running it is cheap and idempotent — a place already on its
    // town centre is left alone.
    await using var scope = host.Services.CreateAsyncScope();

    var logger = host.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Calametra.Ingestion.PlaceCoordinates");

    var result = await scope.ServiceProvider
        .GetRequiredService<IDispatcher>()
        .Send(new RefinePlaceCoordinates.Command());

    if (result.IsFailure)
    {
        WorkerLog.CoordinateRefinementFailed(logger, result.Error!.Code, result.Error.Description);

        return 1;
    }

    WorkerLog.CoordinateRefinementCompleted(
        logger,
        result.Value.MovedCount,
        result.Value.PlaceCount,
        result.Value.MeanMoveKilometres,
        result.Value.LargestMoveKilometres,
        result.Value.UnmatchedCount,
        result.Value.AmbiguousCount);

    return 0;
}

// ── ADR-005 PHASE 1: THE REGISTER ───────────────────────────────────────────
if (isRegisterImport)
{
    await using var scope = host.Services.CreateAsyncScope();

    var logger = host.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Calametra.Ingestion.PsgcRegister");

    var result = await scope.ServiceProvider
        .GetRequiredService<IDispatcher>()
        .Send(new ImportPsgcRegister.Command());

    if (result.IsFailure)
    {
        LguLog.RegisterImportFailed(logger, result.Error!.Code, result.Error.Description);

        return 1;
    }

    var summary = result.Value;

    LguLog.RegisterImportCompleted(
        logger,
        summary.EditionLabel,
        summary.Provenance,
        summary.IsCitableAsAuthority,
        summary.RegionCount,
        summary.ProvinceCount,
        summary.CityCount,
        summary.MunicipalityCount,
        summary.UnitsCreated,
        summary.UnitsReconciled,
        summary.UnitsRejected,
        summary.RegisterStatedPairings);

    LguReadinessPrinter.PrintImport(summary);

    return 0;
}

// ── ADR-005 PHASE 2: PROPOSALS ONLY ────────────────────────────────────────
if (isLinkProposal)
{
    await using var scope = host.Services.CreateAsyncScope();

    var logger = host.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Calametra.Ingestion.LguMatcher");

    var result = await scope.ServiceProvider
        .GetRequiredService<IDispatcher>()
        .Send(new ProposeLguCodeLinks.Command());

    if (result.IsFailure)
    {
        LguLog.ProposalFailed(logger, result.Error!.Code, result.Error.Description);

        return 1;
    }

    var summary = result.Value;

    LguLog.ProposalCompleted(
        logger,
        summary.RegisterUnits,
        summary.DirectoryRows,
        summary.ProposedRegisterMatch,
        summary.ProposedDigitReslice,
        summary.ResliceWithNameDisagreement,
        summary.AlreadyProposed,
        summary.UnitsWithNoCandidate,
        summary.DirectoryRowsWithNoCandidate);

    return 0;
}

// ── ADR-005 D4: THE REVIEW QUEUE ───────────────────────────────────────────
//
// Read-only. Exists because "1,729 proposals" is not reviewable information: a reviewer needs to see
// what kind of evidence each row rests on before deciding anything.
if (isReviewQueue)
{
    await using var scope = host.Services.CreateAsyncScope();

    var result = await scope.ServiceProvider
        .GetRequiredService<IDispatcher>()
        .Send(new GetLguReviewQueue.Query());

    if (result.IsFailure)
    {
        Console.WriteLine($"Review queue failed: {result.Error!.Code} — {result.Error.Description}");

        return 1;
    }

    LguReviewPrinter.WriteQueue(result.Value);

    return 0;
}

// ── ADR-005 D5: ACCEPT THE UNMATCHED SET ───────────────────────────────────
//
// The reasons are not invented here. Each is administrative history already documented in this
// repository or its data, and each is published to the reader.
if (isExceptionAcceptance)
{
    var acceptedBy = builder.Configuration["Ingestion:Lgu:ReviewedBy"];

    if (string.IsNullOrWhiteSpace(acceptedBy))
    {
        Console.WriteLine(
            "Ingestion:Lgu:ReviewedBy is required. An exception is a person's finding and the reason is "
            + "published with their name on it.");

        return 1;
    }

    await using var scope = host.Services.CreateAsyncScope();

    var exitCode = await LguExceptionRunner.AcceptKnownExceptionsAsync(
        scope.ServiceProvider.GetRequiredService<IDispatcher>(),
        acceptedBy);

    return exitCode;
}

// ── ADR-005 D4: CONFIRM ONE EVIDENCE CLASS AS A NAMED, DATED BATCH ─────────
if (isClassConfirmation)
{
    var reviewedBy = builder.Configuration["Ingestion:Lgu:ReviewedBy"];
    var sources = builder.Configuration["Ingestion:Lgu:SourcesConsulted"];
    var expected = builder.Configuration["Ingestion:Lgu:ExpectedCount"];

    if (string.IsNullOrWhiteSpace(reviewedBy) || string.IsNullOrWhiteSpace(sources))
    {
        Console.WriteLine(
            "Ingestion:Lgu:ReviewedBy and Ingestion:Lgu:SourcesConsulted are both required. ADR-005 D4 "
            + "permits a batch only when it records who decided and which sources were consulted.");

        return 1;
    }

    if (!int.TryParse(expected, CultureInfo.InvariantCulture, out var expectedCount))
    {
        Console.WriteLine(
            "Ingestion:Lgu:ExpectedCount is required and must be a number. The reviewer states how many "
            + "rows they are settling, so a population that changed since they read the queue aborts the "
            + "batch instead of confirming work they never saw.");

        return 1;
    }

    if (!Enum.TryParse<LguLinkEvidence>(confirmClass, ignoreCase: true, out var evidence))
    {
        Console.WriteLine(
            $"'{confirmClass}' is not an evidence class. Expected RegisterMatch or DigitReslice.");

        return 1;
    }

    await using var scope = host.Services.CreateAsyncScope();

    var result = await scope.ServiceProvider
        .GetRequiredService<IDispatcher>()
        .Send(new ConfirmLguCodeLinkClass.Command(evidence, reviewedBy, sources, expectedCount));

    if (result.IsFailure)
    {
        Console.WriteLine(
            $"Class confirmation refused: {result.Error!.Code} — {result.Error.Description}");

        return 1;
    }

    LguReviewPrinter.WriteClassConfirmation(result.Value);

    return 0;
}

// ── ADR-005 D4: REJECT ONE PAIRING ─────────────────────────────────────────
//
// The individual half of the review. A rejection is retained rather than deleted, because the
// proportion of proposals a reviewer refused is what justifies the review gate existing.
if (isLinkRejection)
{
    var reviewedBy = builder.Configuration["Ingestion:Lgu:ReviewedBy"];
    var reason = builder.Configuration["Ingestion:Lgu:Reason"];

    if (string.IsNullOrWhiteSpace(reviewedBy) || string.IsNullOrWhiteSpace(reason))
    {
        Console.WriteLine(
            "Ingestion:Lgu:ReviewedBy and Ingestion:Lgu:Reason are both required. A rejection without a "
            + "reason is a number nobody can explain.");

        return 1;
    }

    if (!Guid.TryParse(rejectLinkId, out var linkId))
    {
        Console.WriteLine($"'{rejectLinkId}' is not a link id. The review queue prints them.");

        return 1;
    }

    await using var scope = host.Services.CreateAsyncScope();

    var result = await scope.ServiceProvider
        .GetRequiredService<IDispatcher>()
        .Send(new RejectLguCodeLink.Command(linkId, reviewedBy, reason));

    if (result.IsFailure)
    {
        Console.WriteLine($"Rejection refused: {result.Error!.Code} — {result.Error.Description}");

        return 1;
    }

    Console.WriteLine($"Pairing {linkId} rejected by {reviewedBy}.");

    return 0;
}

// ── ADR-005: THE GATE ──────────────────────────────────────────────────────
//
// Printed rather than only logged, because this is the artefact a reader of the decision record
// checks before geometry work begins, and a gate whose verdict is buried in a log line is a gate
// nobody consults.
if (isReadinessReport)
{
    await using var scope = host.Services.CreateAsyncScope();

    var result = await scope.ServiceProvider
        .GetRequiredService<IDispatcher>()
        .Send(new GetLguCrosswalkReadiness.Query());

    if (result.IsFailure)
    {
        Console.WriteLine($"Readiness report failed: {result.Error!.Code} — {result.Error.Description}");

        return 1;
    }

    LguReadinessPrinter.Write(result.Value);

    // Non-zero while the gate is shut, so a script cannot proceed to geometry by ignoring the text.
    return result.Value.MayBeginGeometryIngestion ? 0 : 2;
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
