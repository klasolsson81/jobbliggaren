using System.Text.Json;
using JobbPilot.Domain.Resumes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JobbPilot.Infrastructure.Persistence.Configurations;

public sealed class ResumeVersionConfiguration : IEntityTypeConfiguration<ResumeVersion>
{
    private static readonly JsonSerializerOptions ContentJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

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

        // ResumeContent är en immutable record med nästade IReadOnlyList<T>-properties
        // (Experiences, Educations, Skills) plus value object PersonalInfo. EF Core 10
        // OwnsOne(...).ToJson() med nästade OwnsMany hanterar inte init-only
        // IReadOnlyList<T> tillförlitligt — backing-fields-mappning blir bräcklig.
        // Domänsemantiken (whole-replacement, reference-equality på collections per
        // ResumeContent.cs xml-doc) matchar JSON-blob-mappning precis. Använder därför
        // HasConversion med System.Text.Json mot jsonb-kolumn.
        var contentConverter = new ValueConverter<ResumeContent, string>(
            content => JsonSerializer.Serialize(content, ContentJsonOptions),
            json => JsonSerializer.Deserialize<ResumeContent>(json, ContentJsonOptions)!);

        var contentComparer = new ValueComparer<ResumeContent>(
            (left, right) => JsonSerializer.Serialize(left, ContentJsonOptions)
                == JsonSerializer.Serialize(right, ContentJsonOptions),
            content => JsonSerializer.Serialize(content, ContentJsonOptions).GetHashCode(StringComparison.Ordinal),
            content => JsonSerializer.Deserialize<ResumeContent>(
                JsonSerializer.Serialize(content, ContentJsonOptions), ContentJsonOptions)!);

        // TODO(GDPR): Content innehåller känsligt CV-innehåll (BUILD.md §13.1: PersonalInfo,
        // Experiences, Educations, Skills) — kryptera kolumnen med KMS-backed value converter
        // i Fas 2. Idag lagras klartext-JSONB; samma status som applications.cover_letter,
        // application_notes.content, follow_ups.note. Spårning: TD-13.
        builder.Property(rv => rv.Content)
            .HasConversion(contentConverter, contentComparer)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(rv => rv.CreatedAt).IsRequired();
        builder.Property(rv => rv.UpdatedAt).IsRequired();
        builder.Property(rv => rv.DeletedAt);

        builder.HasQueryFilter(rv => rv.DeletedAt == null);
    }
}
