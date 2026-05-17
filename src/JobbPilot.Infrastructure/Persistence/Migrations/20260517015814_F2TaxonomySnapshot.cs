using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobbPilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F2TaxonomySnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "taxonomy_concepts",
                columns: table => new
                {
                    concept_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    label = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    parent_concept_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_taxonomy_concepts", x => x.concept_id);
                });

            migrationBuilder.CreateTable(
                name: "taxonomy_snapshot_meta",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    taxonomy_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    seeded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_taxonomy_snapshot_meta", x => x.id);
                    table.CheckConstraint("ck_taxonomy_snapshot_meta_singleton", "id = 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_taxonomy_concepts_kind",
                table: "taxonomy_concepts",
                column: "kind");

            migrationBuilder.CreateIndex(
                name: "ix_taxonomy_concepts_parent_concept_id",
                table: "taxonomy_concepts",
                column: "parent_concept_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "taxonomy_concepts");

            migrationBuilder.DropTable(
                name: "taxonomy_snapshot_meta");
        }
    }
}
