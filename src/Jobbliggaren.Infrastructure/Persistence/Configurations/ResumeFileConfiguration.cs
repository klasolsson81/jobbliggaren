using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes.Files;
using Jobbliggaren.Domain.Resumes.Parsing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF configuration for the Fas 4b PR-9a <see cref="ResumeFile"/> aggregate (greenfield table
/// <c>resume_files</c>, ADR 0093 §D5 — original-file binary store). The Form C ciphertext lives
/// in a <c>bytea</c> column (<see cref="ResumeFile.SealedContent"/>) — sealed in the write-path,
/// opaque to the model (no interceptor, no <c>EncryptedFieldRegistry</c> entry). Plaintext
/// metadata (redacted <c>file_name</c>, <c>byte_size</c>, <c>content_type</c>) is minimised at
/// rest and row-deleted on the Art. 17 cascade (survives crypto-erasure). FK-less by convention
/// (ADR 0011): owner-scoped by <c>job_seeker_id</c>, retention-coupled by <c>parsed_resume_id</c>,
/// both indexed, no DB FK (the cascade is explicit in <c>AccountHardDeleter</c>). Immutable
/// (write-once) → no concurrency token, no soft-delete filter.
/// </summary>
public sealed class ResumeFileConfiguration : IEntityTypeConfiguration<ResumeFile>
{
    public void Configure(EntityTypeBuilder<ResumeFile> builder)
    {
        builder.ToTable("resume_files");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id)
            .HasConversion(id => id.Value, value => new ResumeFileId(value))
            .ValueGeneratedNever();

        builder.Property(f => f.JobSeekerId)
            .HasConversion(id => id.Value, value => new JobSeekerId(value))
            .HasColumnName("job_seeker_id")
            .IsRequired();

        builder.HasIndex(f => f.JobSeekerId);

        // Retention coupling key (M-F3): an original never outlives its parsed sibling.
        builder.Property(f => f.ParsedResumeId)
            .HasConversion(id => id.Value, value => new ParsedResumeId(value))
            .HasColumnName("parsed_resume_id")
            .IsRequired();

        builder.HasIndex(f => f.ParsedResumeId);

        // Form C envelope ([version]||nonce||ct||tag), AES-256-GCM under the owner DEK. Opaque
        // bytea — the model never decrypts (the 9b streaming download owns the read path).
        builder.Property(f => f.SealedContent)
            .HasColumnName("content")
            .HasColumnType("bytea")
            .IsRequired();

        // Server-derived canonical MIME (never the client-declared content-type). Non-PII.
        builder.Property(f => f.ContentType)
            .HasColumnName("content_type")
            .HasMaxLength(255)
            .IsRequired();

        // Plaintext metadata, personnummer-redacted at rest (M-F1). Row-deleted on cascade.
        builder.Property(f => f.FileName)
            .HasColumnName("file_name")
            .HasMaxLength(400)
            .IsRequired();

        // Plaintext byte length (non-PII) — retention accounting + future D9 export-cap.
        builder.Property(f => f.ByteSize)
            .HasColumnName("byte_size")
            .IsRequired();

        // M-F5 metadata — true only for a consent-backed capture since CV-pivot 5b (the
        // aggregate's biconditional refuses a flagged row without the evidence). Never public.
        builder.Property(f => f.PnrFlagged)
            .HasColumnName("pnr_flagged")
            .HasDefaultValue(false)
            .IsRequired();

        // Art. 7(1) consent evidence (5b security-bind B1) — nullable, non-null iff pnr_flagged.
        // Every pre-5b row is non-flagged, so null is the correct backfill-free default.
        builder.Property(f => f.PnrConsentAt)
            .HasColumnName("pnr_consent_at");

        builder.Property(f => f.PnrConsentDialogVersion)
            .HasColumnName("pnr_consent_dialog_version")
            .HasMaxLength(32);

        builder.Property(f => f.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Ignore(f => f.DomainEvents);
    }
}
