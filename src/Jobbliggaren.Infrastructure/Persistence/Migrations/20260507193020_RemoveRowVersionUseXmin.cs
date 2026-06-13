using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRowVersionUseXmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "row_version",
                table: "applications");
            // xmin är en PostgreSQL-systemkolumn — ingen ADD COLUMN behövs
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                table: "applications",
                type: "bytea",
                rowVersion: true,
                nullable: false,
                defaultValue: Array.Empty<byte>());
        }
    }
}
