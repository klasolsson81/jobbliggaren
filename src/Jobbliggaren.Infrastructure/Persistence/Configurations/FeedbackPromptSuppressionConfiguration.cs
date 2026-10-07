using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

public sealed class FeedbackPromptSuppressionConfiguration : IEntityTypeConfiguration<FeedbackPromptSuppression>
{
    public void Configure(EntityTypeBuilder<FeedbackPromptSuppression> builder)
    {
        builder.ToTable("feedback_prompt_suppressions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasConversion(id => id.Value, value => new FeedbackPromptSuppressionId(value))
            .ValueGeneratedNever();

        builder.Property(s => s.JobSeekerId)
            .HasConversion(id => id.Value, value => new JobSeekerId(value))
            .IsRequired();

        builder.Property(s => s.Page)
            .HasConversion(page => page.Name, key => FeedbackPage.FromName(key, false))
            .HasColumnName("page_key")
            .HasMaxLength(40)
            .IsRequired();

        builder.HasIndex(s => new { s.JobSeekerId, s.Page })
            .IsUnique()
            .HasDatabaseName("ux_feedback_prompt_suppressions_job_seeker_page");

        builder.Ignore(s => s.DomainEvents);
    }
}
