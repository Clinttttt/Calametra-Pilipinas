using Calametra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Calametra.Api.IntegrationTests;

/// <summary>
/// Asserts the schema the application actually gets when its migrations are applied in order.
/// </summary>
/// <remarks>
/// These exist because this project's migrations have needed hand-correction three times, each
/// time for something the compiler and the unit tests could not see: a scaffolded table rename
/// that dropped and recreated a table holding 27,241 rows, a required column defaulted to the
/// all-zero GUID, and two coordinate columns defaulted to <c>0.0</c> — which would have moved
/// every stored place to the Gulf of Guinea. A migration is code that runs once against real
/// data, and it deserves a test that runs it.
/// </remarks>
[Collection(PostgisCollection.Name)]
public sealed class SchemaTests(PostgisApiFixture fixture)
{
    [Fact]
    public async Task EveryMigration_ShouldBeApplied()
    {
        await fixture.WithContextAsync(async context =>
        {
            var pending = await context.Database.GetPendingMigrationsAsync();

            pending.ShouldBeEmpty(
                "the fixture applies migrations on start, so anything pending means a migration "
                + "failed to run: " + string.Join(", ", pending));
        });
    }

    [Fact]
    public async Task Postgis_ShouldBeAvailable()
    {
        // The initial migration creates the extension. Without it every geography column and
        // every spatial query in the platform is unusable, and the failure would appear as a
        // confusing parse error rather than as a missing extension.
        var version = await ScalarAsync<string>("SELECT postgis_version();");

        version.ShouldNotBeNullOrWhiteSpace();
        version.ShouldStartWith("3.5");
    }

    [Fact]
    public async Task ThePlaceCoordinates_ShouldBeStoredAsColumnsBesideTheGeometry()
    {
        // The reason is measurable rather than stylistic: naming an epicentre reads the whole
        // gazetteer, and as geometry that is a point object per row to parse. It also removes the
        // trap that PostGIS has no ST_Y for geography, which is what this asserts is unnecessary.
        var columns = await ColumnTypesAsync("places");

        columns.ShouldContainKey("latitude");
        columns.ShouldContainKey("longitude");
        columns["latitude"].ShouldBe("double precision");

        var centroidType = await ScalarAsync<string>(
            "SELECT udt_name FROM information_schema.columns "
            + "WHERE table_name = 'places' AND column_name = 'centroid';");

        centroidType.ShouldBe("geography");
    }

    [Fact]
    public async Task ThePlaceGazetteer_ShouldBeRequiredToBeAttributed()
    {
        // Guardrail 3 as a database constraint rather than as a convention: a place row cannot
        // exist without the source it came from.
        var nullable = await ScalarAsync<string>(
            "SELECT is_nullable FROM information_schema.columns "
            + "WHERE table_name = 'places' AND column_name = 'data_source_id';");

        nullable.ShouldBe("NO");
    }

    [Fact]
    public async Task TheSpatialColumns_ShouldHaveGistIndexes()
    {
        // Without these, ST_DWithin degrades to a sequential scan over every row and the radius
        // searches move from milliseconds to seconds — the failure this project measured at
        // 500 km and worked around by capping the radius.
        var indexed = await ListAsync(
            "SELECT tablename || '.' || indexname FROM pg_indexes "
            + "WHERE schemaname = 'public' AND indexdef LIKE '%USING gist%' ORDER BY 1;");

        indexed.ShouldNotBeEmpty();

        // The two that carry the platform's own queries: epicentres and place centroids.
        indexed.ShouldContain(entry => entry.StartsWith("earthquake_observations.", StringComparison.Ordinal));
        indexed.ShouldContain(entry => entry.StartsWith("places.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheObservationTable_ShouldCarryItsPostRenameName()
    {
        // The rename from event_observations was applied by hand because EF scaffolded a drop and
        // recreate. PostgreSQL does not rename dependent objects, so this also checks that no
        // index or constraint was left behind under the old name — the state that makes the next
        // migration fail to find them.
        var stale = await ListAsync(
            "SELECT indexname FROM pg_indexes WHERE indexname LIKE 'ix_event_observations%' "
            + "UNION ALL "
            + "SELECT conname FROM pg_constraint WHERE conname LIKE '%event_observations%';");

        stale.ShouldBeEmpty("objects still named after the pre-rename table: " + string.Join(", ", stale));
    }

    private async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);

        var value = await command.ExecuteScalarAsync();

        return value is null or DBNull ? default : (T)value;
    }

    private async Task<List<string>> ListAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();

        var values = new List<string>();

        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private async Task<Dictionary<string, string>> ColumnTypesAsync(string table)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "SELECT column_name, data_type FROM information_schema.columns WHERE table_name = @table;",
            connection);

        command.Parameters.AddWithValue("table", table);

        await using var reader = await command.ExecuteReaderAsync();

        var columns = new Dictionary<string, string>(StringComparer.Ordinal);

        while (await reader.ReadAsync())
        {
            columns[reader.GetString(0)] = reader.GetString(1);
        }

        return columns;
    }
}
