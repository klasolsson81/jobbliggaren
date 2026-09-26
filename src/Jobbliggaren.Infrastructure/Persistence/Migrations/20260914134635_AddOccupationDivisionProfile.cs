using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOccupationDivisionProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "occupation_division_profile_runs",
                columns: table => new
                {
                    profile_key = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    profiled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    occupation_groups_profiled = table.Column<int>(type: "integer", nullable: false),
                    rows_written = table.Column<int>(type: "integer", nullable: false),
                    ads_counted = table.Column<int>(type: "integer", nullable: false),
                    ads_not_in_register = table.Column<int>(type: "integer", nullable: false),
                    ads_in_register_without_sni = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_occupation_division_profile_runs", x => x.profile_key);
                });

            migrationBuilder.CreateTable(
                name: "occupation_division_profiles",
                columns: table => new
                {
                    occupation_group_concept_id = table.Column<string>(type: "text", nullable: false),
                    division_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    ad_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_occupation_division_profiles", x => new { x.occupation_group_concept_id, x.division_code });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "occupation_division_profile_runs");

            migrationBuilder.DropTable(
                name: "occupation_division_profiles");
        }
    }
}
