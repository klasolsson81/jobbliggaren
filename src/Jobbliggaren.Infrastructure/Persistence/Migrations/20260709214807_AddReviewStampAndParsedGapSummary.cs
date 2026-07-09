using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewStampAndParsedGapSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reviewed_rubric_version",
                table: "resumes",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gap_summary",
                table: "parsed_resumes",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reviewed_rubric_version",
                table: "resumes");

            migrationBuilder.DropColumn(
                name: "gap_summary",
                table: "parsed_resumes");
        }
    }
}
