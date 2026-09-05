using Calametra.Application.Abstractions.Data;
using Calametra.Application.Abstractions.Sources;
using Calametra.Infrastructure.Persistence;
using Calametra.Infrastructure.Sources.Gem;
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
    }
}
