using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// ADR 0058 + ADR 0059 (F6 Prompt 3 BACKEND):
    /// - Adds <c>resumes.language</c> (ResumeLanguage SmartEnum int, 1=Sv default, 2=En).
    ///   <c>defaultValue: 1</c> motsvarar <c>ResumeLanguage.Sv.Value</c> — backfillar
    ///   existerande rader till svenska och matchar Domain-default i Resume.cs.
    ///   Override behövs eftersom EF inte härleder SmartEnum-default till int;
    ///   utan den skulle existerande rader få <c>0</c> som saknar FromValue-mappning.
    /// - Adds denormaliserade projektion-fält <c>latest_role</c>,
    ///   <c>section_count</c>, <c>top_skills</c> per ADR 0059. Backfill av
    ///   dessa sker via runtime self-heal vid första <c>UpdateMasterContent</c>
    ///   (kräver KMS-dekryptering — kan inte göras i migration per ADR 0049
    ///   envelope-encryption-disciplin). Defaultvärden: NULL / 0 / '{}'.
    /// - Adds <c>job_seekers.primary_resume_id</c> (nullable, INGEN DB-FK per
    ///   architect-design: soft-delete-mönster + cascade-handler i
    ///   <c>DeleteResumeCommandHandler</c> håller konsistens).
    /// - Backfill: för varje JobSeeker med ≥1 aktiv Resume men inget
    ///   PrimaryResumeId, sätt PrimaryResumeId till senaste-uppdaterade aktiva
    ///   Resume (per Klas-direktiv F6 Prompt 3).
    /// - <c>top_skills</c>: <c>defaultValueSql: "'{}'"</c> backfillar tomma
    ///   arrays på existerande rader (NOT NULL-kolumn kräver default annars
    ///   bryts AddColumn på icke-tom tabell).
    /// </remarks>
    public partial class AddResumeLanguageDenormProjAndPrimaryResume : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "language",
                table: "resumes",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "latest_role",
                table: "resumes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "section_count",
                table: "resumes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<List<string>>(
                name: "top_skills",
                table: "resumes",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<Guid>(
                name: "primary_resume_id",
                table: "job_seekers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_seekers_primary_resume_id",
                table: "job_seekers",
                column: "primary_resume_id");

            // F6 Prompt 3 BACKEND backfill (Klas-direktiv):
            // För varje aktiv JobSeeker som har ≥1 aktiv Resume men inget
            // PrimaryResumeId, sätt PrimaryResumeId till senaste-uppdaterade
            // aktiva Resume. DISTINCT ON + ORDER BY job_seeker_id, updated_at DESC
            // ger deterministisk pick. Soft-delete-filter (deleted_at IS NULL)
            // på båda sidor speglar global query filter i
            // ResumeConfiguration/JobSeekerConfiguration.
            migrationBuilder.Sql(@"
                UPDATE job_seekers js
                SET primary_resume_id = sub.resume_id
                FROM (
                    SELECT DISTINCT ON (r.job_seeker_id)
                        r.job_seeker_id,
                        r.id AS resume_id
                    FROM resumes r
                    WHERE r.deleted_at IS NULL
                    ORDER BY r.job_seeker_id, r.updated_at DESC
                ) sub
                WHERE js.id = sub.job_seeker_id
                  AND js.primary_resume_id IS NULL
                  AND js.deleted_at IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Backfill reverseras inte — primary_resume_id är derivable från
            // resumes.updated_at (Klas-godkänd data-loss vid rollback).
            migrationBuilder.DropIndex(
                name: "ix_job_seekers_primary_resume_id",
                table: "job_seekers");

            migrationBuilder.DropColumn(
                name: "language",
                table: "resumes");

            migrationBuilder.DropColumn(
                name: "latest_role",
                table: "resumes");

            migrationBuilder.DropColumn(
                name: "section_count",
                table: "resumes");

            migrationBuilder.DropColumn(
                name: "top_skills",
                table: "resumes");

            migrationBuilder.DropColumn(
                name: "primary_resume_id",
                table: "job_seekers");
        }
    }
}
