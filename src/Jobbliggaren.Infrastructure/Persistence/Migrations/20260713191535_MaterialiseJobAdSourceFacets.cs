using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #841 — turn the seven payload-derived STORED generated columns on <c>job_ads</c> into ORDINARY
    /// columns, written in C# at the ingest funnel (<c>JobAd.SetSourcePayload</c>).
    ///
    /// <para>
    /// <b>Why.</b> <c>PurgeStaleRawPayloadsJob</c> nulls <c>raw_payload</c> 30 days after publication
    /// (ADR 0032 §8). Postgres RECOMPUTES a stored generated column on every UPDATE of its base, so the
    /// purge nulled all seven — and the 02:00 snapshot sync rewrote the payload and resurrected them.
    /// Facet-filtered search, the per-user matching engine and the company-watch location filter therefore
    /// dropped still-ACTIVE ads ~21.5 h out of every 24, every day. These seven ARE the "sanitized fields"
    /// ADR 0032 §8 promised to keep indefinitely; they must outlive the payload they were parsed from.
    /// </para>
    ///
    /// <para>
    /// <b>THIS MIGRATION IS HAND-WRITTEN. DO NOT REGENERATE IT.</b> The scaffolded form is a data-loss
    /// event that runs green. <c>dotnet ef migrations add</c> emits seven <c>AlterColumn</c> operations
    /// (which look harmless in C# — there is no <c>DropColumn</c> token anywhere in the file), and the
    /// Npgsql generator turns each one into <c>ALTER TABLE job_ads DROP COLUMN x; ALTER TABLE job_ads ADD
    /// x text;</c>. That would (1) destroy every value in all seven columns on every row — unrecoverable
    /// for any ad whose payload is already purged — and (2) SILENTLY drop all seven partial indexes,
    /// because <c>DROP COLUMN</c> takes every dependent index with it and EF's model snapshot does not
    /// know these indexes exist (all seven were created with raw <c>migrationBuilder.Sql</c>; the fluent
    /// API cannot express a partial index). Green migration, green CI, and filtered search would return
    /// nothing at all, permanently — strictly worse than the bug this fixes. Verified empirically; see
    /// <c>docs/reviews/2026-07-13-841-postgres-probe.md</c> §4.
    /// </para>
    ///
    /// <para>
    /// <b>What we do instead.</b> <c>ALTER COLUMN … DROP EXPRESSION</c> converts a stored generated column
    /// into an ordinary one IN PLACE: measured at 2.5 ms over 50k rows, with NO table rewrite
    /// (<c>relfilenode</c> unchanged), every value retained, and every index retained and
    /// <c>indisvalid</c>. One <c>ALTER TABLE</c> with seven sub-commands → one lock acquisition.
    /// </para>
    ///
    /// <para>
    /// <b>The guard below is the point of this file — and here is precisely what it is and is not.</b> It
    /// counts non-null values per column BEFORE and AFTER the ALTER and <c>RAISE</c>s if a single one is
    /// lost, then asserts all seven columns are ordinary (<c>attgenerated = ''</c>) and all seven indexes
    /// survive <c>indisvalid</c>. Postgres DDL is transactional, so the rollback is real — and it was
    /// mutation-verified: fed the scaffolded <c>DROP COLUMN</c>/<c>ADD COLUMN</c> SQL against 47 000
    /// seeded rows, it aborts with <c>"46998 non-null values before, 0 after"</c> and the data survives.
    /// </para>
    ///
    /// <para>
    /// <b>What it does NOT do</b> (an earlier draft of this comment over-claimed, and that is exactly the
    /// defect this PR exists to kill): it cannot stop someone REGENERATING this file — the scaffolded
    /// migration is a different file and would carry no <c>DO</c> block at all. What stops that is the
    /// test suite: <c>JobAdIndexOracleTests.FacetIndex_SurvivesTheMaterialisation</c> and
    /// <c>JobAdFacetsSurvivePurgeTests</c> run every migration against real Postgres and go red. The
    /// unique, irreplaceable value of the guard below is the value census against <b>production data</b> —
    /// which no Testcontainers test can perform, because no test knows what the real table held.
    /// </para>
    ///
    /// <para>
    /// <b><c>Down</c> is a SCHEMA rollback, not a DATA rollback.</b> Re-creating the generated columns
    /// recomputes them from <c>raw_payload</c> — which is NULL for every purged row, so those values are
    /// gone and the defect returns by construction. That is what rolling back to a defective schema means.
    /// The seven <c>CREATE INDEX</c> statements in <c>Down</c> are load-bearing: <c>DROP COLUMN</c> takes
    /// the indexes with it, so omitting them would leave <c>job_ads</c> silently unindexed — the same trap,
    /// hiding inside the escape hatch, discoverable only during an incident.
    /// </para>
    ///
    /// <para>
    /// <b>Deploy (ADR 0032 §8; CTO ruling Q4).</b> Postgres rejects an INSERT/UPDATE that supplies a value
    /// to a <c>GENERATED ALWAYS … STORED</c> column (SQLSTATE 428C9), so model and schema must ship
    /// together. An OLD Worker against the NEW schema inserts NULL facets SILENTLY — worse than the loud
    /// failure of the reverse. This is therefore a <b>stop-the-world migration for the Worker</b>; the API
    /// may stay up, since it only READS these columns.
    /// </para>
    ///
    /// <para>
    /// Sequence: <b>stop the Worker → migrate → start the new Worker → trigger a full snapshot sync →
    /// verify.</b> The sync repopulates every still-listed ad immediately rather than up to 24 h later (it
    /// is the work the 02:00 cron would do anyway, moved earlier). The trigger is
    /// <c>POST /api/v1/admin/jobs/recurring/sync-platsbanken-snapshot/trigger</c>.
    /// <b>NOT <c>POST /admin/job-ads/sync/platsbanken</c></b> — that endpoint has returned <c>410 Gone</c>
    /// since 2026-05-16 (ADR 0032 §9-amendment X4) and can trigger nothing. The wrong URL was in this
    /// comment on the first pass, and a deploy instruction is read verbatim during an incident, which is
    /// the worst possible moment to discover it. Found by <c>db-migration-writer</c>.
    /// </para>
    ///
    /// <para>
    /// Run the migration through EF (<c>Database.MigrateAsync()</c> / <c>dotnet ef database update</c>),
    /// which wraps it in a transaction so the guard's <c>RAISE</c> actually rolls back. If you instead
    /// apply a generated script with <c>psql</c>, you MUST pass <c>--single-transaction</c>: without it the
    /// <c>DO</c> block would fail and the subsequent <c>INSERT INTO "__EFMigrationsHistory"</c> would still
    /// run, marking a failed migration as applied.
    /// </para>
    /// </summary>
    public partial class MaterialiseJobAdSourceFacets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    facet_columns text[] := ARRAY[
                        'ssyk_concept_id',
                        'region_concept_id',
                        'occupation_group_concept_id',
                        'municipality_concept_id',
                        'employment_type_concept_id',
                        'worktime_extent_concept_id',
                        'organization_number'
                    ];
                    facet_indexes text[] := ARRAY[
                        'ix_job_ads_ssyk_concept_id',
                        'ix_job_ads_region_concept_id',
                        'ix_job_ads_occupation_group_concept_id',
                        'ix_job_ads_municipality_concept_id',
                        'ix_job_ads_employment_type_concept_id',
                        'ix_job_ads_worktime_extent_concept_id',
                        'ix_job_ads_organization_number'
                    ];
                    col          text;
                    idx          text;
                    before_count bigint;
                    after_count  bigint;
                    generated    "char";
                BEGIN
                    -- 1. Census BEFORE. This is the only proof that runs against PRODUCTION data:
                    --    no Testcontainers test can tell us what the real table held.
                    CREATE TEMP TABLE jbl841_before (column_name text PRIMARY KEY, non_null bigint)
                        ON COMMIT DROP;

                    FOREACH col IN ARRAY facet_columns LOOP
                        EXECUTE format('SELECT count(%I) FROM job_ads', col) INTO before_count;
                        INSERT INTO jbl841_before VALUES (col, before_count);
                    END LOOP;

                    -- 2. The actual change. DROP EXPRESSION keeps the stored values and the indexes;
                    --    it only stops Postgres from recomputing the column when raw_payload changes.
                    --    One ALTER TABLE => one ACCESS EXCLUSIVE lock acquisition (~2.5 ms at 50k rows).
                    ALTER TABLE job_ads
                        ALTER COLUMN ssyk_concept_id             DROP EXPRESSION,
                        ALTER COLUMN region_concept_id           DROP EXPRESSION,
                        ALTER COLUMN occupation_group_concept_id DROP EXPRESSION,
                        ALTER COLUMN municipality_concept_id     DROP EXPRESSION,
                        ALTER COLUMN employment_type_concept_id  DROP EXPRESSION,
                        ALTER COLUMN worktime_extent_concept_id  DROP EXPRESSION,
                        ALTER COLUMN organization_number         DROP EXPRESSION;

                    -- 3. NOT ONE VALUE MAY BE LOST. A DROP COLUMN + ADD COLUMN rewrite (what the
                    --    scaffolded migration emits) sends every count to zero and trips this.
                    FOREACH col IN ARRAY facet_columns LOOP
                        EXECUTE format('SELECT count(%I) FROM job_ads', col) INTO after_count;
                        SELECT non_null INTO before_count FROM jbl841_before WHERE column_name = col;

                        IF after_count <> before_count THEN
                            RAISE EXCEPTION
                                '#841 ABORT: job_ads.% lost data. % non-null values before, % after. '
                                'The column was rewritten instead of altered in place — every value is '
                                'gone, and for ads whose raw_payload is already purged they are gone '
                                'FOREVER. Rolling back.',
                                col, before_count, after_count;
                        END IF;
                    END LOOP;

                    -- 4. The columns must now be ORDINARY (attgenerated = ''), or the purge still
                    --    destroys them and this migration achieved nothing while appearing to work.
                    FOREACH col IN ARRAY facet_columns LOOP
                        SELECT attgenerated INTO generated
                          FROM pg_attribute
                         WHERE attrelid = 'job_ads'::regclass AND attname = col;

                        IF generated IS NULL THEN
                            RAISE EXCEPTION '#841 ABORT: job_ads.% does not exist after the ALTER.', col;
                        END IF;

                        IF generated <> '' THEN
                            RAISE EXCEPTION
                                '#841 ABORT: job_ads.% is STILL a generated column (attgenerated=%). '
                                'PurgeStaleRawPayloadsJob would keep destroying it.',
                                col, generated;
                        END IF;
                    END LOOP;

                    -- 5. All seven partial indexes must survive. DROP COLUMN takes dependent indexes
                    --    with it SILENTLY, and EF's model snapshot is blind to these seven (raw SQL) —
                    --    so nothing else in the toolchain would ever tell us they were gone.
                    FOREACH idx IN ARRAY facet_indexes LOOP
                        IF NOT EXISTS (
                            SELECT 1
                              FROM pg_index i
                              JOIN pg_class c ON c.oid = i.indexrelid
                             WHERE c.relname = idx AND i.indisvalid
                        ) THEN
                            RAISE EXCEPTION
                                '#841 ABORT: index % is missing or invalid after the ALTER. Facet-filtered '
                                'search and the matching engine would seq-scan job_ads.', idx;
                        END IF;
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SCHEMA rollback only — see the class comment. Re-generating the columns recomputes them
            // from raw_payload, which is NULL for every purged ad: those values do not come back, and the
            // 21.5h/day defect returns. The CREATE INDEX statements are mandatory, not tidy-up: DROP
            // COLUMN silently removes each dependent index, and EF cannot rebuild what its snapshot never
            // knew about.
            migrationBuilder.Sql("""
                ALTER TABLE job_ads
                    DROP COLUMN ssyk_concept_id,
                    DROP COLUMN region_concept_id,
                    DROP COLUMN occupation_group_concept_id,
                    DROP COLUMN municipality_concept_id,
                    DROP COLUMN employment_type_concept_id,
                    DROP COLUMN worktime_extent_concept_id,
                    DROP COLUMN organization_number;

                ALTER TABLE job_ads
                    ADD COLUMN ssyk_concept_id text
                        GENERATED ALWAYS AS (raw_payload->'occupation'->>'concept_id') STORED,
                    ADD COLUMN region_concept_id text
                        GENERATED ALWAYS AS (raw_payload->'workplace_address'->>'region_concept_id') STORED,
                    ADD COLUMN occupation_group_concept_id text
                        GENERATED ALWAYS AS (raw_payload->'occupation_group'->>'concept_id') STORED,
                    ADD COLUMN municipality_concept_id text
                        GENERATED ALWAYS AS (raw_payload->'workplace_address'->>'municipality_concept_id') STORED,
                    ADD COLUMN employment_type_concept_id text
                        GENERATED ALWAYS AS (raw_payload->'employment_type'->>'concept_id') STORED,
                    ADD COLUMN worktime_extent_concept_id text
                        GENERATED ALWAYS AS (raw_payload->'working_hours_type'->>'concept_id') STORED,
                    ADD COLUMN organization_number text
                        GENERATED ALWAYS AS (raw_payload->'employer'->>'organization_number') STORED;

                CREATE INDEX ix_job_ads_ssyk_concept_id
                    ON job_ads (ssyk_concept_id) WHERE ssyk_concept_id IS NOT NULL;
                CREATE INDEX ix_job_ads_region_concept_id
                    ON job_ads (region_concept_id) WHERE region_concept_id IS NOT NULL;
                CREATE INDEX ix_job_ads_occupation_group_concept_id
                    ON job_ads (occupation_group_concept_id) WHERE occupation_group_concept_id IS NOT NULL;
                CREATE INDEX ix_job_ads_municipality_concept_id
                    ON job_ads (municipality_concept_id) WHERE municipality_concept_id IS NOT NULL;
                CREATE INDEX ix_job_ads_employment_type_concept_id
                    ON job_ads (employment_type_concept_id) WHERE employment_type_concept_id IS NOT NULL;
                CREATE INDEX ix_job_ads_worktime_extent_concept_id
                    ON job_ads (worktime_extent_concept_id) WHERE worktime_extent_concept_id IS NOT NULL;
                CREATE INDEX ix_job_ads_organization_number
                    ON job_ads (organization_number) WHERE organization_number IS NOT NULL;
                """);
        }
    }
}
