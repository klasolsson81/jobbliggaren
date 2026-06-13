using Jobbliggaren.Domain.Resumes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

public sealed class ResumeVersionConfiguration : IEntityTypeConfiguration<ResumeVersion>
{
    public void Configure(EntityTypeBuilder<ResumeVersion> builder)
    {
        builder.ToTable("resume_versions");

        builder.HasKey(rv => rv.Id);
        builder.Property(rv => rv.Id)
            .HasConversion(id => id.Value, value => new ResumeVersionId(value))
            .ValueGeneratedNever();

        builder.Property(rv => rv.Kind)
            .HasConversion(
                k => k.Name,
                v => ResumeVersionKind.FromName(v, ignoreCase: false))
            .HasMaxLength(20)
            .IsRequired();

        // TD-13 C4.2 #1c (ADR 0049 Mekanik-not 6, architect-låst 2026-05-19;
        // C4.0-gate RÖD bekräftad: ValueConverter.ConvertFromProvider kör FÖRE
        // IMaterializationInterceptor.InitializedInstance). ResumeContent bär
        // känsligt CV-innehåll (BUILD.md §13.1). Den tidigare JSON-
        // ValueConverter:n + ValueComparer:n tas bort HELT — Content görs
        // EF-Ignore:ad och interceptor-paret äger hela transformen
        // (ResumeContent↔JSON↔ciphertext) på en krypterad text-shadow.
        // ValueComparer-frågan UPPHÖR (Content är ej EF-tracked → change-
        // tracking sker på shadow-strängen). Spårning: TD-13.
        builder.Ignore(rv => rv.Content);

        // Krypterad text-shadow (C4.1-migration la till `content_enc text NULL`).
        // INGEN .IsRequired() — kolumnen är nullable under backfill-fönstret
        // (legacy-only-rader har content_enc IS NULL tills C5-backfill;
        // architect-korrektion 2026-05-19, Beslut 5 steg 2).
        builder.Property<string>("ContentEnc")
            .HasColumnName("content_enc");

        // Legacy klartext-jsonb-rå-shadow (backfill-fallback, Beslut 5 steg 2).
        // RÅ string mot jsonb (Npgsql text-hämtning) — INGEN JSON-VC (en VC
        // skulle återinföra C4.0-RED: ConvertFromProvider före interceptorn).
        // HÅRT read-only: PropertySaveBehavior.Ignore på BÅDE before/after-save
        // → EF skriver ALDRIG `content` (varken på INSERT eller UPDATE), så
        // klartext-JSON kan aldrig write-back:as under dual-state-fönstret
        // (architect 2026-05-19; striktare, ej svagare — paritet Not 5b).
        // Kolumnen är fortfarande materialiserbar på read (Ignore styr endast
        // write-paths) → MaterializationInterceptorn läser den vid fallback.
        builder.Property<string>("ContentLegacyJson")
            .HasColumnName("content")
            .HasColumnType("jsonb");
        builder.Metadata
            .FindProperty("ContentLegacyJson")!
            .SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        builder.Metadata
            .FindProperty("ContentLegacyJson")!
            .SetAfterSaveBehavior(PropertySaveBehavior.Ignore);

        builder.Property(rv => rv.CreatedAt).IsRequired();
        builder.Property(rv => rv.UpdatedAt).IsRequired();
        builder.Property(rv => rv.DeletedAt);

        builder.HasQueryFilter(rv => rv.DeletedAt == null);
    }
}
