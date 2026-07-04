using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "company_register",
                columns: table => new
                {
                    organization_number = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    company_name = table.Column<string>(type: "text", nullable: false),
                    sate_kommun_code = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    sate_kommun_name = table.Column<string>(type: "text", nullable: true),
                    sni_codes = table.Column<List<string>>(type: "text[]", nullable: false),
                    reklamsparr = table.Column<bool>(type: "boolean", nullable: false),
                    scb_status_raw = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_register", x => x.organization_number);
                });

            migrationBuilder.CreateIndex(
                name: "ix_company_register_sate_kommun_code",
                table: "company_register",
                column: "sate_kommun_code");

            migrationBuilder.CreateIndex(
                name: "ix_company_register_status",
                table: "company_register",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_company_register_synced_at",
                table: "company_register",
                column: "synced_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_register");
        }
    }
}
