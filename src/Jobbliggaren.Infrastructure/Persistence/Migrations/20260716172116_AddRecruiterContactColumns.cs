using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecruiterContactColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "contacts",
                table: "job_ads",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "snapshot_contacts",
                table: "applications",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "contacts",
                table: "job_ads");

            migrationBuilder.DropColumn(
                name: "snapshot_contacts",
                table: "applications");
        }
    }
}
