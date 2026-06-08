using JobbPilot.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace JobbPilot.Infrastructure.Persistence.Configurations;

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
        // telefon, firmatecknare). Encryption-at-rest täcks av AWS RDS KMS;
        // envelope encryption (app-side) skjuts till TD-13 (Fas 2 Major).
        // PII-stripping vid ingest dokumenterad i ADR 0032 §8-amendment 2026-05-12.
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

        builder.HasQueryFilter(j => j.DeletedAt == null);

        builder.Ignore(j => j.DomainEvents);
    }
}
