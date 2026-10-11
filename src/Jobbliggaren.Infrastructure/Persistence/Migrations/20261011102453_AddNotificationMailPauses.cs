using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR 0164 (#2107) — the three e-mail pause stamps live in the <c>job_seekers.preferences</c> jsonb, so
    /// this migration moves only the model snapshot and carries no DDL. A row written before it has no such
    /// keys, which reads as "not paused".
    /// </summary>
    public partial class AddNotificationMailPauses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
