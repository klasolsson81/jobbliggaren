using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "feedback_notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_seeker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sending_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    state_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedback_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "feedback_prompt_suppressions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_seeker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedback_prompt_suppressions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "feedback_submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_seeker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_key = table.Column<Guid>(type: "uuid", nullable: false),
                    page_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: true),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    viewport_width = table.Column<int>(type: "integer", nullable: true),
                    viewport_height = table.Column<int>(type: "integer", nullable: true),
                    screen_width = table.Column<int>(type: "integer", nullable: true),
                    screen_height = table.Column<int>(type: "integer", nullable: true),
                    pixel_ratio = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: true),
                    reported_theme = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    reported_device_class = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    reported_os_family = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    reported_browser_family = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: true),
                    app_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feedback_submissions", x => x.id);
                    table.CheckConstraint("ck_feedback_submissions_rating", "rating BETWEEN 1 AND 5");
                });

            migrationBuilder.CreateIndex(
                name: "ix_feedback_notifications_job_seeker_id",
                table: "feedback_notifications",
                column: "job_seeker_id");

            migrationBuilder.CreateIndex(
                name: "ix_feedback_notifications_state_next_attempt_at",
                table: "feedback_notifications",
                columns: new[] { "state", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ux_feedback_notifications_submission_id",
                table: "feedback_notifications",
                column: "submission_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_feedback_prompt_suppressions_job_seeker_page",
                table: "feedback_prompt_suppressions",
                columns: new[] { "job_seeker_id", "page_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_feedback_submissions_submitted_at",
                table: "feedback_submissions",
                column: "submitted_at");

            migrationBuilder.CreateIndex(
                name: "ux_feedback_submissions_job_seeker_submission_key",
                table: "feedback_submissions",
                columns: new[] { "job_seeker_id", "submission_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feedback_notifications");

            migrationBuilder.DropTable(
                name: "feedback_prompt_suppressions");

            migrationBuilder.DropTable(
                name: "feedback_submissions");
        }
    }
}
