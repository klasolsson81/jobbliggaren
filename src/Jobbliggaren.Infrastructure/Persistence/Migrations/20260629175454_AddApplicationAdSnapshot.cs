using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationAdSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "snapshot_captured_at",
                table: "applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "snapshot_company",
                table: "applications",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "snapshot_description",
                table: "applications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "snapshot_expires_at",
                table: "applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "snapshot_municipality_concept_id",
                table: "applications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "snapshot_published_at",
                table: "applications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "snapshot_source",
                table: "applications",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "snapshot_title",
                table: "applications",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "snapshot_url",
                table: "applications",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "snapshot_captured_at",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "snapshot_company",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "snapshot_description",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "snapshot_expires_at",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "snapshot_municipality_concept_id",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "snapshot_published_at",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "snapshot_source",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "snapshot_title",
                table: "applications");

            migrationBuilder.DropColumn(
                name: "snapshot_url",
                table: "applications");
        }
    }
}
