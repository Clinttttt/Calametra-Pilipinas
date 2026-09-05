using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Calametra.Infrastructure.Persistence;

/// <summary>
/// Supplies a <see cref="ApplicationDbContext"/> to the EF Core command-line tools.
/// </summary>
/// <remarks>
/// <para>
/// <b>Design-time only.</b> The EF tools instantiate this directly and never touch
/// the application host, so nothing here participates in a running application.
/// The API and the worker resolve their connection string from configuration as
/// normal.
/// </para>
/// <para>
/// This exists because of a specific and easily-hit trap. Without it, the tools
/// build the host to obtain a context, and the host resolves its environment from
/// <c>ASPNETCORE_ENVIRONMENT</c>. That variable is unset in a plain terminal, so
/// the environment resolves to <b>Production</b>, <c>appsettings.json</c> is read
/// rather than <c>appsettings.Development.json</c>, and the migration attempts to
/// connect using the deliberate placeholder password — failing with
/// <c>28P01: password authentication failed</c>. The cause is invisible from the
/// error, and the usual workaround is remembering to export an environment
/// variable before every command.
/// </para>
/// <para>
/// Set <c>CALAMETRA_DB</c> to point the tools at a different database. The
/// fallback matches <c>docker-compose.yml</c>, so <c>dotnet ef database update</c>
/// works against a freshly started local container with no further setup. These are
/// disposable local development credentials and are not used by any deployed
/// environment.
/// </para>
/// </remarks>
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    private const string ConnectionStringVariable = "CALAMETRA_DB";

    /// <summary>Matches the <c>database</c> service in docker-compose.yml.</summary>
    private const string LocalDevelopmentConnectionString =
        "Host=localhost;Port=5433;Database=calametra;Username=calametra;Password=calametra_dev";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringVariable)
            ?? LocalDevelopmentConnectionString;

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql
                .UseNetTopologySuite()
                .MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(options);
    }
}
