using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NpgsqlTypes;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

public sealed class JobAdConfiguration : IEntityTypeConfiguration<JobAd>
{
    public void Configure(EntityTypeBuilder<JobAd> builder)
    {
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id)
            .HasConversion(id => id.Value, value => new JobAdId(value))
            .ValueGeneratedNever();

        builder.Property(j => j.Title).HasMaxLength(300).IsRequired();
        builder.Property(j => j.Description).IsRequired();
        builder.Property(j => j.Url).HasMaxLength(2000).IsRequired();
        builder.Property(j => j.PublishedAt).IsRequired();
        builder.Property(j => j.ExpiresAt);
        builder.Property(j => j.CreatedAt).IsRequired();

        // ADR 0032 §4 — raw_payload som jsonb för debug/replay-artefakter.
        // PII-yta: JobTech-payload kan innehålla rekryterar-PII (namn, email,
        // telefon, firmatecknare). PII-stripping vid ingest (ADR 0032 §8-amendment
        // 2026-05-12, JobTechPayloadSanitizer allowlist default-deny) droppar
        // email/telefon/kontakt och behåller publika fält (namn + org.nr).
        // AT-REST: PLAINTEXT jsonb. (Den tidigare "AWS RDS KMS"-noten var stale —
        // AWS pensionerat, ADR 0066; allt körs lokalt.) DEK-envelope (ADR 0049/0066)
        // är reserverad för hög-känslig ANVÄNDAR-författad PII (CV-/personligt-brev-
        // innehåll), INTE publik ingest-data — så raw_payload DEK-krypteras ej (TD-13
        // återuppväcks ej för denna dataklass). org.nr-vid-vila-posturen: ADR 0087 D8.
        builder.Property(j => j.RawPayload).HasColumnType("jsonb");

        builder.OwnsOne(j => j.Company, company =>
        {
            company.Property(c => c.Name)
                .HasMaxLength(200)
                .IsRequired();
        });

        builder.Property(j => j.Status)
            .HasConversion(s => s.Value, v => JobAdStatus.FromValue(v).Value)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(j => j.Source)
            .HasConversion(s => s.Value, v => JobSource.FromValue(v).Value)
            .HasMaxLength(50)
            .IsRequired();

        // ADR 0032 §4-§5 — ExternalReference owned-type + UNIQUE-index
        // på (Source, ExternalId) WHERE external_id IS NOT NULL (defense-in-depth
        // mot duplicat vid parallella Hangfire-workers). Explicit snake_case
        // HasColumnName för konsekvens med övriga job_ads-kolumner (init-migration).
        builder.OwnsOne(j => j.External, ext =>
        {
            ext.Property(e => e.Source)
                .HasColumnName("external_source")
                .HasConversion(s => s.Value, v => JobSource.FromValue(v).Value)
                .HasMaxLength(50);
            ext.Property(e => e.ExternalId)
                .HasColumnName("external_id")
                .HasMaxLength(100);
            ext.HasIndex(e => new { e.Source, e.ExternalId })
                .IsUnique()
                .HasFilter("\"external_id\" IS NOT NULL")
                .HasDatabaseName("ix_job_ads_external_source_external_id");
        });

        // ── #841 — THE SEVEN SOURCE FACETS. Ordinary columns, written in C#. ────────────
        //
        // WHAT THEY WERE, AND WHY THAT WAS WRONG. Until 2026-07-13 all seven were Postgres
        // STORED generated columns reading raw_payload (F2P9 / F6P6 / F6P7 / #311 D1). The
        // justification on record was "drift omöjlig, ingen C#-skrivväg" — true, and
        // catastrophic: PurgeStaleRawPayloadsJob nulls raw_payload 30 days after published_at
        // (GDPR Art. 5(1)(c)/(e), ADR 0032 §8), and Postgres RECOMPUTES a stored generated
        // column on every UPDATE of its base. So the purge silently nulled all seven, and the
        // 02:00 snapshot sync rewrote the payload and resurrected them. Measured against real
        // Postgres: facet-filtered search, the per-user matching engine and the company-watch
        // location filter dropped still-ACTIVE ads for ~21.5 h of every 24, every day — and
        // CreateApplicationFromJobAd froze a NULL municipality into AdSnapshot, permanently.
        //
        // THE RULE THAT REPLACES IT (executable, not prose — JobAdRawPayloadDerivationGuardTests):
        // NOTHING DURABLE MAY BE DERIVED FROM raw_payload IN THE DATABASE. raw_payload is the
        // only column on this table with a retention TTL; anything computed from it inherits
        // that TTL, silently. The rule is NOT "no generated columns" — search_vector (from
        // title/description) and extracted_lexemes (from extracted_terms) are legitimate and
        // stay, because their bases have no TTL.
        //
        // These seven ARE the "sanitized fields" ADR 0032 §8 promised to retain INDEFINITELY.
        // They are now written by JobAd.SetSourcePayload, atomically with the payload they were
        // parsed from, at the single ingest funnel — the shape extracted_terms has always had,
        // twenty lines below. The ACL (PlatsbankenJobSource.MapFacets) is the ONLY place that
        // knows the JSON paths; the nesting traps and the worktime_extent/working_hours_type
        // name gap live there now.
        //
        // ABOUT .ValueGeneratedNever() — and this note is honest about its own strength, because
        // the claim was mutation-tested rather than assumed. It is NOT, by itself, what keeps the
        // write working: remove it and the seven still persist, because EF's default for a plain
        // scalar property is already ValueGenerated.Never (verified: the model test stays green).
        //
        // What actually protects the write is the ABSENCE of HasComputedColumnSql. A computed
        // column is what makes EF mark a property ValueGeneratedOnAddOrUpdate — and EF OMITS such
        // properties from INSERT/UPDATE, because the database is meant to produce the value. So the
        // dangerous state is the HALF-DONE change: CLR property added, HasComputedColumnSql left
        // behind. Then the C# compiles, SetSourcePayload runs, every InMemory test passes, and
        // Postgres never receives the value — #841's own failure mode (a value that looks written
        // and is functionally absent) re-entering through its own fix. That exact mutation turns
        // JobAdFacetColumnMappingTests AND JobAdRawPayloadDerivationGuardTests red.
        //
        // The flag stays because it says the requirement out loud in the model, and it is what the
        // model test asserts on. It is defence-in-depth and documentation — not the lock.
        //
        // No HasMaxLength (they are `text`; varchar(n) would force a table rewrite) and no
        // HasIndex: the seven partial `WHERE … IS NOT NULL` indexes are raw-SQL/migration-owned
        // and EF's model snapshot is blind to them (the fluent API cannot express a partial
        // index). Those predicates are NULL-SPARSITY, not lifecycle-derived, and they stay —
        // #821 Q2 bans lifecycle-derived predicates on job_ads indexes, nothing else.
        builder.Property(j => j.SsykConceptId)
            .HasColumnName("ssyk_concept_id")
            .ValueGeneratedNever();

        builder.Property(j => j.RegionConceptId)
            .HasColumnName("region_concept_id")
            .ValueGeneratedNever();

        builder.Property(j => j.OccupationGroupConceptId)
            .HasColumnName("occupation_group_concept_id")
            .ValueGeneratedNever();

        builder.Property(j => j.MunicipalityConceptId)
            .HasColumnName("municipality_concept_id")
            .ValueGeneratedNever();

        builder.Property(j => j.EmploymentTypeConceptId)
            .HasColumnName("employment_type_concept_id")
            .ValueGeneratedNever();

        builder.Property(j => j.WorktimeExtentConceptId)
            .HasColumnName("worktime_extent_concept_id")
            .ValueGeneratedNever();

        // #551 — AF's remote/distans classification. An ORDINARY column, written in C# through
        // SetSourcePayload exactly like the seven above — NOT a STORED generated column: the JobSearch
        // response schema carries no per-ad remote field (ADR 0067 Beslut 3, amended 2026-07-18), so there
        // is nothing in raw_payload to derive from, and deriving durable state from raw_payload is the
        // #841 data-loss trap regardless. `bool` (not `bool?`) → `boolean NOT NULL`; the migration adds it
        // with DEFAULT false so existing rows backfill safe-false (the #552-gated conservative direction).
        // A partial index `WHERE remote` is raw-SQL/migration-owned (EF cannot express partial indexes) —
        // remote ads are sparse (~1.4 %) and both readers query `remote = true` (the grade override and
        // the Distans facet filter). No HasMaxLength (boolean), no HasComputedColumnSql (that is the trap).
        builder.Property(j => j.Remote)
            .HasColumnName("remote")
            .ValueGeneratedNever();

        // #311 D1 (ADR 0087) — the employer's organisation number: the CANONICAL follow /
        // attribution key (no fuzzy name matching, the "Volvo×20" trap).
        //
        // ENSKILD FIRMA (GDPR Art. 32, ADR 0087 D8, CLAUDE.md §5 — highest priority): a sole
        // proprietor's org.nr IS a personnummer, in plaintext. It is PLAINTEXT AT REST, and
        // #841 changes one of the reasons why. The old justification said "a generated column
        // cannot be DEK-encrypted" — after this change it is an ordinary column, so that leg is
        // GONE. The posture is unchanged and rests on the leg that already carries D8(b) and
        // that Klas signed on 2026-06-30: QUERYABILITY NECESSITY. A DEK-encrypted column could
        // not carry ix_job_ads_organization_number, could not serve the IN-set in
        // CompanyWatchScanJob, and could not be the GROUP BY key in employer attribution
        // (#824) — the EF strongly-typed-VO Contains trap. See ADR 0087 D8(a) amendment
        // 2026-07-13.
        //
        // RETENTION CHANGED HERE, DELIBERATELY: while this was a generated column it self-nulled
        // with the purge, so an ad that left the feed lost its org.nr after ~30 days by accident.
        // It now persists INDEFINITELY — which is what ADR 0032 §8 always specified for sanitized
        // fields, and what #824 requires (an application filed in 2026 must still be attributable
        // to its employer in 2028). Any Art. 17 erasure path must now clear this column EXPLICITLY;
        // it will not vanish on its own (#842 Tier B tombstone).
        //
        // The protection is at the SURFACING/LOG boundary, not at rest: never logged, never
        // surfaced un-flagged; sole-prop values are masked+flagged via IsPersonnummerShaped.
        builder.Property(j => j.OrganizationNumber)
            .HasColumnName("organization_number")
            .ValueGeneratedNever();

        // F6 P4 (ADR 0062) — FTS search_vector. STORED tsvector generated column,
        // härledd från title + description av PostgreSQL ('swedish'-config för
        // svensk stemming). Shadow-property (ej CLR-property på JobAd — NpgsqlTsVector
        // är en provider-typ, får ej ligga på Domain-aggregatet, CLAUDE.md §2.1).
        // GIN-index skapas i migration F6P4FtsSearchVector. LINQ-referens i
        // JobAdSearchQuery-impl: EF.Property<NpgsqlTsVector>(j, "SearchVector").
        builder.Property<NpgsqlTsVector>("SearchVector")
            .HasColumnName("search_vector")
            .HasColumnType("tsvector")
            .HasComputedColumnSql(
                "to_tsvector('swedish', coalesce(title,'') || ' ' || coalesce(description,''))",
                stored: true);

        // F4-4 (ADR 0071/0074 Path C, dotnet-architect Variant A/3a) — deterministic
        // keyword/skill extraction, jsonb VO. UNLIKE every shadow column above this
        // is ACTIVELY WRITTEN in C# (the extractor at ingest + the local backfill) —
        // NLP + taxonomi-lookup går inte att uttrycka som en Postgres generated
        // column (till skillnad mot ssyk_concept_id/search_vector). NULL = aldrig
        // extraherat; non-null (inkl. tom array) = extraherat. Property-level
        // ValueConverter mot jsonb (speglar SearchCriteria-mappningen).
        // Cast to the non-generic ValueConverter overload: the property is
        // ExtractedTerms? (nullable) — EF maps NULL↔null natively and applies the
        // non-null converter to present values (the documented nullable-property path).
        builder.Property(j => j.ExtractedTerms)
            .HasColumnName("extracted_terms")
            .HasColumnType("jsonb")
            .HasConversion(
                (ValueConverter)ExtractedTermsConversion.Converter,
                ExtractedTermsConversion.Comparer);

        // #842 Tier A — the recruiter contacts (nullable jsonb, ValueConverter path — architect
        // Q3, mirroring extracted_terms above). NULL = never populated / retention-cleared
        // (non-Active); [] = ingested, none found. DELIBERATELY UNINDEXED (T1 CTO 2026-07-16:
        // the erasure matcher is a fail-safe substring scan over an OR-disjunction that already
        // seq-scans; a jsonb_ops GIN bound for a containment design nobody runs would be a dead
        // index that reads as live — the false-signal class this issue exists to kill). NEVER in
        // search_vector (STORED, title+description only — FTS lock L1) and never on a list DTO
        // (lock L4).
        builder.Property(j => j.Contacts)
            .HasColumnName("contacts")
            .HasColumnType("jsonb")
            .HasConversion(
                (ValueConverter)AdContactsConversion.Converter,
                AdContactsConversion.Comparer);

        // STORED generated jsonb companion för F4-6:s overlap-pre-filter
        // (extracted_lexemes ?| @cvLexemes via GIN). Härleds deterministiskt ur den
        // C#-skrivna extracted_terms av Postgres (jsonb_path_query_array med konstant
        // path = IMMUTABLE, verifierat PG 18.3) → drift omöjlig, ingen separat
        // skrivväg. text[]-formen kräver subquery (ej tillåtet i generated columns)
        // → jsonb-formen är den korrekta 3a-varianten. Shadow-property (provider-typ
        // får ej ligga på Domain-aggregatet, CLAUDE.md §2.1); GIN-indexet skapas i
        // migrationen (fluent API kan ej GIN:a en shadow-prop, samma skäl som
        // SearchVector). extracted_lexemes IS NULL ⟺ extracted_terms IS NULL →
        // backfill-predikatet (BackfillJobAdExtractedTermsJob).
        builder.Property<string?>("ExtractedLexemes")
            .HasColumnName("extracted_lexemes")
            .HasColumnType("jsonb")
            .HasComputedColumnSql(
                "jsonb_path_query_array(extracted_terms, '$[*].Lexeme')",
                stored: true);

        // #821 — NO HasQueryFilter on JobAd, deliberately. Unlike the 13 aggregates that
        // carry a real soft-delete axis (each with a SoftDelete() writer), JobAd has none:
        // Status is the sole lifecycle axis and end-user views filter it at the SPOT in
        // JobAdSearchComposition.ApplyFilter. The old `DeletedAt == null` filter was
        // VACUOUS (no writer, ever) and every job_ads index is therefore predicate-FREE —
        // see RetireJobAdDeletedAtAxis. Do not reintroduce a lifecycle-derived index
        // predicate: F6P4aJobAdTrigramIndexPredicateFix cost ~35-50 s of seq scan when an
        // index predicate and a query predicate drifted apart.
        builder.Ignore(j => j.DomainEvents);
    }
}
