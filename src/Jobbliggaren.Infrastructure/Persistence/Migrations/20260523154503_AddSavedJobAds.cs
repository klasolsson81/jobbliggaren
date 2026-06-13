using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1861 // Avoid constant arrays as arguments — EF Core scaffolds new[] in CreateIndex.

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// F6 P5 Punkt 2 Del A — SavedJobAds (bokmärkta annonser per JobSeeker).
    /// - Skapar <c>saved_job_ads</c> med UNIQUE-invariant
    ///   <c>ux_saved_job_ads_seeker_jobad</c> på (job_seeker_id, job_ad_id).
    ///   SaveJobAdCommandHandler hanterar race via ADR 0032 §5 ON CONFLICT-
    ///   mönstret (try/catch DbUpdateException + IDbExceptionInspector).
    /// - Sekundär-index <c>ix_saved_job_ads_seeker_created_at</c>
    ///   (job_seeker_id ASC, created_at DESC) för list-query ordering.
    /// - Ingen DB-FK till job_ads eller job_seekers per ADR 0011 strongly-
    ///   typed soft-reference-mönstret; cascade hanteras explicit i
    ///   AccountHardDeleter (ADR 0024 amend).
    /// </remarks>
    public partial class AddSavedJobAds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "saved_job_ads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_seeker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_ad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_job_ads", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_saved_job_ads_seeker_created_at",
                table: "saved_job_ads",
                columns: new[] { "job_seeker_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_saved_job_ads_seeker_jobad",
                table: "saved_job_ads",
                columns: new[] { "job_seeker_id", "job_ad_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "saved_job_ads");
        }
    }
}

#pragma warning restore CA1861
