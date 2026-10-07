using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountAccessSuspension : Migration
    {
        internal const string RollbackGuardSql =
            """
            LOCK TABLE identity.account_security_epoch IN ACCESS EXCLUSIVE MODE;
            DO $guard$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM identity.account_security_epoch WHERE id = 1 AND value = 0
                ) THEN
                    RAISE EXCEPTION 'Account access suspension cannot be reversed after the security epoch has been used.'
                        USING ERRCODE = '55000';
                END IF;
            END
            $guard$;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "access_revision",
                schema: "identity",
                table: "AspNetUsers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "credential_cutoff",
                schema: "identity",
                table: "AspNetUsers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "is_suspended",
                schema: "identity",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "account_security_epoch",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_security_epoch", x => x.id);
                    table.CheckConstraint("ck_account_security_epoch_nonnegative", "value >= 0");
                    table.CheckConstraint("ck_account_security_epoch_singleton", "id = 1");
                });

            migrationBuilder.InsertData(
                schema: "identity",
                table: "account_security_epoch",
                columns: new[] { "id", "value" },
                values: new object[] { 1, 0L });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(RollbackGuardSql);

            migrationBuilder.DropTable(
                name: "account_security_epoch",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "access_revision",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "credential_cutoff",
                schema: "identity",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "is_suspended",
                schema: "identity",
                table: "AspNetUsers");
        }
    }
}
