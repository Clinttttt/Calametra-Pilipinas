using System.Globalization;
using System.Threading.RateLimiting;
using Calametra.Api.Middleware;
using Microsoft.AspNetCore.RateLimiting;

namespace Calametra.Api.Extensions;

/// <summary>Registers everything the HTTP host itself needs.</summary>
internal static class ServiceRegistration
{
    /// <summary>CORS policy name for the Angular client.</summary>
    public const string ClientCorsPolicy = "calametra-client";

    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOpenApi();

        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                context.ProblemDetails.Instance =
                    $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";

                context.ProblemDetails.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier);
            });

        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddCors(options => options.AddPolicy(ClientCorsPolicy, policy =>
        {
            // Origins come from configuration so production is never a wildcard.
            var origins = configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>() ?? [];

            if (origins.Length > 0)
            {
                policy.WithOrigins(origins);
            }

            policy.AllowAnyHeader().WithMethods("GET", "HEAD", "OPTIONS");
        }));

        services.AddApiRateLimiting();

        services.AddResponseCompression(options => options.EnableForHttps = true);

        services.AddHealthChecks();

        return services;
    }

    private static void AddApiRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        title = "Too many requests",
                        status = StatusCodes.Status429TooManyRequests,
                        code = "request.rate_limited",
                    },
                    cancellationToken);
            };

            // Generous: reading hazard data is the point of the service, and a map
            // pan legitimately produces a burst of requests.
            options.AddPolicy(RateLimitPolicies.PublicRead, PartitionByClient(
                permitLimit: 300,
                window: TimeSpan.FromMinutes(1)));

            // Map tiles, sized for how a map actually behaves rather than for how a
            // user does. MapLibre requests one tile per viewport tile, so a single
            // view is twenty to forty requests and panning across the archipelago is
            // several hundred. An earlier limit of 60/min was set as though a request
            // were a user action, and returned HTTP 429 to our own map within seconds.
            //
            // Upstream volume is controlled by the server-side tile cache in
            // PhivolcsHazardMapService, not here. This limit exists only to stop a
            // pathological client, so it is set well above legitimate map use.
            options.AddPolicy(RateLimitPolicies.HazardProxy, PartitionByClient(
                permitLimit: 1_200,
                window: TimeSpan.FromMinutes(1)));
        });

    private static Func<HttpContext, RateLimitPartition<string>> PartitionByClient(
        int permitLimit,
        TimeSpan window) =>
        httpContext => RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
            });
}
