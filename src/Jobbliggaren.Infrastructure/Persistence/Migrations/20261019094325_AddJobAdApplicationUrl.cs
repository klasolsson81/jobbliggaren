using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #2191 — <c>job_ads.application_url</c>, the employer's own application page (JobTech
    /// <c>application_details.url</c>), nullable <c>varchar(2000)</c>. Klas's migration GO of 2026-10-19 names
    /// exactly this. No data step: the nightly snapshot sync rewrites every listed ad through
    /// <c>JobAd.UpdateFromSource</c> and fills it. No index: the
    /// only predicate on it is the recruiter-erasure match's <c>LIKE ANY</c>, which scans its other columns the same way.
    /// </summary>
    public partial class AddJobAdApplicationUrl : Migration
    {
        private const string BoundTheLockWait = "SET LOCAL lock_timeout = '3s';";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BoundTheLockWait);

            migrationBuilder.AddColumn<string>(
                name: "application_url",
                table: "job_ads",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BoundTheLockWait);

            migrationBuilder.DropColumn(
                name: "application_url",
                table: "job_ads");
        }
    }
}
