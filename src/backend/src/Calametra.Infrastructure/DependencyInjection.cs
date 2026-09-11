using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Sources;
using Calametra.Infrastructure.Persistence;
using Calametra.Infrastructure.Sources.Gem;
using Calametra.Infrastructure.Sources.GeoNames;
using Calametra.Infrastructure.Sources.Ibtracs;
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

        services.AddHttpClient<IHazardMapService, PhivolcsHazardMapService>((provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<PhivolcsOptions>>().Value;

                client.Timeout = options.Timeout;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
            })
            .AddStandardResilienceHandler();

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
    }
}
