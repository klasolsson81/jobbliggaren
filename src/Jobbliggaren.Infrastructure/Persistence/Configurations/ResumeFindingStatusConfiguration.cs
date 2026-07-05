using Jobbliggaren.Domain.Resumes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// The DEK-free finding-status ledger (Fas 4b PR-4, ADR 0093 §D2(e), local ADR 0097) —
/// child of <c>resumes</c> via the FK configured in <see cref="ResumeConfiguration"/>
/// (ON DELETE CASCADE = the Art. 17 path, ResumeVersion precedent). Every column is a
/// closed enum name, a bounded machine token, a fixed-length hex digest or a timestamp —
/// NO free-text column may ever be added here (D2(e) at-rest guarantee, pinned
/// fail-closed by <c>ResumeFindingStatusColumnGuardTests</c>).
/// </summary>
public sealed class ResumeFindingStatusConfiguration : IEntityTypeConfiguration<ResumeFindingStatus>
{
    public void Configure(EntityTypeBuilder<ResumeFindingStatus> builder)
    {
        builder.ToTable("resume_finding_statuses");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id)
            .HasConversion(id => id.Value, value => new ResumeFindingStatusId(value))
            .ValueGeneratedNever();

        // "major.minor.patch" — bounded token, aggregate-validated (Resume.SetFindingStatus).
        builder.Property(f => f.RubricVersion)
            .HasColumnName("rubric_version")
            .HasMaxLength(14)
            .IsRequired();

        // "A1".."E12" — bounded token, aggregate-validated.
        builder.Property(f => f.CriterionId)
            .HasColumnName("criterion_id")
            .HasMaxLength(3)
            .IsRequired();

        // SmartEnum by Name-string (reorder-safe house default, ResumeSourceOrigin parity).
        builder.Property(f => f.Status)
            .HasConversion(
                s => s.Name,
                v => ReviewFindingStatus.FromName(v, false))
            .HasColumnName("status")
            .HasMaxLength(20)
            .IsRequired();

        // Full SHA-256 as lowercase hex — fixed length, never truncated (CTO-bind Q4).
        builder.Property(f => f.TargetFingerprint)
            .HasColumnName("target_fingerprint")
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();

        builder.Property(f => f.StaleAt).HasColumnName("stale_at");
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // One decision per (resume, rubric version, criterion) — the D2(e) key. The FK
        // shadow property is declared by the parent's HasMany in ResumeConfiguration.
        builder.HasIndex("ResumeId", nameof(ResumeFindingStatus.RubricVersion), nameof(ResumeFindingStatus.CriterionId))
            .IsUnique()
            .HasDatabaseName("ux_resume_finding_statuses_resume_version_criterion");
    }
}
