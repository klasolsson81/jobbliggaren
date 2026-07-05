using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
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

        // Fas 4b PR-4 (ADR 0093 §D2(e), lokal ADR 0097): DEK-fri fynd-status-ledger som
        // barn-collection (ResumeVersion-precedent). FK-cascade = Art. 17-vägen; raderna
        // skrivs enbart via Resume-rotens metoder och laddas explicit (Include) på
        // skriv- och review-merge-vägarna — aldrig på list-queries (§3.6).
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
