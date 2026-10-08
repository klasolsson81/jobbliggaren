using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

public sealed class FeedbackScreenshotConfiguration : IEntityTypeConfiguration<FeedbackScreenshot>
{
    public void Configure(EntityTypeBuilder<FeedbackScreenshot> builder)
    {
        builder.ToTable("feedback_screenshots");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasConversion(id => id.Value, value => new FeedbackScreenshotId(value))
            .ValueGeneratedNever();

        builder.Property(s => s.SubmissionId)
            .HasConversion(id => id.Value, value => new FeedbackSubmissionId(value))
            .IsRequired();
        builder.HasIndex(s => s.SubmissionId)
            .IsUnique()
            .HasDatabaseName("ux_feedback_screenshots_submission_id");

        builder.Property(s => s.JobSeekerId)
            .HasConversion(id => id.Value, value => new JobSeekerId(value))
            .IsRequired();
        builder.HasIndex(s => s.JobSeekerId)
            .HasDatabaseName("ix_feedback_screenshots_job_seeker_id");

        builder.Ignore(s => s.Content);
        builder.Property<byte[]>("_content")
            .HasField("_content")
            .HasColumnName("content")
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(s => s.Width).IsRequired();
        builder.Property(s => s.Height).IsRequired();
        builder.Property(s => s.SubmittedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Ignore(s => s.DomainEvents);
    }
}
