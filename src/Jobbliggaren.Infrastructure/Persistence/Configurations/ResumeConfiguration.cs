using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

public sealed class ResumeConfiguration : IEntityTypeConfiguration<Resume>
{
    public void Configure(EntityTypeBuilder<Resume> builder)
    {
        builder.ToTable("resumes");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, value => new ResumeId(value))
            .ValueGeneratedNever();

        builder.Property(r => r.JobSeekerId)
            .HasConversion(id => id.Value, value => new JobSeekerId(value))
            .IsRequired();

        builder.HasIndex(r => r.JobSeekerId);

        builder.Property(r => r.Name)
            .HasMaxLength(200)
            .IsRequired();

        // ADR 0058 + ADR 0059: Resume.Language (Ardalis.SmartEnum) +
        // denormaliserade list-projektion-fält (LatestRole/SectionCount/TopSkills).
        builder.Property(r => r.Language)
            .HasConversion(
                lang => lang.Value,
                value => ResumeLanguage.FromValue(value))
            .HasColumnName("language")
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(r => r.LatestRole)
            .HasMaxLength(500)
            .HasColumnName("latest_role");

        builder.Property(r => r.SectionCount)
            .HasColumnName("section_count")
            .IsRequired();

        // Mappar mot privata _topSkills (List<string>) via shadow-name + field-access;
        // Resume.TopSkills är beräknad IReadOnlyList<string>-getter (AsReadOnly-wrapper)
        // och ska ignoreras av EF. Npgsql 10 auto-mappar List<string> → text[].
        builder.Property<List<string>>("_topSkills")
            .HasField("_topSkills")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("top_skills")
            .HasColumnType("text[]")
            .IsRequired();

        builder.Ignore(r => r.TopSkills);

        // Fas 4b PR-3 (ADR 0096, CTO-bind D1/D5d/D9): non-PII source metadata +
        // template options as plain columns (ADR 0059 parity — enumerated/bool/
        // timestamp only, no free text; ResumeRootPlainColumnGuardTests pins this).
        // SmartEnums persist by Name-string (reorder-safe house default,
        // StatusChangeConfiguration precedent — NOT Language's legacy int Value).
        // Legacy rows: origin backfills to 'Legacy' (honest-unknown, never fabricated)
        // and template options to the handoff defaults via DB defaults in the migration.
        builder.Property(r => r.Origin)
            .HasConversion(
                o => o.Name,
                v => ResumeSourceOrigin.FromName(v, false))
            .HasColumnName("origin")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.AdoptedAt)
            .HasColumnName("adopted_at");

        builder.Ignore(r => r.IsAdopted);

        // Fas 4b PR-9c (ADR 0100 §D5 / ADR 0103, CTO-bind F1=L-B): the parsed-artifact this CV
        // was promoted from — a nullable strongly-typed provenance soft-reference (the
        // PrimaryResumeId nullable-VO-converter precedent, JobSeekerConfiguration). It is the
        // cascade key for per-CV original-file erasure (joins resume_files.parsed_resume_id,
        // which is already indexed). Non-PII machine id. NO index here: resumes is never queried
        // BY this column — the cascade filters the file side, not the resume side.
        builder.Property(r => r.SourceParsedResumeId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ParsedResumeId(value.Value) : null)
            .HasColumnName("source_parsed_resume_id");

        // Fas 4b PR-8 (ADR 0093 §D5(b), CTO-bind PR-8 Q1): the rubric version the ledger
        // was last reconciled against — a bounded "major.minor.patch" machine token
        // (nullable; pre-PR-8 rows honestly "never reviewed"). Non-PII by shape, parity
        // adopted_at; the hub badge renders a count only when this equals the current
        // rubric version.
        builder.Property(r => r.ReviewedRubricVersion)
            .HasColumnName("reviewed_rubric_version")
            .HasMaxLength(14);

        // Owned VO → separate columns with explicit template_* prefix (ManualPosting/
        // AdSnapshot precedent; global UseSnakeCaseNamingConvention would otherwise
        // prefix the navigation name). Required navigation — unlike ManualPosting,
        // absence is NOT meaningful here: every CV has effective display options, and
        // the migration backfills legacy rows with NOT NULL defaults, so no all-null
        // row exists to disambiguate. NOTE: OwnsOne over ComplexProperty is deliberate —
        // the InMemory test provider cannot shape complex properties carrying value
        // converters (KeyNotFoundException in the shaper, verified 2026-07-05), and the
        // owned-entity reference-identity constraint is honored by CvTemplateOptions.Default
        // returning a FRESH instance per access (never a shared singleton across owners).
        builder.OwnsOne(r => r.TemplateOptions, opts =>
        {
            opts.Property(o => o.Template)
                .HasConversion(t => t.Name, v => CvTemplate.FromName(v, false))
                .HasColumnName("template")
                .HasMaxLength(30)
                .IsRequired();
            opts.Property(o => o.AccentColor)
                .HasConversion(c => c.Name, v => CvAccentColor.FromName(v, false))
                .HasColumnName("template_accent")
                .HasMaxLength(30)
                .IsRequired();
            opts.Property(o => o.FontPair)
                .HasConversion(f => f.Name, v => CvFontPair.FromName(v, false))
                .HasColumnName("template_font")
                .HasMaxLength(30)
                .IsRequired();
            opts.Property(o => o.Density)
                .HasConversion(d => d.Name, v => CvDensity.FromName(v, false))
                .HasColumnName("template_density")
                .HasMaxLength(30)
                .IsRequired();
            opts.Property(o => o.PhotoEnabled)
                .HasColumnName("template_photo_enabled")
                .IsRequired();
            opts.Property(o => o.PhotoShape)
                .HasConversion(s => s.Name, v => CvPhotoShape.FromName(v, false))
                .HasColumnName("template_photo_shape")
                .HasMaxLength(30)
                .IsRequired();

            // Computed completeness guard (consumed by Resume.ChangeTemplateOptions) —
            // never a column.
            opts.Ignore(o => o.IsComplete);

            // Computed ATS-safety verdict (Template.AtsSafe && !PhotoEnabled) — the single
            // honest source consumed by the query DTO (8b.2) + per-render label (8b.3);
            // derived, never a column.
            opts.Ignore(o => o.EffectiveAtsSafe);
        });
        builder.Navigation(r => r.TemplateOptions).IsRequired();

        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.UpdatedAt).IsRequired();
        builder.Property(r => r.DeletedAt);

        // xmin är PostgreSQL-systemkolumn — ingen DDL-kolumn behövs, Npgsql mappar automatiskt
        builder.Property<uint>("xmin")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.HasMany(r => r.Versions)
            .WithOne()
            .HasForeignKey("ResumeId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // Backing-field-access på _versions (privat List<ResumeVersion>) så EF kan
        // materialisera child-collection trots IReadOnlyList-exponering.
        builder.Metadata
            .FindNavigation(nameof(Resume.Versions))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Fas 4b PR-4 (ADR 0093 §D2(e), local ADR 0097): DEK-free finding-status ledger
        // as a child collection (ResumeVersion precedent). FK cascade = the Art. 17
        // path; rows are written only through Resume root methods and loaded explicitly
        // (Include) on the write and review-merge paths — never on list queries (§3.6).
        builder.HasMany(r => r.FindingStatuses)
            .WithOne()
            .HasForeignKey("ResumeId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(Resume.FindingStatuses))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasQueryFilter(r => r.DeletedAt == null);

        builder.Ignore(r => r.DomainEvents);
    }
}
