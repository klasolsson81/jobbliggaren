using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyWatchCriterionMembers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_watch_criterion_materialisations",
                columns: table => new
                {
                    criterion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    member_count = table.Column<int>(type: "integer", nullable: false),
                    excluded_personnummer_shaped = table.Column<int>(type: "integer", nullable: false),
                    materialised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_watch_criterion_materialisations", x => x.criterion_id);
                    table.ForeignKey(
                        name: "fk_company_watch_criterion_materialisations_criterion_id",
                        column: x => x.criterion_id,
                        principalTable: "company_watch_criteria",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_watch_criterion_members",
                columns: table => new
                {
                    criterion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_number = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_watch_criterion_members", x => new { x.criterion_id, x.organization_number });
                    table.ForeignKey(
                        name: "fk_company_watch_criterion_members_criterion_id",
                        column: x => x.criterion_id,
                        principalTable: "company_watch_criteria",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_watch_criterion_materialisations");

            migrationBuilder.DropTable(
                name: "company_watch_criterion_members");
        }
    }
}
