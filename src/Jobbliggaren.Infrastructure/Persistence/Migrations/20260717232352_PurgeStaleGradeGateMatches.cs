using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #552 (senior-cto-advisor + Klas plan-approved, this PR) — a scoped, logged DELETE that
    /// purges the <c>user_job_ad_matches</c> rows the #552 grade gate (commit
    /// <c>a06d2f05</c>, <c>MatchScorer.ScoreOrtUnion</c> / <c>ScoreEmploymentMembership</c>) made
    /// impossible to create going forward, but which the nightly <c>BackgroundMatchingJob</c>'s
    /// watermark-incremental scan can never re-visit and correct on its own — its
    /// <c>UNIQUE(user_id, job_ad_id)</c> dedup spine (the "idempotency backstop") SKIPS any pair
    /// already persisted, so a row written under the pre-#552 hole is permanent noise on
    /// <c>/matchningar</c> until this migration removes it.
    ///
    /// <para>
    /// <b>What the hole was.</b> Before #552, an ad whose <c>region_concept_id</c> AND
    /// <c>municipality_concept_id</c> were BOTH <c>NULL</c> (or whose
    /// <c>employment_type_concept_id</c> was <c>NULL</c>) graded <c>NotAssessed</c> on that
    /// dimension even when the user had STATED an ort/employment preference — "cannot assess"
    /// rather than "the ad contradicts you". <c>MatchGradeCalculator</c>'s RB1 floor only fires on
    /// a <c>NoMatch</c> verdict, so such a pair could clear the ladder into Good/Strong/Top and get
    /// persisted (a persisted row is by construction NEVER Basic — see
    /// <c>BackgroundMatchingJob.ToNotifiable</c>, which maps Basic/Related/null to <c>null</c> and
    /// the created match is skipped, never <c>db.UserJobAdMatches.Add</c>'d). #552 changed the
    /// verdict for these two STATED-preference secondaries: a stated preference against an
    /// ad-side <c>NULL</c> now reads <c>NoMatch</c> (empty evidence — nothing to cite), which the
    /// UNCHANGED RB1 floor turns into Basic. Re-creation of the stale class is therefore
    /// impossible post-gate: the scan can never again persist a row this predicate would flag.
    /// </para>
    ///
    /// <para>
    /// <b>The purge predicate</b> (bound: the OWNING user's CURRENT stated preferences are the
    /// correct approximation — see PR discussion): a <c>user_job_ad_matches</c> row is stale when,
    /// joined to its ad (<c>job_ad_id</c>, by-identity — no FK, ADR 0058/0059) and to the owning
    /// job-seeker (<c>user_id = job_seekers.user_id</c>, likewise by-identity), EITHER
    /// <list type="bullet">
    /// <item>the ad states NEITHER ort value (<c>region_concept_id IS NULL AND
    /// municipality_concept_id IS NULL</c>) AND the user currently states an ort preference
    /// (<c>PreferredRegions</c> OR <c>PreferredMunicipalities</c> non-empty — mirrors
    /// <c>ScoreOrtUnion</c>'s <c>stated</c> predicate exactly; <c>ContainmentRegionConceptIds</c> is
    /// NOT part of "stated", it only gates the containment carve-out once the ad HAS a region), OR
    /// </item>
    /// <item>the ad states no employment value (<c>employment_type_concept_id IS NULL</c>) AND the
    /// user currently states an employment preference (<c>PreferredEmploymentTypes</c>
    /// non-empty — mirrors <c>ScoreEmploymentMembership</c>).</item>
    /// </list>
    /// A user who currently states NEITHER ort NOR employment keeps every row untouched (the
    /// vacuous-gate doctrine — an unstated preference never penalises). An ad the user's
    /// preferences explicitly CONTRADICTED (a real <c>NoMatch</c> pre-#552, e.g. wrong stated
    /// municipality) was already floored to Basic before this gate and therefore never persisted —
    /// out of scope for this purge, it does not exist in the table.
    /// </para>
    ///
    /// <para>
    /// <b>jsonb key-existence, not just array-length</b> — <c>job_seekers.match_preferences</c>
    /// (<c>MatchPreferencesConverters</c>) treats a MISSING key as an empty list, and the
    /// <c>jsonb_typeof(...) = 'array'</c> guard (precedent:
    /// <c>C2SearchParityReverseLookupAndRecentExpansion</c>) makes the "non-empty" test defensive
    /// against a key that is present but not (yet) array-shaped, rather than raising
    /// <c>jsonb_array_length</c>'s hard error on a non-array input — a stricter and safer form
    /// than a bare <c>COALESCE(jsonb_array_length(...), 0)</c>, which throws instead of coalescing
    /// when the value exists and is not an array.
    /// </para>
    ///
    /// <para>
    /// <b>Idempotent by construction</b> (a re-run deletes 0 rows): the predicate is a pure
    /// function of CURRENT data, not a "created before X" cutoff — once a qualifying row is
    /// deleted it is gone, and #552 structurally prevents the scan from ever re-creating a row
    /// this predicate would flag (the ad would grade Basic and never reach
    /// <c>db.UserJobAdMatches.Add</c>). No destructive classification: this is a data purge, not a
    /// schema DROP — no column, table, or index is touched.
    /// </para>
    ///
    /// <para>
    /// <b>Down() is an intentional no-op</b> (precedent: <c>NullResumeVersionLegacyContent</c>).
    /// The purged rows cannot be reconstructed — they were background-scan artefacts of a bug, not
    /// user input, and #552 means the very computation that produced them no longer exists to
    /// re-derive them from. There is no schema change to reverse.
    /// </para>
    /// </summary>
    public partial class PurgeStaleGradeGateMatches : Migration
    {
        // SPOT: the exact statement this migration runs, exposed so the Testcontainers
        // integration suite (Jobbliggaren.Api.IntegrationTests, InternalsVisibleTo) exercises the
        // REAL predicate instead of a hand-copied twin that could drift out of sync (repo
        // precedent: NullResumeVersionLegacyContent.NullOutSql / C2SearchParityReverseLookupAndRecentExpansion.BuildReverseLookupSql).
        internal const string PurgeSql =
            """
            DO $$
            DECLARE
                deleted_count integer;
            BEGIN
                DELETE FROM user_job_ad_matches m
                USING job_ads a, job_seekers js
                WHERE m.job_ad_id = a.id
                  AND m.user_id = js.user_id
                  AND (
                      (
                          a.region_concept_id IS NULL
                          AND a.municipality_concept_id IS NULL
                          AND (
                              (CASE WHEN jsonb_typeof(js.match_preferences -> 'PreferredRegions') = 'array'
                                    THEN jsonb_array_length(js.match_preferences -> 'PreferredRegions')
                                    ELSE 0 END) > 0
                              OR
                              (CASE WHEN jsonb_typeof(js.match_preferences -> 'PreferredMunicipalities') = 'array'
                                    THEN jsonb_array_length(js.match_preferences -> 'PreferredMunicipalities')
                                    ELSE 0 END) > 0
                          )
                      )
                      OR
                      (
                          a.employment_type_concept_id IS NULL
                          AND (CASE WHEN jsonb_typeof(js.match_preferences -> 'PreferredEmploymentTypes') = 'array'
                                    THEN jsonb_array_length(js.match_preferences -> 'PreferredEmploymentTypes')
                                    ELSE 0 END) > 0
                      )
                  );

                GET DIAGNOSTICS deleted_count = ROW_COUNT;
                RAISE NOTICE '#552 stale-match purge (PurgeStaleGradeGateMatches): deleted % row(s) from user_job_ad_matches whose ad, judged against the owning user''s CURRENT stated ort/employment preferences, is now gate-floored to Basic under the #552 grade gate (MatchScorer.ScoreOrtUnion / ScoreEmploymentMembership, commit a06d2f05) and can never re-persist post-gate.', deleted_count;
            END $$;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(PurgeSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentional no-op (precedent: NullResumeVersionLegacyContent). The deleted rows
            // were background-scan artefacts of the pre-#552 hole, not user input — there is
            // nothing to restore them FROM, and #552 removed the very computation (the old
            // NotAssessed verdict on a stated-preference-vs-NULL-ad pair) that produced them, so
            // they cannot even be honestly re-derived. No schema changed, so there is nothing
            // structural to reverse either.
        }
    }
}
