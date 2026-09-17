using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTermsAcceptanceToJobSeeker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "privacy_policy_version",
                table: "job_seekers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "terms_accepted_at",
                table: "job_seekers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "terms_version",
                table: "job_seekers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_job_seekers_terms_all_or_none",
                table: "job_seekers",
                sql: "num_nonnulls(terms_accepted_at, terms_version, privacy_policy_version) IN (0, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_job_seekers_terms_all_or_none",
                table: "job_seekers");

            migrationBuilder.DropColumn(
                name: "privacy_policy_version",
                table: "job_seekers");

            migrationBuilder.DropColumn(
                name: "terms_accepted_at",
                table: "job_seekers");

            migrationBuilder.DropColumn(
                name: "terms_version",
                table: "job_seekers");
        }
    }
}
