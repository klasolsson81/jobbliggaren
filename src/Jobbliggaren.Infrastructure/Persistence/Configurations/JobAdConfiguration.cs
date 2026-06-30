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
        builder.Property(j => j.DeletedAt);

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

        // F2-P9 (TD-70, CTO-rond 2026-05-13 Q2-C): shadow properties som speglar
        // Postgres generated columns. Värdena härleds STORED från raw_payload
        // av PostgreSQL → drift omöjlig, ingen Domain-koppling till JobTech-
        // taxonomi (Evans 2003 §14 Anti-Corruption Layer). Indexes (partial,
        // WHERE … IS NOT NULL) skapas i migration F2P9JobAdSearchColumns.
        // LINQ-referens: EF.Property<string?>(j, "SsykConceptId") /
        // EF.Property<string?>(j, "RegionConceptId").
        builder.Property<string?>("SsykConceptId")
            .HasColumnName("ssyk_concept_id")
            .HasComputedColumnSql("raw_payload->'occupation'->>'concept_id'", stored: true);

        builder.Property<string?>("RegionConceptId")
            .HasColumnName("region_concept_id")
            .HasComputedColumnSql("raw_payload->'workplace_address'->>'region_concept_id'", stored: true);

        // B1 (ADR 0067 Beslut 2 + ADR 0043-amendment 2026-06-08) — Klass 1
        // STORED generated columns för Platsbanken sök-paritet. Payload finns
        // redan (POCO deserialiserar occupation_group + workplace_address.
        // municipality_concept_id, sanitizer-allowlist passerar dem) → ADD
        // COLUMN populerar från befintlig raw_payload utan re-ingest.
        // OBS: occupation_group är TOP-LEVEL i payloaden (EJ nested under
        // occupation som ssyk_concept_id) — namnglappet "occupation_group"
        // pekar på ssyk-level-4 (yrkesgrupp), JobTechs primära yrke-filternivå.
        builder.Property<string?>("OccupationGroupConceptId")
            .HasColumnName("occupation_group_concept_id")
            .HasComputedColumnSql("raw_payload->'occupation_group'->>'concept_id'", stored: true);

        builder.Property<string?>("MunicipalityConceptId")
            .HasColumnName("municipality_concept_id")
            .HasComputedColumnSql("raw_payload->'workplace_address'->>'municipality_concept_id'", stored: true);

        // B2 (ADR 0067 Beslut 2, Platsbanken sök-paritet) — Klass 2 STORED
        // generated columns: anställningsform (employment_type) + omfattning
        // (worktime_extent). Båda TOP-LEVEL i payloaden (som occupation_group i
        // B1). SKILLNAD MOT B1/Klass 1: raw_payload saknar dessa keys för ALLA
        // befintliga rader (JobTechHit-POCO deserialiserade dem aldrig förrän B2)
        // → kolumnerna är NULL för 100% av raderna tills POCO-tillägg + full
        // re-ingest (backfill-klass2-jobbet) re-serialiserar raw_payload. ADD
        // COLUMN backfillar INGET här (till skillnad mot B1 där payload fanns).
        //
        // NAMNGLAPP-FÄLLA: kolumnen heter worktime_extent_concept_id (taxonomi-typ
        // worktime-extent) men källan i payloaden heter working_hours_type. ADR
        // 0067 Beslut 2 låser kolumnnamnet efter taxonomi-typen. Pekar
        // computedColumnSql på fel path ('worktime_extent') blir kolumnen tyst
        // alltid-NULL utan kompileringsfel — verifierat av JobAdGeneratedColumnsTests.
        builder.Property<string?>("EmploymentTypeConceptId")
            .HasColumnName("employment_type_concept_id")
            .HasComputedColumnSql("raw_payload->'employment_type'->>'concept_id'", stored: true);

        builder.Property<string?>("WorktimeExtentConceptId")
            .HasColumnName("worktime_extent_concept_id")
            .HasComputedColumnSql("raw_payload->'working_hours_type'->>'concept_id'", stored: true);

        // #311 D1 (följ arbetsgivare, org.nr-promotion, ADR 0087) — STORED generated
        // column för arbetsgivarens organisationsnummer, den KANONISKA följ-nyckeln
        // (ingen fuzzy namn-matchning, "Volvo×20"-fällan). NESTED under employer
        // (raw_payload->'employer'->>'organization_number') — EJ top-level som
        // occupation_group/employment_type. Samma B2-mönster som F6P7: raw_payload
        // saknar employer.organization_number för 100% av befintliga rader
        // (JobTechEmployer-POCO:n deserialiserade den aldrig förrän #311) → kolumnen
        // NULL tills POCO-tillägg + full re-ingest re-serialiserar raw_payload. Drift
        // omöjlig (Postgres härleder ur raw_payload, ingen C#-skrivväg). Partial-index
        // (WHERE … IS NOT NULL) skapas i migrationen (fluent-API saknar partial-stöd för
        // shadow-properties → raw SQL, samma skäl som F6P7/F2P9). LINQ-referens:
        // EF.Property<string?>(j, "OrganizationNumber"). ENSKILD FIRMA-NOT (GDPR
        // Art. 32, ADR 0087 D8): org.nr kan vara ett personnummer (enskild firma).
        // PUBLIK Platsbanken-data (arbetsgivaren publicerade det på annonsen) och en
        // STORED generated column → PLAINTEXT at-rest (en generated column kan inte
        // DEK-krypteras; raw_payload är redan plaintext, ADR 0032 §8). Accept-risk
        // per ADR 0087 D8 (Klas Art. 32-sign-off 2026-06-30; Art. 32-balanstest:
        // publik källa + queryability-nödvändighet). Skyddet ligger vid SURFNINGS-/
        // LOGG-gränsen, INTE at-rest: ALDRIG loggad/surfad oflaggat; sole-prop
        // flaggas/maskeras i disambiguerings-display (CLAUDE.md §5, högsta prioritet).
        builder.Property<string?>("OrganizationNumber")
            .HasColumnName("organization_number")
            .HasComputedColumnSql("raw_payload->'employer'->>'organization_number'", stored: true);

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

        builder.HasQueryFilter(j => j.DeletedAt == null);

        builder.Ignore(j => j.DomainEvents);
    }
}
