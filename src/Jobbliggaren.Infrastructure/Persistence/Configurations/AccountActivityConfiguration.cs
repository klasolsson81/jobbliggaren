using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Auth.Activity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// #2161 (ADR 0172) — EF Core configuration for <c>account_activity</c> (parity
/// <see cref="CompanyWatchCriterionMemberConfiguration"/>): the table is reached only through raw SQL inside
/// Infrastructure, so this configuration exists for the migration schema.
/// </summary>
internal sealed class AccountActivityConfiguration : IEntityTypeConfiguration<AccountActivity>
{
    public void Configure(EntityTypeBuilder<AccountActivity> builder)
    {
        builder.ToTable("account_activity");

        builder.HasKey(a => a.JobSeekerId);
        builder.Property(a => a.JobSeekerId)
            .HasConversion(id => id.Value, value => new JobSeekerId(value))
            .ValueGeneratedNever();

        builder.Property(a => a.LastLoginAt);
        builder.Property(a => a.LastActiveAt).IsRequired();

        // Erasure is a database property (ADR 0172 D2): whichever path deletes the profile row deletes this one.
        // Declared from this side with no navigation, so JobSeeker knows nothing of the record. The cascade is
        // outside AccountHardDeleteCascadeFitnessTests, which partitions aggregate roots; its oracle is
        // HardDeleteAccountsJobIntegrationTests (ADR 0172 D9).
        builder.HasOne<JobSeeker>()
            .WithMany()
            .HasForeignKey(a => a.JobSeekerId)
            .HasConstraintName("fk_account_activity_job_seekers_job_seeker_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
