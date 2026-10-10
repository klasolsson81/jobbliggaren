using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

public sealed class FeedbackReplyConfiguration : IEntityTypeConfiguration<FeedbackReply>
{
    public void Configure(EntityTypeBuilder<FeedbackReply> builder)
    {
        builder.ToTable("feedback_replies");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, value => new FeedbackReplyId(value))
            .ValueGeneratedNever();

        // A soft reference by id (ADR 0011): retention and the account cascade delete replies
        // explicitly, so no foreign key carries the lifecycle.
        builder.Property(r => r.SubmissionId)
            .HasConversion(id => id.Value, value => new FeedbackSubmissionId(value))
            .IsRequired();

        builder.Property(r => r.ReplyKey).IsRequired();
        builder.HasIndex(r => new { r.SubmissionId, r.ReplyKey })
            .IsUnique()
            .HasDatabaseName("ux_feedback_replies_submission_id_reply_key");

        builder.Property(r => r.JobSeekerId)
            .HasConversion(id => id.Value, value => new JobSeekerId(value))
            .IsRequired();
        builder.HasIndex(r => r.JobSeekerId)
            .HasDatabaseName("ix_feedback_replies_job_seeker_id");

        builder.Property(r => r.Text)
            .HasConversion((ValueConverter)new ValueConverter<FeedbackReplyText, string>(
                text => text.Value,
                value => FeedbackReplyText.Create(value).Value))
            .HasColumnName("body")
            .HasMaxLength(FeedbackReplyText.MaxLength)
            .IsRequired();

        builder.Property(r => r.State)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(r => r.SubmittedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.CompletedAt);

        builder.Ignore(r => r.DomainEvents);
    }
}
