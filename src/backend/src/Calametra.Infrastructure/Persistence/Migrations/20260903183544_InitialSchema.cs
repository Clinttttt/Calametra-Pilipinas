using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Calametra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "data_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    agency = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    dataset_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    access_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attribution = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    source_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    terms_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    is_redistributable = table.Column<bool>(type: "boolean", nullable: false),
                    is_authoritative_for_philippines = table.Column<bool>(type: "boolean", nullable: false),
                    minimum_reliable_magnitude = table.Column<double>(type: "double precision", nullable: true),
                    coverage_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    source_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    last_retrieved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_payload_checksum = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_sources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hazard_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    canonical_occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    canonical_epicenter = table.Column<Point>(type: "geography (Point, 4326)", nullable: false),
                    preferred_observation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hazard_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "places",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    psgc_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    parent_place_id = table.Column<Guid>(type: "uuid", nullable: true),
                    centroid = table.Column<Point>(type: "geography (Point, 4326)", nullable: false),
                    boundary = table.Column<MultiPolygon>(type: "geography (MultiPolygon, 4326)", nullable: true),
                    population_estimate = table.Column<long>(type: "bigint", nullable: true),
                    population_data_source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    population_as_of = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_places", x => x.id);
                    table.ForeignKey(
                        name: "fk_places_places_parent_place_id",
                        column: x => x.parent_place_id,
                        principalTable: "places",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hazard_layers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hazard_type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    lens = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    display_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    delivery_mode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    wms_endpoint = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    wms_layer_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    supports_feature_info = table.Column<bool>(type: "boolean", nullable: false),
                    explainer = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    interpretation_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_enabled_by_default = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hazard_layers", x => x.id);
                    table.ForeignKey(
                        name: "fk_hazard_layers_data_sources_data_source_id",
                        column: x => x.data_source_id,
                        principalTable: "data_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "event_observations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hazard_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_event_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    epicenter = table.Column<Point>(type: "geography (Point, 4326)", nullable: false),
                    magnitude_value = table.Column<double>(type: "double precision", nullable: true),
                    magnitude_scale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    depth_kilometres = table.Column<double>(type: "double precision", nullable: true),
                    depth_quality = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    retrieved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_observations", x => x.id);
                    table.ForeignKey(
                        name: "fk_event_observations_data_sources_data_source_id",
                        column: x => x.data_source_id,
                        principalTable: "data_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_observations_hazard_events_hazard_event_id",
                        column: x => x.hazard_event_id,
                        principalTable: "hazard_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_track_points",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hazard_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    position = table.Column<Point>(type: "geography (Point, 4326)", nullable: false),
                    max_sustained_wind_knots = table.Column<int>(type: "integer", nullable: true),
                    minimum_pressure_millibars = table.Column<int>(type: "integer", nullable: true),
                    classification = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    distance_to_land_km = table.Column<double>(type: "double precision", nullable: true),
                    is_landfall = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_track_points", x => x.id);
                    table.ForeignKey(
                        name: "fk_event_track_points_data_sources_data_source_id",
                        column: x => x.data_source_id,
                        principalTable: "data_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_event_track_points_hazard_events_hazard_event_id",
                        column: x => x.hazard_event_id,
                        principalTable: "hazard_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hazard_features",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    hazard_layer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    classification = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    geometry = table.Column<Geometry>(type: "geography (Geometry, 4326)", nullable: false),
                    attributes_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hazard_features", x => x.id);
                    table.ForeignKey(
                        name: "fk_hazard_features_data_sources_data_source_id",
                        column: x => x.data_source_id,
                        principalTable: "data_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hazard_features_hazard_layers_hazard_layer_id",
                        column: x => x.hazard_layer_id,
                        principalTable: "hazard_layers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_data_sources_slug",
                table: "data_sources",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_observations_data_source_id_external_event_id",
                table: "event_observations",
                columns: new[] { "data_source_id", "external_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_observations_depth_quality_depth_kilometres",
                table: "event_observations",
                columns: new[] { "depth_quality", "depth_kilometres" });

            migrationBuilder.CreateIndex(
                name: "ix_event_observations_epicenter",
                table: "event_observations",
                column: "epicenter")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_event_observations_hazard_event_id_data_source_id",
                table: "event_observations",
                columns: new[] { "hazard_event_id", "data_source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_observations_magnitude_value_magnitude_scale",
                table: "event_observations",
                columns: new[] { "magnitude_value", "magnitude_scale" });

            migrationBuilder.CreateIndex(
                name: "ix_event_track_points_data_source_id",
                table: "event_track_points",
                column: "data_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_track_points_hazard_event_id_captured_at",
                table: "event_track_points",
                columns: new[] { "hazard_event_id", "captured_at" });

            migrationBuilder.CreateIndex(
                name: "ix_event_track_points_position",
                table: "event_track_points",
                column: "position")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_hazard_events_canonical_epicenter",
                table: "hazard_events",
                column: "canonical_epicenter")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_hazard_events_type_canonical_occurred_at",
                table: "hazard_events",
                columns: new[] { "type", "canonical_occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_hazard_features_data_source_id_external_id",
                table: "hazard_features",
                columns: new[] { "data_source_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hazard_features_geometry",
                table: "hazard_features",
                column: "geometry")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_hazard_features_hazard_layer_id",
                table: "hazard_features",
                column: "hazard_layer_id");

            migrationBuilder.CreateIndex(
                name: "ix_hazard_layers_data_source_id",
                table: "hazard_layers",
                column: "data_source_id");

            migrationBuilder.CreateIndex(
                name: "ix_hazard_layers_lens_sort_order",
                table: "hazard_layers",
                columns: new[] { "lens", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_places_boundary",
                table: "places",
                column: "boundary")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_places_centroid",
                table: "places",
                column: "centroid")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_places_kind_name",
                table: "places",
                columns: new[] { "kind", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_places_parent_place_id",
                table: "places",
                column: "parent_place_id");

            migrationBuilder.CreateIndex(
                name: "ix_places_psgc_code",
                table: "places",
                column: "psgc_code",
                unique: true,
                filter: "psgc_code IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_observations");

            migrationBuilder.DropTable(
                name: "event_track_points");

            migrationBuilder.DropTable(
                name: "hazard_features");

            migrationBuilder.DropTable(
                name: "places");

            migrationBuilder.DropTable(
                name: "hazard_events");

            migrationBuilder.DropTable(
                name: "hazard_layers");

            migrationBuilder.DropTable(
                name: "data_sources");
        }
    }
}
