using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobbPilot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDataKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_data_keys",
                columns: table => new
                {
                    job_seeker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dek_version = table.Column<int>(type: "integer", nullable: false),
                    wrapped_dek = table.Column<byte[]>(type: "bytea", nullable: false),
                    cmk_key_id = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_data_keys", x => new { x.job_seeker_id, x.dek_version });
                });

            // TD-13 / ADR 0049 / CTO-triage FRÅGA 2 (2026-05-18). EF-confign har
            // MEDVETET ingen navigation till JobSeeker (keyless-på-aggregat-vis,
            // ingen navigations-yta in i Application — ISP/Clean Arch, Martin
            // 2017 kap. 10/22). FK-integriteten läggs därför på DB-nivå via raw
            // SQL i stället för EF-relation (samma råsql-stil som F2P9-
            // migrationens partial-index, ADR 0024-precedens).
            //
            // ON DELETE CASCADE = defense-in-depth, INTE primär raderingsväg:
            // crypto-erasure raderar wrapped-DEK-raderna EXPLICIT i hard-delete-
            // transaktionen (C6) innan JobSeeker-raden tas bort. Cascade är
            // säkerhetsnät om en JobSeeker-rad ändå hard-deletas utan att C6-
            // hooken körts — en orphan wrapped-DEK utan ägare får aldrig
            // kvarstå (GDPR: ingen krypterad PII-nyckel utan känd subjekt-
            // koppling). PK (job_seeker_id, dek_version) ger redan vänster-
            // prefix-index för "WHERE job_seeker_id =" — inget redundant
            // separat index läggs.
            migrationBuilder.Sql(
                "ALTER TABLE user_data_keys " +
                "ADD CONSTRAINT fk_user_data_keys_job_seekers " +
                "FOREIGN KEY (job_seeker_id) REFERENCES job_seekers (id) " +
                "ON DELETE CASCADE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // DROP TABLE droppar fk_user_data_keys_job_seekers implicit
            // (constraint lever på user_data_keys, ej på job_seekers).
            // IF EXISTS för Down-idempotens — samma stil som F2P9-
            // migrationens "DROP INDEX IF EXISTS".
            migrationBuilder.Sql("DROP TABLE IF EXISTS user_data_keys;");
        }
    }
}
