using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// ADR 0087 D5 (#311 PR-4) — EF Core configuration for <see cref="FollowedCompanyAdHit"/>.
/// <para>
/// <b>Design decisions (parity <c>UserJobAdMatchConfiguration</c>):</b>
/// <list type="bullet">
/// <item>Strongly-typed <see cref="FollowedCompanyAdHitId"/> converted to uuid.</item>
/// <item><see cref="JobAdId"/> and <see cref="CompanyWatchId"/> stored as plain uuid — NO FK to
///   <c>job_ads</c> / <c>company_watches</c> (ADR 0058/0059 soft-delete isolation: a retracted ad
///   or an unfollowed/erased watch must not delete a delivery row; the join is handler-managed;
///   parity <c>UserJobAdMatchConfiguration.job_ad_id</c>).</item>
/// <item><see cref="FollowedCompanyAdHitStatus"/> stored as its enum NAME via
///   <c>HasConversion&lt;string&gt;()</c> (reorder-safe; parity the
///   <c>[JsonConverter(typeof(JsonStringEnumConverter))]</c> on the enum).</item>
/// <item>UNIQUE (user_id, job_ad_id, company_watch_id) — the dedup spine (idempotent scan; an
///   existing row in any non-Pending status is skipped). The watch_id is part of the key so the
///   same ad matched via TWO of a user's follows is two honest, independently-dispatched rows.</item>
/// <item>Two btree indexes for the hot paths: dispatch query (user_id, notification_status) and
///   the cascade-by-user sweep (user_id).</item>
/// </list>
/// </para>
/// <para>
/// <b>No grade, no score, no jsonb.</b> A company-follow hit is not scored — the aggregate carries
/// no <c>Grade</c> and no numeric field (ADR 0071/0076 — no new Goodhart surface). The notification
/// body's public title/company come from a handler-managed join to <c>job_ads</c>, never
/// denormalised here. Soft-delete via <c>deleted_at</c> + global query filter; no DEK column.
/// </para>
/// </summary>
public sealed class FollowedCompanyAdHitConfiguration : IEntityTypeConfiguration<FollowedCompanyAdHit>
{
    public void Configure(EntityTypeBuilder<FollowedCompanyAdHit> builder)
    {
        builder.ToTable("followed_company_ad_hits");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id)
            .HasConversion(id => id.Value, value => new FollowedCompanyAdHitId(value))
            .ValueGeneratedNever();

        builder.Property(h => h.UserId)
            .IsRequired();

        // ADR 0058/0059 — by-identity references, NO FK (parity UserJobAdMatchConfiguration).
        builder.Property(h => h.JobAdId)
            .HasConversion(id => id.Value, value => new JobAdId(value))
            .HasColumnName("job_ad_id")
            .IsRequired();

        builder.Property(h => h.CompanyWatchId)
            .HasConversion(id => id.Value, value => new CompanyWatchId(value))
            .HasColumnName("company_watch_id")
            .IsRequired();

        // Enum column — stored by NAME (stable against ordinal reorder; parity
        // UserJobAdMatchConfiguration.NotificationStatus).
        builder.Property(h => h.NotificationStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(h => h.CreatedAt).IsRequired();
        builder.Property(h => h.SentAt);
        builder.Property(h => h.DeletedAt);

        // #453 (cross-channel dedup) — nullable "seen in-app" stamp. NO dedicated index: the dispatch
        // hot path already scopes to a tiny per-user set via ix_followed_company_ad_hits_user_status
        // (user_id, notification_status='Pending'), and `seen_at IS NULL` is a cheap residual predicate
        // over that handful of rows — a composite index buys nothing here (db-migration-writer 2026-07-02).
        builder.Property(h => h.SeenAt);

        // UNIQUE (user_id, job_ad_id, company_watch_id) — the dedup spine. The scan is race-safe via
        // the Worker's DisableConcurrentExecution (single-writer) + a client-side existing-triple skip
        // (CompanyWatchScanJob), with this UNIQUE as the hard backstop (a concurrent insert would
        // throw DbUpdateException, never silently duplicate). company_watch_id is part of the key so
        // the same ad matched via TWO of a user's follows is two honest, independently-dispatched rows.
        builder.HasIndex(h => new { h.UserId, h.JobAdId, h.CompanyWatchId })
            .IsUnique()
            .HasDatabaseName("ux_followed_company_ad_hits_user_jobad_watch");

        // Dispatch query (DigestDispatchJob: WHERE user_id = ? AND notification_status = 'Pending').
        // A FULL B-tree, NOT a `WHERE deleted_at IS NULL` partial index (in contrast to CompanyWatch's
        // ux_company_watches_user_orgnr_active): a soft-deleted hit is semantically never Pending
        // (SoftDelete stamps deleted_at regardless of status), so a partial filter would buy nothing
        // here — there is no resurrect/ON-CONFLICT need that would require soft-deleted rows to remain
        // index-visible (db-migration-writer 2026-07-01).
        builder.HasIndex(h => new { h.UserId, h.NotificationStatus })
            .HasDatabaseName("ix_followed_company_ad_hits_user_status");

        // UserId index for cascade-delete by user (AccountHardDeleter Art. 17 sweep) + the scan's
        // per-user grouping.
        builder.HasIndex(h => h.UserId)
            .HasDatabaseName("ix_followed_company_ad_hits_user_id");

        // Soft-delete: deleted rows hidden from normal queries (retained for audit).
        builder.HasQueryFilter(h => h.DeletedAt == null);

        builder.Ignore(h => h.DomainEvents);
    }
}
