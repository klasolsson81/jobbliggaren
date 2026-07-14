using Jobbliggaren.Infrastructure.CompanyRegister;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// #560 (ADR 0091) — EF Core configuration for the <c>company_register</c> read-model replica
/// (parity <c>TaxonomyConceptConfiguration</c>, ADR 0043). The table is NOT a <c>DbSet</c> on
/// <c>IAppDbContext</c> — it is reached only via the concrete <c>AppDbContext.Set&lt;T&gt;()</c>
/// inside Infrastructure, and populated via the batched raw-SQL <c>ON CONFLICT</c> upsert. This
/// configuration exists for the migration schema + read materialization; it must live in the
/// <c>...Persistence.Configurations</c> namespace (that is what <c>AppDbContext.OnModelCreating</c>
/// scans).
/// </summary>
internal sealed class ScbCompanyRegisterEntryConfiguration
    : IEntityTypeConfiguration<ScbCompanyRegisterEntry>
{
    public void Configure(EntityTypeBuilder<ScbCompanyRegisterEntry> builder)
    {
        builder.ToTable("company_register");

        // 10-digit org.nr is the natural key (Evans natural identity — no surrogate; parity ADR 0087
        // D2's read-model identity). Plaintext varchar(10), no hyphen.
        builder.HasKey(c => c.OrganizationNumber);
        builder.Property(c => c.OrganizationNumber)
            .HasColumnName("organization_number")
            .HasMaxLength(10)
            .ValueGeneratedNever();

        // #884 — Swedish natural-language text. Å/Ä/Ö sort AFTER Z; the cluster default (en_US.utf8)
        // folds them into A and O, putting "Åkesson AB" between "Ahlberg" and "Bok" in the live browse
        // list. On the COLUMN, never on the query or the index: it is a property of what this text IS,
        // and putting it here is what lets every reader be correct without knowing it had to be.
        // Rationale, measurements, scope rule: ADR 0109.
        builder.Property(c => c.Name)
            .HasColumnName("company_name")
            .UseCollation(Collations.Swedish)
            .IsRequired();

        builder.Property(c => c.SeatMunicipalityCode)
            .HasColumnName("sate_kommun_code")
            .HasMaxLength(4)
            .IsRequired();

        // #884 — also Swedish natural-language text (Åre, Älvdalen, Örnsköldsvik, Östersund). Nothing
        // sorts it today, and it is collated anyway: YAGNI governs capability, but a collation is this
        // column's declared TYPE, and declaring text that holds Örnsköldsvik as en_US.utf8 is a false
        // type declaration NOW — not a speculative future need. Marginal cost is one AlterColumn: no
        // index covers this column, so nothing is rebuilt. ADR 0109 §2.
        builder.Property(c => c.SeatMunicipalityName)
            .HasColumnName("sate_kommun_name")
            .UseCollation(Collations.Swedish);

        // text[] for the ≤5 SNI codes (Npgsql auto-maps List<string>). Value comparer per the
        // RecentJobSearch text[] precedent so EF snapshots the collection correctly. The GIN index
        // ADR 0091 deferred ("no consumer until smart-bevakning") is added below — #560's criteria
        // wave IS that consumer.
        var sniComparer = new ValueComparer<List<string>>(
            (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>(), StringComparer.Ordinal),
            v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s)),
            v => v.ToList());
        builder.Property(c => c.SniCodes)
            .HasColumnName("sni_codes")
            .HasColumnType("text[]")
            .IsRequired()
            .Metadata.SetValueComparer(sniComparer);

        builder.Property(c => c.HasAdvertisingBlock)
            .HasColumnName("reklamsparr")
            .IsRequired();

        builder.Property(c => c.ScbStatusRaw)
            .HasColumnName("scb_status_raw")
            .HasMaxLength(2);

        // Coarse lifecycle status stored BY NAME (reorder-safe; parity TaxonomyConcept.Kind).
        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.SyncedAt)
            .HasColumnName("synced_at")
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        // status — dead-company filter (queries read Active only) + the vanish-sweep predicate.
        builder.HasIndex(c => c.Status)
            .HasDatabaseName("ix_company_register_status");

        // sate_kommun_code — the smart-bevakning kommun facet (the `= ANY(@kommun)` half of the
        // criteria predicate).
        builder.HasIndex(c => c.SeatMunicipalityCode)
            .HasDatabaseName("ix_company_register_sate_kommun_code");

        // #560 Fork B1 — GIN on sni_codes: the index-backed half of the criteria browse predicate
        // (`sni_codes && @user_sni`, array overlap). The DEFAULT GIN operator class for text[] is
        // the built-in array_ops, which supports && / @> / <@ / = — no HasOperators() needed, and
        // no raw SQL: the house raw-SQL GIN precedent (F6P4aJobAdTrigramIndexes) exists only
        // because those are EXPRESSION indexes (lower(title) gin_trgm_ops) that EF cannot model.
        // A plain column can be, so it is — the index then lives in the model snapshot and
        // survives future diffs.
        //
        // NOTE for PR-2 (the browse read-path): LINQ does NOT reliably translate to the `&&`
        // operator — a naive .Where(c => c.SniCodes.Any(s => userSni.Contains(s))) compiles to an
        // unnest-subquery that does NOT use this index, which would make the index cosmetic. The
        // port must emit && explicitly and pin it with an EXPLAIN test.
        builder.HasIndex(c => c.SniCodes)
            .HasMethod("gin")
            .HasDatabaseName("ix_company_register_sni_codes_gin");

        // synced_at — the vanish-sweep scans "rows not touched this run" by synced_at < runStartedAt.
        builder.HasIndex(c => c.SyncedAt)
            .HasDatabaseName("ix_company_register_synced_at");

        // #875 (senior-cto-advisor bind 2026-07-14) — company_name, organization_number: the ORDER BY
        // CompanyWatchBrowseQuery.ItemsSql issues (paginated, LIMIT/OFFSET). Without this index a BROAD
        // criterion (one that matches most of the 1,17M-row register) forces Postgres to materialize and
        // Sort() the whole match set to answer LIMIT 20 — measured p95 = 7 066 ms against ADR 0045's
        // 300 ms budget (bound-legal worst case, 1000 SNI x 290 kommuner, production's actual post-sync
        // state — GIN's fastupdate pending list full). With the index the planner walks it IN ORDER and
        // LIMIT stops the walk after 20 rows: p95 drops to 26 ms. The SELECTIVE case (few SNI x few
        // kommun) is untouched — it stays on BitmapAnd(GIN, kommun btree) -> Sort over the handful of
        // hits, which was already fast (32 ms -> 36 ms).
        //
        // organization_number is the PK, appended so the sort key is TOTAL: company_name is not unique in
        // a real register (duplicate legal names are normal) and Postgres sorts are not stable, so a
        // non-total ORDER BY + OFFSET can silently drop or duplicate rows ACROSS pages.
        //
        // Plain, not partial, not covering — a stated choice, not a default. Not partial: there is no
        // WHERE clause this index could usefully carry — company_register has no deleted_at axis, and the
        // status/kommun/SNI predicates are already served by the three indexes above; a partial predicate
        // here would only need to be IMPLIED by every query meant to use it, a footgun (see
        // AddCompanyWatchCriteriaAndRegisterSniGin's user_id index note) this index does not need to take
        // on for zero benefit. Not covering: the query also reads sate_kommun_code / sate_kommun_name /
        // sni_codes via the heap fetch, and INCLUDE-ing them would materially grow this index for a
        // fetch-cost saving that was not what the campaign measured — the win here is the Index Scan +
        // LIMIT early-stop replacing the Sort, not avoiding heap fetches.
        //
        // Deliberately WITHOUT an explicit UseCollation(...) ON THE INDEX: it inherits company_name's
        // COLUMN collation (`swedish`, ICU sv-SE, pinned by #884 above), which is exactly what
        // "ORDER BY company_name" sorts under — they match by construction, not by two places agreeing.
        //
        // CORRECTION (#884, 2026-07-14). This comment used to say that #884 would change the column's
        // collation and "THIS INDEX MUST THEN BE REBUILT" — that a stale index would fall out of the
        // plan silently. MEASURED, AND FALSE: `ALTER TABLE ... ALTER COLUMN ... TYPE text COLLATE ...`
        // rebuilds every dependent index automatically and atomically (1,17M rows: table relfilenode
        // unchanged — no rewrite — while the name index's changed, and it came back indisvalid=t under
        // the new collation). No manual step ever existed, so there was nothing to forget. The warning
        // was real but aimed at the road we did NOT take: it describes what happens if you put COLLATE
        // in the QUERY (or an explicit, divergent one on the index) instead of on the column. Keeping a
        // false warning is not harmless — it is the mirror image of a guard that claims a protection it
        // never had, and it teaches the next reader to discount the warnings that ARE true.
        //
        // The live danger, restated correctly: DO NOT write COLLATE in ItemsSql. The column carries it.
        // A sort requested under a collation the index was not built under is not an error — Postgres
        // just silently Sorts the whole match set (7 066 ms, #875). The guard against that is not this
        // comment; it is CompanyWatchBrowseQueryPlanTests.BroadCriterion_WalksTheNameIndexInOrder_
        // AndStopsEarly, which goes red the moment the two diverge. Do not delete that test.
        builder.HasIndex(c => new { c.Name, c.OrganizationNumber })
            .HasDatabaseName("ix_company_register_company_name_organization_number");
    }
}
