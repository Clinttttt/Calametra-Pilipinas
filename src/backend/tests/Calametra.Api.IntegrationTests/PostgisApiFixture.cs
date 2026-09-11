using Calametra.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Calametra.Api.IntegrationTests;

/// <summary>
/// A real PostgreSQL 17 + PostGIS 3.5 database, migrated, with the API hosted against it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a container rather than the EF in-memory provider.</b> Every query worth testing in this
/// project is spatial, and the in-memory provider has no PostGIS at all — it would silently accept
/// LINQ that Postgres cannot translate. That is not hypothetical: projecting <c>Centroid.Y</c>
/// compiles, passes unit tests, and returns HTTP 500 against a real database, because PostGIS
/// defines <c>ST_Y</c> on <c>geometry</c> and the column is <c>geography</c>. Only a real server
/// catches that class of defect, which is the whole reason this project exists.
/// </para>
/// <para>
/// <b>The image is pinned to the one the application ships with.</b> <c>postgis/postgis:17-3.5</c>,
/// matching <c>docker-compose.yml</c>. Testing against a different PostGIS version would verify a
/// database nobody runs.
/// </para>
/// <para>
/// <b>Migrations run rather than a schema being created from the model.</b>
/// <c>EnsureCreated</c> would build the tables from the current model and never execute a
/// migration, so the migrations — where this project has already had to hand-correct a
/// data-destroying rename and two fabricated column defaults — would go untested.
/// </para>
/// </remarks>
public sealed class PostgisApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgis/postgis:17-3.5")
        .WithDatabase("calametra_tests")
        .WithUsername("calametra")
        .WithPassword("calametra_tests")
        .Build();

    private WebApplicationFactory<Program>? _factory;

    /// <summary>The hosted API, talking to the container.</summary>
    public WebApplicationFactory<Program> Api =>
        _factory ?? throw new InvalidOperationException("The fixture has not been initialised.");

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Database", ConnectionString);

                // Development so the host does not demand HTTPS redirection, which would turn
                // every request in these tests into a 307.
                builder.UseEnvironment("Development");
            });

        await using var scope = Api.Services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    /// <summary>
    /// Runs work against the database directly, for arranging state and asserting on it.
    /// </summary>
    /// <remarks>
    /// Its own scope each time, so a test cannot accidentally assert against an entity still
    /// tracked from the arrange step — which would pass while the row was never written.
    /// </remarks>
    public async Task WithContextAsync(Func<ApplicationDbContext, Task> work)
    {
        await using var scope = Api.Services.CreateAsyncScope();

        await work(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    public HttpClient CreateClient() => Api.CreateClient();
}

/// <summary>
/// Shares one container across every test class.
/// </summary>
/// <remarks>
/// <para>
/// Starting PostGIS takes seconds; doing it per class would make the suite slow enough that it
/// stopped being run, which is the failure mode that leaves a test project empty.
/// </para>
/// <para>
/// <b>The consequence is that every test shares one database, and tests must be written for
/// that.</b> Arrange coordinates that cannot collide with another test's, and never assert on a
/// total — assert on what this test put there. The first version of the "no place within 300 km"
/// test placed its epicentre 99 km from a city another class had added, so it passed or failed
/// depending on execution order. Any test needing a genuinely empty table needs its own database
/// instead.
/// </para>
/// </remarks>
[CollectionDefinition(Name)]
public sealed class PostgisCollection : ICollectionFixture<PostgisApiFixture>
{
    public const string Name = "postgis";
}
