using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

public sealed class ApplicationConfiguration : IEntityTypeConfiguration<DomainApplication>
{
    public void Configure(EntityTypeBuilder<DomainApplication> builder)
    {
        builder.ToTable("applications");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasConversion(
                id => id.Value,
                value => new Jobbliggaren.Domain.Applications.ApplicationId(value))
            .ValueGeneratedNever();

        builder.Property(a => a.JobSeekerId)
            .HasConversion(id => id.Value, value => new JobSeekerId(value))
            .IsRequired();

        builder.Property(a => a.JobAdId)
            .HasConversion(
                id => id == null ? (Guid?)null : id.Value.Value,
                value => value == null ? (JobAdId?)null : new JobAdId(value.Value));

        // F4-11 (BUILD §5.3): the exact CV version used when applying. Plain
        // nullable converted column, NO cross-aggregate FK — parity with JobAdId
        // (reference-by-id, CLAUDE.md §2.2). Nullable for backward compatibility
        // (existing applications have no link); ResumeVersion is soft-delete only,
        // so a real FK would add no anti-dangling value.
        builder.Property(a => a.ResumeVersionId)
            .HasConversion(
                id => id == null ? (Guid?)null : id.Value.Value,
                value => value == null ? (ResumeVersionId?)null : new ResumeVersionId(value.Value))
            .HasColumnName("resume_version_id");

        // TD-13 (ADR 0049 C3): krypteras via FieldEncryptionSaveChangesInterceptor
        // (sentinel v1:+base64). HasMaxLength borttagen — ciphertext överskrider
        // klartext-cap; TEXT obegränsad i Postgres. Längd-validering hör i
        // domän/validator, ej kolumn (ApplicationNote.Create-precedens).
        builder.Property(a => a.CoverLetter);

        // ManualPosting — optional owned entity (manuell ansökan utan JobAd).
        // Explicit HasColumnName krävs: global UseSnakeCaseNamingConvention
        // skulle annars ge manual_posting_* (navigation-prefix). Samma mönster
        // som External owned-type på JobAd. IsRequired(false) obligatorisk —
        // EF Core 10 default för owned-referens är required; utan denna kan EF
        // ej skilja "ingen ManualPosting" från "all-null ManualPosting".
        builder.OwnsOne(a => a.ManualPosting, manual =>
        {
            manual.Property(m => m.Title)
                .HasColumnName("manual_title")
                .HasMaxLength(300);
            manual.Property(m => m.Company)
                .HasColumnName("manual_company")
                .HasMaxLength(200);
            manual.Property(m => m.Url)
                .HasColumnName("manual_url")
                .HasMaxLength(2000);
            manual.Property(m => m.ExpiresAt)
                .HasColumnName("manual_expires_at");
        });
        builder.Navigation(a => a.ManualPosting).IsRequired(false);

        // AdSnapshot — optional owned entity (issue #315, ADR 0086): the frozen
        // copy of a JobAd's text captured at apply-time. Same separate-column
        // OwnsOne pattern as ManualPosting above. Explicit snapshot_*
        // HasColumnName on EVERY property (the global UseSnakeCaseNamingConvention
        // would otherwise prefix the navigation name → ad_snapshot_*); distinct
        // prefix so columns never collide with manual_*. Navigation.IsRequired(false)
        // is obligatory — EF Core 10 defaults an owned reference to required;
        // without it EF cannot distinguish "no snapshot" (all snapshot_* NULL →
        // navigation null) from an all-null AdSnapshot instance (back-compat for
        // pre-#315 rows). Public Platsbanken metadata → plaintext, NO DEK (ADR
        // 0086 D5). snapshot_description is unbounded TEXT (no HasMaxLength), like
        // cover_letter — a full ad body exceeds any varchar cap.
        builder.OwnsOne(a => a.AdSnapshot, snap =>
        {
            snap.Property(s => s.Title)
                .HasColumnName("snapshot_title")
                .HasMaxLength(300);
            snap.Property(s => s.Company)
                .HasColumnName("snapshot_company")
                .HasMaxLength(200);
            snap.Property(s => s.MunicipalityConceptId)
                .HasColumnName("snapshot_municipality_concept_id")
                .HasMaxLength(64);
            snap.Property(s => s.Url)
                .HasColumnName("snapshot_url")
                .HasMaxLength(2000);
            snap.Property(s => s.Source)
                .HasColumnName("snapshot_source")
                .HasMaxLength(50);
            snap.Property(s => s.PublishedAt)
                .HasColumnName("snapshot_published_at");
            snap.Property(s => s.ExpiresAt)
                .HasColumnName("snapshot_expires_at");
            snap.Property(s => s.Description)
                .HasColumnName("snapshot_description");
            snap.Property(s => s.CapturedAt)
                .HasColumnName("snapshot_captured_at");
        });
        builder.Navigation(a => a.AdSnapshot).IsRequired(false);

        builder.Property(a => a.Status)
            .HasConversion(
                s => s.Name,
                v => ApplicationStatus.FromName(v))
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(a => a.CreatedAt).IsRequired();
        builder.Property(a => a.UpdatedAt).IsRequired();
        builder.Property(a => a.LastStatusChangeAt).IsRequired();

        // The apply/"ansökt" date (issue #316, BUILD.md §5.3). Nullable column:
        // Draft applications have not been submitted yet, so they carry no apply
        // date; the value is stamped on the first Submitted transition
        // (Application.TransitionTo) and never overwritten.
        builder.Property(a => a.AppliedAt).HasColumnName("applied_at");
        builder.Property(a => a.GhostedThresholdDays)
            .IsRequired()
            .HasDefaultValue(21);
        builder.Property(a => a.DeletedAt);

        // xmin är PostgreSQL-systemkolumn — ingen DDL-kolumn behövs, Npgsql mappar automatiskt
        builder.Property<uint>("xmin")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.HasMany(a => a.FollowUps)
            .WithOne()
            .HasForeignKey("ApplicationId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.Notes)
            .WithOne()
            .HasForeignKey("ApplicationId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // StatusChange timeline (ADR 0092 D4) — same shadow-FK + cascade pattern
        // as FollowUps/Notes above (a related aggregate-owned entity, not an EF
        // owned type); config in StatusChangeConfiguration.
        builder.HasMany(a => a.StatusChanges)
            .WithOne()
            .HasForeignKey("ApplicationId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // #506 (persistence audit #482, ADR 0045 query-hygiene): applications
        // carried no index on its owner key job_seeker_id (the pre-existing
        // ix_applications_stale_detection is a partial index on
        // last_status_change_at for the ghosting job — orthogonal), so every
        // owner-scoped read and both account-delete cascades sequential-scanned
        // the hottest per-user table. One composite index closes it (net 1:
        // senior-cto-advisor re-triage supersedes the
        // 2026-07-02 net-2 bind — the second, bare (job_ad_id) index served no
        // read path this prefix does not already cover; see below).
        //
        // Composite (job_seeker_id, status). CREATE INDEX takes a SHARE lock
        // (blocks writes, allows reads) — NOT the ACCESS EXCLUSIVE full-rewrite of
        // the sibling ADD COLUMN STORED migrations; see the migration comment. The
        // btree leftmost-prefix serves:
        //   - the owner-scoped equality EVERY read applies (GetApplications /
        //     GetApplicationStats / GetPipeline / ActivityReport / EmployerHistory
        //     / EmployerCountBatch / HasApplied: Where(JobSeekerId == x));
        //   - both account cascades (soft: DeleteAccountCommandHandler; hard:
        //     AccountHardDeleter, IgnoreQueryFilters) — which is why it is
        //     deliberately NOT partial on "deleted_at IS NULL": the hard-delete
        //     cascade must resolve soft-deleted rows a partial index would miss;
        //   - the owner-scoped TWO-column (job_seeker_id + job_ad_id) lookups over
        //     the tiny per-seeker partition — HasAppliedQueryHandler,
        //     GetJobAdStatusBatchQueryHandler, and the /jobb applied/not-applied
        //     EXISTS in PerUserJobAdSearchQuery. No non-owner-scoped
        //     "applicants-to-this-ad" read exists, so a bare (job_ad_id) index
        //     would add write-amplification for zero read benefit (measure-first,
        //     ADR 0045 §2.5; in-house #348 precedent, GetApplicationsQueryHandler).
        // The second column serves the #383 status filter (GetApplications: AND
        // Status == s); the composite also replaces the need for a bare
        // (job_seeker_id) index (leftmost prefix).
        //
        // Pre-named remedy (NOT now — only a future MEASURED hot path): a partial
        // (job_seeker_id, job_ad_id) WHERE job_ad_id IS NOT NULL for the two-column
        // seek (mirrors ux_saved_job_ads_seeker_jobad; the #348 pattern).
        builder.HasIndex(a => new { a.JobSeekerId, a.Status })
            .HasDatabaseName("ix_applications_job_seeker_id_status");

        builder.HasQueryFilter(a => a.DeletedAt == null);

        builder.Ignore(a => a.DomainEvents);
    }
}
