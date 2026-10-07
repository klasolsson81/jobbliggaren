using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

public sealed class FeedbackNotificationConfiguration : IEntityTypeConfiguration<FeedbackNotification>
{
    public void Configure(EntityTypeBuilder<FeedbackNotification> builder)
    {
        builder.ToTable("feedback_notifications");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id)
            .HasConversion(id => id.Value, value => new FeedbackNotificationId(value))
            .ValueGeneratedNever();

        // One notice per submission. A soft reference by id (ADR 0011): retention and the account
        // cascade delete both rows explicitly, so no foreign key carries the lifecycle.
        builder.Property(n => n.SubmissionId)
            .HasConversion(id => id.Value, value => new FeedbackSubmissionId(value))
            .IsRequired();
        builder.HasIndex(n => n.SubmissionId)
            .IsUnique()
            .HasDatabaseName("ux_feedback_notifications_submission_id");

        builder.Property(n => n.JobSeekerId)
            .HasConversion(id => id.Value, value => new JobSeekerId(value))
            .IsRequired();
        builder.HasIndex(n => n.JobSeekerId)
            .HasDatabaseName("ix_feedback_notifications_job_seeker_id");

        builder.Property(n => n.State)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(n => n.Attempts).IsRequired();
        builder.Property(n => n.NextAttemptAt).IsRequired();
        builder.Property(n => n.SendingStartedAt);
        builder.Property(n => n.AcceptedAt);
        builder.Property(n => n.StateChangedAt);
        builder.Property(n => n.CreatedAt).IsRequired();

        // The dispatch job reads due Queued rows and stale Sending rows.
        builder.HasIndex(n => new { n.State, n.NextAttemptAt })
            .HasDatabaseName("ix_feedback_notifications_state_next_attempt_at");

        // The Worker's claim and an administrator's requeue can meet on one row.
        builder.Property<uint>("xmin")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.Ignore(n => n.DomainEvents);
    }
}
