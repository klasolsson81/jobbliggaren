using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #2191 — <c>job_ads.application_url</c>, the employer's own application page (JobTech
    /// <c>application_details.url</c>), nullable <c>varchar(2000)</c>. Klas's migration GO of 2026-10-19 names
    /// exactly this. No data step: the nightly snapshot sync rewrites every listed ad through
    /// <c>JobAd.UpdateFromSource</c> and fills it. A nullable column with no default is a catalogue-only change in
    /// Postgres, so the ACCESS EXCLUSIVE lock on <c>job_ads</c> is held only for that, with no table rewrite. No index: the
    /// only predicate on it is the recruiter-erasure match's <c>LIKE ANY</c>, which scans its other columns the same way.
    /// </summary>
    public partial class AddJobAdApplicationUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
            migrationBuilder.DropColumn(
                name: "application_url",
                table: "job_ads");
        }
    }
}
