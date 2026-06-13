using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthProviderToUser : Migration
    {
        private static readonly string[] ProviderIndexColumns = ["provider", "provider_user_id"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "provider",
                schema: "identity",
                table: "AspNetUsers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Local");

            migrationBuilder.AddColumn<string>(
                name: "provider_user_id",
                schema: "identity",
                table: "AspNetUsers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_users_provider_provider_user_id",
                schema: "identity",
                table: "AspNetUsers",
                columns: ProviderIndexColumns,
                unique: true,
                filter: "\"provider_user_id\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_asp_net_users_provider_provider_user_id",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "provider",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "provider_user_id",
                schema: "identity",
                table: "AspNetUsers");
        }
    }
}
