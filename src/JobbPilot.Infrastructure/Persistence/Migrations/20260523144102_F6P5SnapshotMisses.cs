using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF Core-genererat: literal-arrays i CreateIndex är konventionellt

namespace JobbPilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F6P5SnapshotMisses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_ad_snapshot_misses",
                columns: table => new
                {
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    miss_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    first_missed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_missed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_ad_snapshot_misses", x => new { x.source, x.external_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_ad_snapshot_misses_miss_count",
                table: "job_ad_snapshot_misses",
                columns: new[] { "source", "miss_count" },
                filter: "\"miss_count\" >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_ad_snapshot_misses");
        }
    }
}
