using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Calametra.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Renames <c>event_observations</c> to <c>earthquake_observations</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Hand-written, replacing what EF Core scaffolded.</b> The generated migration was
    /// <c>DropTable</c> followed by <c>CreateTable</c>, because EF compares model snapshots
    /// and cannot distinguish a rename from a delete plus an unrelated addition. Applying it
    /// would have silently destroyed all 27,241 observations — the entire ingested archive —
    /// while reporting success.
    /// </para>
    /// <para>
    /// <c>ALTER TABLE ... RENAME TO</c> preserves every row, and is effectively instantaneous
    /// because PostgreSQL only rewrites catalogue entries rather than touching heap data.
    /// </para>
    /// <para>
    /// <b>Indexes and constraints must be renamed separately.</b> PostgreSQL does not rename
    /// dependent objects when a table is renamed, so without the statements below the database
    /// would keep index and constraint names starting <c>ix_event_observations_</c>. That does
    /// not break queries, but it does leave the schema disagreeing with EF's model snapshot —
    /// and the next migration that touches one of these indexes would fail to find it.
    /// </para>
    /// <para>
    /// The primary key uses <c>RENAME CONSTRAINT</c> rather than <c>ALTER INDEX</c>: the
    /// constraint owns its backing index, so renaming the constraint renames both, whereas
    /// renaming only the index would leave the constraint name stale.
    /// </para>
    /// </remarks>
    public partial class RenameEventObservationsToEarthquakeObservations : Migration
    {
        private const string OldTable = "event_observations";
        private const string NewTable = "earthquake_observations";

        /// <summary>Index suffixes, identical either side of the rename.</summary>
        private static readonly string[] IndexSuffixes =
        [
            "data_source_id_external_event_id",
            "depth_quality_depth_kilometres",
            "epicenter",
            "hazard_event_id_data_source_id",
            "magnitude_value_magnitude_scale",
        ];

        /// <summary>Foreign key suffixes, identical either side of the rename.</summary>
        private static readonly string[] ForeignKeySuffixes =
        [
            "data_sources_data_source_id",
            "hazard_events_hazard_event_id",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            Rename(migrationBuilder, OldTable, NewTable);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            Rename(migrationBuilder, NewTable, OldTable);
        }

        /// <summary>
        /// Renames the table and every dependent object, in the only order that works:
        /// the table first, because the later statements address it by its new name.
        /// </summary>
        private static void Rename(MigrationBuilder migrationBuilder, string from, string to)
        {
            migrationBuilder.RenameTable(name: from, newName: to);

            migrationBuilder.Sql(
                $"ALTER TABLE {to} RENAME CONSTRAINT pk_{from} TO pk_{to};");

            foreach (var suffix in ForeignKeySuffixes)
            {
                migrationBuilder.Sql(
                    $"ALTER TABLE {to} RENAME CONSTRAINT fk_{from}_{suffix} TO fk_{to}_{suffix};");
            }

            foreach (var suffix in IndexSuffixes)
            {
                migrationBuilder.RenameIndex(
                    name: $"ix_{from}_{suffix}",
                    newName: $"ix_{to}_{suffix}",
                    table: to);
            }
        }
    }
}
