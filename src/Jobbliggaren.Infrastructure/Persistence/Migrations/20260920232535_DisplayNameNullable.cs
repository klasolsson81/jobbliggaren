using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR 0142 D7, the expand half (#1737): <c>job_seekers.display_name</c> becomes nullable, because a
    /// passwordless account is created before anyone has typed a name. The column is otherwise unchanged.
    /// </summary>
    public partial class DisplayNameNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "display_name",
                table: "job_seekers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);
        }

        /// <summary>
        /// Not EF's scaffold, which backfills every NULL with <c>''</c> before re-adding NOT NULL: an invented
        /// name would compose into the CV header. This refuses while a row without a name exists, and
        /// otherwise re-imposes the constraint.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                DECLARE nameless_count integer;
                BEGIN
                    SELECT count(*) INTO nameless_count FROM job_seekers WHERE display_name IS NULL;
                    IF nameless_count > 0 THEN
                        RAISE EXCEPTION
                            'DisplayNameNullable Down: % job_seekers row(s) have no display_name. The column cannot be made NOT NULL without inventing a name; resolve those rows first.',
                            nameless_count;
                    END IF;
                END $$;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "display_name",
                table: "job_seekers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}
