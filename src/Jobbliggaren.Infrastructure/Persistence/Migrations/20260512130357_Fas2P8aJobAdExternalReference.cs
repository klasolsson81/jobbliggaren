using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Fas2P8aJobAdExternalReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_id",
                table: "job_ads",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_source",
                table: "job_ads",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "raw_payload",
                table: "job_ads",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_ads_external_source_external_id",
                table: "job_ads",
                columns: ["external_source", "external_id"],
                unique: true,
                filter: "\"external_id\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_job_ads_external_source_external_id",
                table: "job_ads");

            migrationBuilder.DropColumn(
                name: "external_id",
                table: "job_ads");

            migrationBuilder.DropColumn(
                name: "external_source",
                table: "job_ads");

            migrationBuilder.DropColumn(
                name: "raw_payload",
                table: "job_ads");
        }
    }
}
