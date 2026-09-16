using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Sources;
using Calametra.Infrastructure.Persistence;
using Calametra.Infrastructure.Sources.ArcGis;
using Calametra.Infrastructure.Sources.Gem;
using Calametra.Infrastructure.Sources.GeoNames;
using Calametra.Infrastructure.Sources.Ibtracs;
using Calametra.Infrastructure.Sources.Mgb;
using Calametra.Infrastructure.Sources.OpenStreetMap;
using Calametra.Infrastructure.Sources.Psgc;
using Calametra.Infrastructure.Sources.Phivolcs;
using Calametra.Infrastructure.Sources.Usgs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Calametra.Infrastructure;

/// <summary>Registers persistence and every port adapter this assembly provides.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddPersistence(configuration);
        services.AddExternalSources(configuration);
        services.AddScoped<Persistence.Seed.ReferenceDataSeeder>();
        services.AddScoped<Persistence.Seed.BulletinObservationSeeder>();
        services.AddScoped<Persistence.Seed.PagasaNameSeeder>();

        return services;
    }

    private static void AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "Connection string 'Database' is not configured. Set ConnectionStrings:Database.");

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options
                .UseNpgsql(connectionString, npgsql => npgsql
                    .UseNetTopologySuite()
                    .MigrationsHistoryTable("__ef_migrations_history"))
                // snake_case so tables and columns are readable in psql without quoting.
                .UseSnakeCaseNamingConvention();

            // Opt-in diagnostics. Off by default because sensitive data logging writes
            // parameter values — including anything user-supplied — into the log, which
            // must never happen outside a developer's own machine.
            if (configuration.GetValue("Database:EnableDetailedErrors", false))
            {
                options.EnableDetailedErrors().EnableSensitiveDataLogging();
            }
        });

        // A factory delegating to the same scoped context, never a second AddDbContext.
        // Two registrations would mean two change trackers per request, and writes
        // through one would be invisible to the other.
        services.AddScoped<IApplicationDbContext>(provider =>
            provider.GetRequiredService<ApplicationDbContext>());

        // The crosswalk review surface, delegating to the same instance for the same reason. Registered
        // separately rather than folded into the interface above because the separation IS the read
        // boundary ADR-005 D4 requires: analytics hold a surface on which an unreviewed proposal does
        // not exist, and only the matcher, the review commands and the readiness report ask for this one.
        services.AddScoped<ILguCrosswalkReviewContext>(provider =>
            provider.GetRequiredService<ApplicationDbContext>());
    }

    private static void AddExternalSources(this IServiceCollection services, IConfiguration configuration)
    {
        // Bounded tile cache. This is what actually protects the upstream agency: a
        // tile is fetched once and then served from memory to every later request, so
        // PHIVOLCS sees one request per distinct tile per lifetime no matter how many
        // people are panning.
        //
        // Size is measured in bytes and capped, because an unbounded cache over a
        // service that can render any bounding box at any scale has no natural limit —
        // a user panning continuously would otherwise grow the process indefinitely.
        // Tiles are ~11 KB, so 64 MB holds several thousand.
        services.AddMemoryCache(options => options.SizeLimit = 64L * 1024 * 1024);

        services.AddOptions<UsgsOptions>()
            .Bind(configuration.GetSection(UsgsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<PhivolcsOptions>()
            .Bind(configuration.GetSection(PhivolcsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Agency-neutral: the proxy serves DOST-PHIVOLCS and DOST-MGB from the same code path,
        // driven by the endpoints stored on each catalogue row.
        services.AddOptions<HazardProxyOptions>()
            .Bind(configuration.GetSection(HazardProxyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<MgbOptions>()
            .Bind(configuration.GetSection(MgbOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<GemOptions>()
            .Bind(configuration.GetSection(GemOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IEarthquakeCatalogSource, UsgsEarthquakeCatalogSource>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<UsgsOptions>>().Value;

                client.BaseAddress = options.BaseAddress;
                client.Timeout = options.Timeout;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            })
            // Retries transient failures and trips a circuit breaker on sustained
            // ones. Ingestion talks to a third-party service on a schedule; failing
            // the whole run because of one blip is the wrong behaviour, and hammering
            // an agency that is already struggling is worse.
            .AddStandardResilienceHandler();

        services.AddHttpClient<IHazardMapService, ArcGisHazardMapService>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<HazardProxyOptions>>().Value;

                client.Timeout = options.Timeout;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            })
            // Retries transient failures and trips a circuit breaker on sustained ones, which is
            // worth having for a proxy — but the defaults had to be widened, and the numbers come
            // from measurement.
            //
            // The standard handler's per-attempt timeout defaults to 10 seconds, and a national
            // polygon layer legitimately takes longer: measured 18.8-19.4 s for MGB's rain-induced
            // landslide render and 14.4 s for the PHIVOLCS earthquake-induced landslide service. At
            // 10 seconds those return HTTP 500 to our own map while the publisher is still working,
            // which looks like an upstream fault rather than a timeout of ours. This is the same
            // trap the GEM and IBTrACS adapters avoid by refusing the handler outright; here retry
            // is worth keeping, so the timeout moves instead.
            //
            // 30 seconds is not enough for everything, and deliberately so. The PHIVOLCS tsunami
            // service takes 140 s to render a 400 km extent, and no timeout makes that a usable
            // layer — so it is not catalogued, rather than catalogued with a timeout sized to hide
            // the problem. See ReferenceDataSeeder.EnsurePhivolcsHazardLayersAsync.
            //
            // The library requires the total to be at least the attempt timeout and the circuit
            // breaker's sampling window to be at least twice it, so all three move together.
            .AddStandardResilienceHandler(resilience =>
            {
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                resilience.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(90);
                resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
            });

        services.AddHttpClient<IActiveFaultSource, GemActiveFaultSource>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<GemOptions>>().Value;

                client.Timeout = options.Timeout;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            });
        // Deliberately no resilience handler: this fetches a single ~10 MB
        // document, and the standard handler's per-attempt timeout would abort a
        // legitimately slow download and then retry the whole transfer.

        services.AddOptions<IbtracsOptions>()
            .Bind(configuration.GetSection(IbtracsOptions.SectionName))
            .ValidateOnStart();

        services.AddHttpClient<ICycloneTrackSource, IbtracsCycloneSource>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<IbtracsOptions>>().Value;

                client.Timeout = options.RequestTimeout;
            });
        // No resilience handler here either, and more emphatically: the basin file is over
        // 100 MB and is consumed as a stream. A retry would restart the whole transfer, and a
        // per-attempt timeout would cut off a download that is progressing normally.

        services.AddOptions<GeoNamesOptions>()
            .Bind(configuration.GetSection(GeoNamesOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpClient<IPlaceDirectorySource, GeoNamesPlaceSource>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<GeoNamesOptions>>().Value;

                client.Timeout = options.Timeout;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            });
        // No resilience handler: one 2.5 MB archive, fetched by hand once. A retry would restart
        // the transfer and the standard handler's per-attempt timeout would abort a slow but
        // healthy download — the same reasoning as the two adapters above.

        services.AddOptions<PsgcRegisterOptions>()
            .Bind(configuration.GetSection(PsgcRegisterOptions.SectionName));

        // The register is three JSON documents fetched once per import, so the same reasoning as the
        // gazetteer applies: no resilience handler, because a retry would restart the transfer and the
        // per-attempt timeout would abort a slow but healthy download. The PSA route is probed first and
        // its failure is recorded on the edition rather than retried into a success.
        services.AddHttpClient<IPsgcRegisterSource, PsgcRegisterSource>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(2);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Calametra/1.0 (+https://github.com/calametra; research platform; PSGC register import)");
        });

        services.AddOptions<OpenStreetMapOptions>()
            .Bind(configuration.GetSection(OpenStreetMapOptions.SectionName))
            .ValidateDataAnnotations()            .ValidateOnStart();

        services.AddHttpClient<ISettlementCoordinateSource, OverpassSettlementSource>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<OpenStreetMapOptions>>().Value;

                client.BaseAddress = new Uri(options.OverpassEndpoint);
                client.Timeout = options.Timeout;
                // Overpass answers 406 with an HTML body to a request without one, which reads as a
                // content-negotiation fault rather than as a missing header.
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            });
        // No resilience handler, and here it is a courtesy as well as a correctness argument: the
        // national query is one heavy request against a shared public instance, and Overpass's usage
        // policy asks callers not to retry heavy queries automatically.

        services.AddHttpClient<OverpassBoundarySource>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<OpenStreetMapOptions>>().Value;

                client.BaseAddress = new Uri(options.OverpassEndpoint);

                // Longer than the settlement client's. A chunk of boundary geometry is megabytes of
                // coordinates rather than a list of points, and the server-side timeout in the query is
                // set from this value, so a short client timeout would abandon work Overpass is still
                // doing.
                client.Timeout = options.BoundaryTimeout;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            });

        // The reader is stateless; the composite decides between a dated local extract and the API.
        services.AddScoped<OsmPbfBoundaryReader>();
        services.AddScoped<ILguBoundarySource, OsmBoundarySource>();
        // The adapter retries once per chunk itself, with a twenty-second pause. That is deliberate rather
        // than delegated to a resilience handler: it retries at the granularity of a chunk, so a refusal
        // costs one box rather than restarting a national fetch, and it stays inside the two-slot limit
        // the public instance publishes.
    }
}
