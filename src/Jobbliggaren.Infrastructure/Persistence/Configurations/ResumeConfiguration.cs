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

        builder.HasQueryFilter(r => r.DeletedAt == null);

        builder.Ignore(r => r.DomainEvents);
    }
}
