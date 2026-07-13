using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetireJobAdDeletedAtAxis : Migration
    {
        // #821 — retire the dead JobAd.DeletedAt soft-delete axis.
        //
        // job_ads.deleted_at NEVER had a writer. JobAd has no SoftDelete() method; the only
        // lifecycle transition is Archive(), which sets Status. So the global query filter
        // `HasQueryFilter(j => j.DeletedAt == null)` was VACUOUSLY TRUE for every row and had
        // never excluded anything. It was not harmless, though: the Applications read path
        // delegated "the ad is gone" to it, so PreservedAdPanel (ADR 0086/#315) never rendered
        // in production for two releases (#805-3). Status (Active | Expired | Archived) is the
        // sole lifecycle axis; end-user views filter it at the SPOT in
        // JobAdSearchComposition.ApplyFilter (ADR 0032-amendment 2026-05-23).
        //
        // THIS MIGRATION IS HAND-WRITTEN. The scaffolded version emits DropColumn("deleted_at")
        // and NOTHING ELSE — verified by running it. Trusting it would have been a silent
        // catastrophe:
        //
        //   1. PostgreSQL's ALTER TABLE ... DROP COLUMN: "Indexes and table constraints involving
        //      the column will be automatically dropped as well." No error. No CASCADE required.
        //   2. FIVE job_ads indexes embed `deleted_at` in their partial predicate, and EF's
        //      AppDbContextModelSnapshot knows about NONE of them — every one was created via raw
        //      migrationBuilder.Sql (the Npgsql fluent API cannot express partial/functional/
        //      shadow-property indexes). EF cannot re-create what Postgres silently drops.
        //
        // Net effect of trusting the scaffold: /jobb search (FTS + trigram), the matching engine's
        // lexeme overlap, and title-suggest would all run against job_ads with NO INDEX AT ALL —
        // with a green migration and a green CI. Hence: explicit DROP INDEX by exact name, then
        // DROP COLUMN, then explicit CREATE INDEX. Pinned by JobAdIndexOracleTests.
        //
        // THE NEW INDEXES CARRY NO PREDICATE AT ALL (senior-cto-advisor bind, Q2 = (ii)).
        // The tempting replacement — `WHERE status = 'Active'` — matches every consumer today and
        // is rejected deliberately. PostgreSQL can only use a partial index when the query's WHERE
        // *implies* the index's WHERE, so a partial predicate is an implicit coupling between the
        // storage layer and a query detail, failing silently and catastrophically on drift. This
        // repo has already paid for exactly that coupling once: F6P4aJobAdTrigramIndexPredicateFix
        // (2026-05-21) exists because a `status = 'Active'` index predicate outlived the query that
        // implied it, and q-search sat at ~35-50 s with the index built but unused. A predicate-free
        // index cannot mismatch. Skipping the predicate also costs nothing here: every ad is born
        // Active (the JobAd constructor sets Status = Active unconditionally), so `WHERE status =
        // 'Active'` excludes no row at ingest — it only evicts rows later, at archival, which would
        // ADD GIN maintenance to the bulk ExecuteUpdate archival jobs. Predicate-free is cheaper on
        // the write path and immune on the read path. Pinned by JobAdPlannerUsabilityOracleTests.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // (1) VACUITY GUARD — the central claim, made executable against production data.
            //
            // Everything here rests on "deleted_at has no writer, so the filter excluded no row".
            // No Testcontainers test can prove that about the REAL database; this can. If a single
            // job_ads row anywhere carries a non-null deleted_at, the premise is false: the filter
            // WAS hiding rows, and dropping it would silently RESURRECT them into /jobb search
            // results. The deploy must then stop, not proceed.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM job_ads WHERE deleted_at IS NOT NULL) THEN
                        RAISE EXCEPTION
                            'MIGRATION ABORTED (#821): job_ads.deleted_at is NOT vacuous - % row(s) carry a non-null deleted_at. The soft-delete query filter WAS hiding rows, so dropping it would resurrect them into search results. Do not force this migration; re-open the #821 premise.',
                            (SELECT count(*) FROM job_ads WHERE deleted_at IS NOT NULL);
                    END IF;
                END $$;
                """);

            // (2) Drop the five deleted_at-predicated indexes BY EXACT NAME, before the column
            // goes. Postgres would drop them silently anyway; doing it explicitly puts the intent
            // in the migration and makes the re-create below impossible to forget.
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_search_vector;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_title_lower_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_description_lower_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_extracted_lexemes;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_title_lower_prefix;");

            // (3) The axis itself.
            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "job_ads");

            // (4) Re-create all five, predicate-free. Same columns, same opclasses, same names —
            // only the WHERE clause is gone.
            //
            // CREATE INDEX (not CONCURRENTLY): migrations run inside a transaction via the Migrate
            // schema task, and CONCURRENTLY cannot run in one. Precedent: every index DDL on this table.
            //
            // DEPLOY WINDOW — MEASURED, not asserted. The five serial rebuilds hold ACCESS EXCLUSIVE on
            // job_ads for the whole build. Measured against a synthetic 47k-row / 61 MB job_ads on
            // PostgreSQL 18: search_vector GIN 2.2 s + description trigram GIN 1.6 s + title trigram GIN
            // 0.27 s + extracted_lexemes GIN 0.06 s + title prefix btree 0.05 s = ~4.2 s total. The Api
            // therefore BLOCKS on job_ads for roughly four to five seconds while this migration runs.
            // That is fine for a dev/staging deploy and well inside the migration job's timeout, but it
            // is NOT nothing and must not be discovered in prod: schedule a maintenance window, or accept
            // a ~5 s search stall. (An earlier revision of this comment called the lock "short" without
            // ever measuring it. db-migration-writer called that out, correctly.)
            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_search_vector " +
                "ON job_ads USING gin (search_vector);");

            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_title_lower_trgm " +
                "ON job_ads USING gin (lower(title) gin_trgm_ops);");

            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_description_lower_trgm " +
                "ON job_ads USING gin (lower(description) gin_trgm_ops);");

            // Default jsonb_ops opclass — required by the `?|` (exists-any) operator the matching
            // engine uses; jsonb_path_ops would not support it. Unchanged from F4P4.
            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_extracted_lexemes " +
                "ON job_ads USING gin (extracted_lexemes);");

            // btree text_pattern_ops — left-anchored LIKE for title suggest. This one ALSO loses
            // its `status = 'Active'` leg, per the no-lifecycle-derived-predicate rule above.
            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_title_lower_prefix " +
                "ON job_ads (lower(title) text_pattern_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the column FIRST — the original index predicates reference it.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "job_ads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_search_vector;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_title_lower_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_description_lower_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_extracted_lexemes;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_job_ads_title_lower_prefix;");

            // Re-create with the exact predicates they carried before this migration.
            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_search_vector " +
                "ON job_ads USING gin (search_vector) " +
                "WHERE deleted_at IS NULL;");

            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_title_lower_trgm " +
                "ON job_ads USING gin (lower(title) gin_trgm_ops) " +
                "WHERE deleted_at IS NULL;");

            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_description_lower_trgm " +
                "ON job_ads USING gin (lower(description) gin_trgm_ops) " +
                "WHERE deleted_at IS NULL;");

            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_extracted_lexemes " +
                "ON job_ads USING gin (extracted_lexemes) " +
                "WHERE deleted_at IS NULL;");

            migrationBuilder.Sql(
                "CREATE INDEX ix_job_ads_title_lower_prefix " +
                "ON job_ads (lower(title) text_pattern_ops) " +
                "WHERE status = 'Active' AND deleted_at IS NULL;");
        }
    }
}
