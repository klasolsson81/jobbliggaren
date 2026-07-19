using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobAdBrowseSortIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_job_ads_status_published_at_id",
                table: "job_ads",
                columns: new[] { "status", "published_at", "id" },
                descending: new[] { false, true, false });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_job_ads_status_published_at_id",
                table: "job_ads");
        }
    }
}
