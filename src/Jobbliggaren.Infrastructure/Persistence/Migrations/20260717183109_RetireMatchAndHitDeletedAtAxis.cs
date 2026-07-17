using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jobbliggaren.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// #868 (senior-cto-advisor bind 2026-07-17, D1 = retire both) — drops
    /// <c>user_job_ad_matches.deleted_at</c> and <c>followed_company_ad_hits.deleted_at</c>.
    ///
    /// <para>
    /// <b>What these columns were.</b> A soft-delete stamp on each of two aggregates whose
    /// <c>SoftDelete()</c> method had ZERO production callers (grep-verified). The EF query filter
    /// <c>DeletedAt == null</c> was therefore VACUOUSLY TRUE for every row and never excluded one — a
    /// decoy axis: a reader who found the method concluded the axis was live. Same disease as #821
    /// (<c>JobAd.DeletedAt</c>, which had no method) and #915 (<c>CompanyWatchCriterion</c>), one
    /// aggregate family over. The method, property and filter go in the same change-reason; this
    /// migration removes the columns they backed. Art. 17 erasure is unaffected: <c>AccountHardDeleter</c>
    /// HARD-deletes both aggregates (<c>RemoveRange</c>), never via this axis — so removing it changes no
    /// erasure behaviour (the two now-redundant <c>IgnoreQueryFilters()</c> calls went with it, and that
    /// removal is machine-enforced by <c>AccountHardDeleteCascadeFitnessTests</c>' iff-invariant).
    /// </para>
    ///
    /// <para>
    /// <b>Why a plain DROP COLUMN is safe — measured, not assumed.</b> <c>DROP COLUMN</c> silently drops
    /// every index/constraint/default depending on the column, and the EF model snapshot cannot see that
    /// happen (#821's hard-won lesson). Verified against the tree:
    /// <list type="bullet">
    ///   <item><b>No dependent index.</b> Neither table has an index whose predicate names
    ///     <c>deleted_at</c> — all four <c>user_job_ad_matches</c> indexes and all three
    ///     <c>followed_company_ad_hits</c> indexes are plain B-trees with no <c>WHERE</c> clause
    ///     (<c>FollowedCompanyAdHitConfiguration</c> documents its dispatch index as a FULL B-tree
    ///     explicitly). So the DROP COLUMN can take no index with it. Pinned physically after migration
    ///     by <c>UserJobAdMatchPersistenceTests</c> / <c>FollowedCompanyAdHitPersistenceTests</c>
    ///     (<c>..._Exists_AndIsNotPartial</c> reads <c>pg_indexes</c>; <c>DeletedAtColumn_IsPhysicallyGone</c>
    ///     reads <c>information_schema.columns</c>).</item>
    ///   <item><b>Ordinary column, not computed.</b> A plain stored <c>timestamp with time zone</c> — no
    ///     <c>DROP EXPRESSION</c> data-loss trap.</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// <b>Down() is genuinely runnable</b> (the #884 lesson: a scaffolded Down that cannot execute is not
    /// a rollback). It restores each column with its ORIGINAL definition — nullable, no default — so
    /// re-applying yields the exact pre-drop shape. It restores no data, and that is faithful rather than
    /// lossy: there was never any (no writer). Rolling back gives the empty columns back; it does not give
    /// you a soft-delete mechanism, because that lived in C# and is now removed.
    /// </para>
    /// </summary>
    public partial class RetireMatchAndHitDeletedAtAxis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // (1) VACUITY GUARD per table (#821 bar, senior-cto-advisor D4) — the central claim made
            // executable against production data. Everything rests on "deleted_at has no writer, so the
            // filter excluded no row". No Testcontainers test can prove that about the REAL database; this
            // can. If a single row carries a non-null deleted_at, the premise is false: the filter WAS
            // hiding rows, and dropping it would silently RESURRECT them into the user's match/hit
            // surfaces. The deploy must then STOP, not proceed. (Stronger than #915's plain drop, per the
            // CTO: these two are user-visible surfaces where a resurrected row would show a stale match.)
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM user_job_ad_matches WHERE deleted_at IS NOT NULL) THEN
                        RAISE EXCEPTION
                            'MIGRATION ABORTED (#868): user_job_ad_matches.deleted_at is NOT vacuous - % row(s) carry a non-null deleted_at. The soft-delete query filter WAS hiding rows, so dropping it would resurrect them into match surfaces. Do not force this migration; re-open the #868 premise.',
                            (SELECT count(*) FROM user_job_ad_matches WHERE deleted_at IS NOT NULL);
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM followed_company_ad_hits WHERE deleted_at IS NOT NULL) THEN
                        RAISE EXCEPTION
                            'MIGRATION ABORTED (#868): followed_company_ad_hits.deleted_at is NOT vacuous - % row(s) carry a non-null deleted_at. The soft-delete query filter WAS hiding rows, so dropping it would resurrect them into follow surfaces. Do not force this migration; re-open the #868 premise.',
                            (SELECT count(*) FROM followed_company_ad_hits WHERE deleted_at IS NOT NULL);
                    END IF;
                END $$;
                """);

            // (2) The axis itself. No index dance: neither column is named by any index predicate
            // (verified — plain B-trees only), so DROP COLUMN takes no index with it.
            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "user_job_ad_matches");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "followed_company_ad_hits");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The original definitions, verbatim: nullable, no default. Restores the shape; there is no
            // data to restore (no writer ever set the stamp).
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "user_job_ad_matches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "followed_company_ad_hits",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
