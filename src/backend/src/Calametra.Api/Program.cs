using Calametra.Api.Endpoints.Cyclones;
using Calametra.Api.Endpoints.Earthquakes;
using Calametra.Api.Endpoints.HazardLayers;
using Calametra.Api.Endpoints.Administrative;
using Calametra.Api.Endpoints.Places;
using Calametra.Api.Endpoints.Sources;
using Calametra.Api.Extensions;
using Calametra.Application;
using Calametra.Infrastructure;
using Scalar.AspNetCore;
using Serilog;

// This file is the ONE place permitted to name a Calametra.Infrastructure type.
// Enforced by Calametra.ArchitectureTests.Api_ShouldOnlyReferenceInfrastructureFromProgram.

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApiServices(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .WithTitle("Calametra Pilipinas API")
        .WithTheme(ScalarTheme.BluePlanet));
}
else
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

app.UseResponseCompression();
app.UseSerilogRequestLogging();
app.UseCors(ServiceRegistration.ClientCorsPolicy);
app.UseRateLimiter();

app.MapHealthChecks("/health");

app.MapEarthquakes();
app.MapCyclones();
app.MapHazardLayers();
app.MapPlaces();
app.MapDataSources();
app.MapLgus();
app.MapLguBoundaries();

await app.RunAsync();

/// <summary>Exposed as a partial class so integration tests can use WebApplicationFactory.</summary>
public partial class Program;
