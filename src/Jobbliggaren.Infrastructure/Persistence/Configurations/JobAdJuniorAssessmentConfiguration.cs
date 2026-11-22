using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// #2264 (ADR 0187) — one row per classified ad, keyed by the ad's id. A row exists only once the ad is classified, so
/// the verdict, its rule and its version are NOT NULL; the decision pair is held whole by a CHECK. Enums are stored by
/// name. The membership reads are a correlated EXISTS on this primary key.
/// </summary>
public sealed class JobAdJuniorAssessmentConfiguration : IEntityTypeConfiguration<JobAdJuniorAssessment>
{
    public void Configure(EntityTypeBuilder<JobAdJuniorAssessment> builder)
    {
        builder.ToTable("job_ad_junior_assessments", t => t.HasCheckConstraint(
            "ck_job_ad_junior_assessments_decision_whole",
            "(decision IS NULL) = (decided_at IS NULL)"));

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("job_ad_id")
            .HasConversion(id => id.Value, value => new JobAdId(value))
            .ValueGeneratedNever();
        builder.Ignore(a => a.JobAdId);

        builder.HasOne<JobAd>()
            .WithOne()
            .HasForeignKey<JobAdJuniorAssessment>(a => a.Id)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(a => a.Verdict)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(a => a.RuleId)
            .HasMaxLength(JuniorClassification.MaxRuleIdLength)
            .IsRequired();
        builder.Property(a => a.RuleSetVersion)
            .HasMaxLength(JuniorClassification.MaxRuleSetVersionLength)
            .IsRequired();
        builder.Property(a => a.ClassifiedFingerprint)
            .HasMaxLength(JuniorInputFingerprint.Length);
        builder.Property(a => a.ClassifiedAt)
            .IsRequired();
        builder.Property(a => a.Decision)
            .HasConversion<string>()
            .HasMaxLength(16);
        builder.Property(a => a.DecidedAt);

        builder.Ignore(a => a.DomainEvents);
    }
}
