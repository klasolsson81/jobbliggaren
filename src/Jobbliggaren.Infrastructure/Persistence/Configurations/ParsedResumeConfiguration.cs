using System.Text.Json;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for the F4-8 <see cref="ParsedResume"/> staging aggregate
/// (greenfield table <c>parsed_resumes</c>, CTO Decision 1 = Variant A). CV-PII is
/// encrypted via the interceptor pair (ADR 0074 Invariant 3): <see cref="ParsedResume.Content"/>
/// is EF-Ignore'd → Form B shadow <c>parsed_content_enc</c>, and
/// <see cref="ParsedResume.RawText"/> → Form A in-place <c>raw_text</c> (both registered
/// in <c>EncryptedFieldRegistry</c>). Non-PII metadata (confidence, personnummer-scan
/// outcome, occupation proposals) is plain jsonb. Greenfield ⇒ NO legacy plaintext
/// column (unlike <c>resume_versions</c>).
/// </summary>
public sealed class ParsedResumeConfiguration : IEntityTypeConfiguration<ParsedResume>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public void Configure(EntityTypeBuilder<ParsedResume> builder)
    {
        builder.ToTable("parsed_resumes");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasConversion(id => id.Value, value => new ParsedResumeId(value))
            .ValueGeneratedNever();

        builder.Property(p => p.JobSeekerId)
            .HasConversion(id => id.Value, value => new JobSeekerId(value))
            .IsRequired();

        builder.HasIndex(p => p.JobSeekerId);

        builder.Property(p => p.SourceFileName)
            .HasMaxLength(400)
            .HasColumnName("source_file_name")
            .IsRequired();

        builder.Property(p => p.SourceContentType)
            .HasMaxLength(255)
            .HasColumnName("source_content_type")
            .IsRequired();

        // SmartEnum conversions (parity ResumeConfiguration.Language / ResumeVersionKind).
        builder.Property(p => p.DetectedLanguage)
            .HasConversion(
                lang => lang.Value,
                value => ResumeLanguage.FromValue(value))
            .HasColumnName("detected_language")
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(p => p.Status)
            .HasConversion(
                status => status.Name,
                value => ParsedResumeStatus.FromName(value, ignoreCase: false))
            .HasColumnName("status")
            .HasMaxLength(20)
            .IsRequired();

        // CV-PII (ADR 0074 Invariant 3) — Content is EF-Ignore'd; the interceptor pair
        // owns the ParsedResumeContent ↔ JSON ↔ ciphertext transform on the shadow.
        builder.Ignore(p => p.Content);
        builder.Property<string>("ParsedContentEnc")
            .HasColumnName("parsed_content_enc");

        // CV-PII — raw normalized text, encrypted in-place (Form A) for F4-9 span
        // citation (Invariant 2). HasMaxLength omitted (ciphertext > plaintext; TEXT).
        builder.Property(p => p.RawText)
            .HasColumnName("raw_text")
            .IsRequired();

        // Non-PII metadata — plain jsonb.
        var (confidenceConverter, confidenceComparer) = JsonConverter<ParseConfidence>();
        builder.Property(p => p.Confidence)
            .HasConversion(confidenceConverter, confidenceComparer)
            .HasColumnName("parse_confidence")
            .HasColumnType("jsonb")
            .IsRequired();

        var (personnummerConverter, personnummerComparer) = JsonConverter<PersonnummerScanOutcome>();
        builder.Property(p => p.Personnummer)
            .HasConversion(personnummerConverter, personnummerComparer)
            .HasColumnName("personnummer_scan")
            .HasColumnType("jsonb")
            .IsRequired();

        var (proposalsConverter, proposalsComparer) = JsonConverter<List<ProposedOccupation>>();
        builder.Property<List<ProposedOccupation>>("_occupationProposals")
            .HasField("_occupationProposals")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(proposalsConverter, proposalsComparer)
            .HasColumnName("occupation_proposals")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Ignore(p => p.OccupationProposals);

        // ADR 0079 STEG 3 — CV-resolved skill proposals (non-PII concept-id + label),
        // plain jsonb, symmetric with occupation_proposals. The migration backfills
        // existing rows with '[]' (the column default) so a pre-STEG-3 parsed_resume
        // deserializes to an empty proposal list.
        var (skillProposalsConverter, skillProposalsComparer) = JsonConverter<List<ProposedSkill>>();
        builder.Property<List<ProposedSkill>>("_skillProposals")
            .HasField("_skillProposals")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasConversion(skillProposalsConverter, skillProposalsComparer)
            .HasColumnName("skill_proposals")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Ignore(p => p.SkillProposals);

        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.UpdatedAt).IsRequired();
        builder.Property(p => p.DeletedAt);

        // xmin system column — optimistic concurrency (parity ResumeConfiguration).
        builder.Property<uint>("xmin")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // Soft-delete: Discarded/deleted rows are hidden from normal queries (retained
        // for audit + the retention sweep).
        builder.HasQueryFilter(p => p.DeletedAt == null);

        builder.Ignore(p => p.DomainEvents);
    }

    // jsonb round-trip via System.Text.Json. ValueComparer compares by canonical
    // serialization (the values are set once at creation and never mutated).
    private static (ValueConverter<T, string> Converter, ValueComparer<T> Comparer) JsonConverter<T>()
    {
        var converter = new ValueConverter<T, string>(
            value => JsonSerializer.Serialize(value, JsonOptions),
            json => JsonSerializer.Deserialize<T>(json, JsonOptions)!);

        var comparer = new ValueComparer<T>(
            (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
            v => v == null ? 0 : JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, JsonOptions), JsonOptions)!);

        return (converter, comparer);
    }
}
