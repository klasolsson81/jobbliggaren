using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// ADR 0175 D5 (#1663) — EF Core configuration for <see cref="CompanyWatchCriterionAdHit"/>.
/// <list type="bullet">
/// <item><see cref="JobAdId"/> by identity, no FK (parity <see cref="FollowedCompanyAdHitConfiguration"/>): an
///   ad that leaves Active is purged by the scan, never cascaded from <c>job_ads</c>.</item>
/// <item>The criterion is a REAL FK with <c>ON DELETE CASCADE</c>, modelled in EF (precedent
///   <c>CompanyWatchCriterionMemberConfiguration</c>). Deleting a criterion is a hard delete, so its hits go
///   with it at the database; and because EF knows the relationship, <c>AccountHardDeleter</c>'s tracked
///   deletes order the hits before their criterion instead of letting the cascade empty the hits' own DELETE
///   (which would throw <c>DbUpdateConcurrencyException</c> and fail the Art. 17 job). The FK property is the
///   strongly-typed id, never a bare <c>Guid</c>: EF matches the relationship on the key's CLR type.</item>
/// <item>Three indexes, the follow table's count. The UNIQUE key leads with the FK column, so it also serves
///   the cascade's lookup and EF adds no index of its own for the FK.</item>
/// <item>No query filter: the aggregate has no lifecycle state (and the Art. 17 arm must then read it without
///   <c>IgnoreQueryFilters</c>, which <c>AccountHardDeleteCascadeFitnessTests</c> enforces).</item>
/// </list>
/// </summary>
public sealed class CompanyWatchCriterionAdHitConfiguration : IEntityTypeConfiguration<CompanyWatchCriterionAdHit>
{
    public void Configure(EntityTypeBuilder<CompanyWatchCriterionAdHit> builder)
    {
        builder.ToTable("company_watch_criterion_ad_hits", t => t.HasCheckConstraint(
            "ck_company_watch_criterion_ad_hits_dismissed_implies_read",
            "NOT dismissed_from_notification_feed OR read_in_notification_feed"));

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id)
            .HasConversion(id => id.Value, value => new CompanyWatchCriterionAdHitId(value))
            .ValueGeneratedNever();

        builder.Property(h => h.UserId)
            .IsRequired();

        builder.Property(h => h.JobAdId)
            .HasConversion(id => id.Value, value => new JobAdId(value))
            .HasColumnName("job_ad_id")
            .IsRequired();

        builder.Property(h => h.CompanyWatchCriterionId)
            .HasConversion(id => id.Value, value => new CompanyWatchCriterionId(value))
            .HasColumnName("company_watch_criterion_id")
            .IsRequired();

        // The default name (84 characters) would be truncated at 63 by PostgreSQL; name it.
        builder.HasOne<CompanyWatchCriterion>()
            .WithMany()
            .HasForeignKey(h => h.CompanyWatchCriterionId)
            .HasConstraintName("fk_company_watch_criterion_ad_hits_company_watch_criterion_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(h => h.CreatedAt).IsRequired();

        builder.Property(h => h.ReadInNotificationFeed)
            .HasColumnName("read_in_notification_feed")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(h => h.DismissedFromNotificationFeed)
            .HasColumnName("dismissed_from_notification_feed")
            .HasDefaultValue(false)
            .IsRequired();

        // One (criterion, ad) is one row: the scan's dedup backstop and the cascade's lookup.
        builder.HasIndex(h => new { h.CompanyWatchCriterionId, h.JobAdId })
            .IsUnique()
            .HasDatabaseName("ux_company_watch_criterion_ad_hits_criterion_jobad");

        // The per-ad mark every click and X runs in the bell (PR 3), and the scan's per-user dedup read.
        builder.HasIndex(h => new { h.UserId, h.JobAdId })
            .HasDatabaseName("ix_company_watch_criterion_ad_hits_user_jobad");

        // A user's hits by recency: the count, the page of new ads, the bell, and the Art. 17 sweep by user_id.
        builder.HasIndex(h => new { h.UserId, h.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("ix_company_watch_criterion_ad_hits_user_created_at");

        builder.Ignore(h => h.DomainEvents);
    }
}
