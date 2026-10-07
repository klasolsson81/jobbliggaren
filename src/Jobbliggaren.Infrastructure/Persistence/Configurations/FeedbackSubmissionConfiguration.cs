using Jobbliggaren.Domain.Feedback;
using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

public sealed class FeedbackSubmissionConfiguration : IEntityTypeConfiguration<FeedbackSubmission>
{
    public void Configure(EntityTypeBuilder<FeedbackSubmission> builder)
    {
        builder.ToTable("feedback_submissions", t => t.HasCheckConstraint(
            "ck_feedback_submissions_rating", $"rating BETWEEN {FeedbackRating.Min} AND {FeedbackRating.Max}"));

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasConversion(id => id.Value, value => new FeedbackSubmissionId(value))
            .ValueGeneratedNever();

        builder.Property(s => s.JobSeekerId)
            .HasConversion(id => id.Value, value => new JobSeekerId(value))
            .IsRequired();

        builder.Property(s => s.SubmissionKey).IsRequired();

        // The idempotency key is unique per owner; the submit handler turns a violation into a replay.
        builder.HasIndex(s => new { s.JobSeekerId, s.SubmissionKey })
            .IsUnique()
            .HasDatabaseName("ux_feedback_submissions_job_seeker_submission_key");

        builder.Property(s => s.Page)
            .HasConversion(page => page.Name, key => FeedbackPage.FromName(key, false))
            .HasColumnName("page_key")
            .HasMaxLength(40)
            .IsRequired();

        // Converters over the non-nullable types: EF passes NULL through without calling them.
        builder.Property(s => s.Rating)
            .HasConversion(new ValueConverter<FeedbackRating, int>(
                rating => rating.Value,
                value => FeedbackRating.Create(value).Value));

        builder.Property(s => s.Comment)
            .HasConversion((ValueConverter)new ValueConverter<FeedbackComment, string>(
                comment => comment.Value,
                text => FeedbackComment.Create(text).Value!))
            .HasMaxLength(FeedbackComment.MaxLength);

        builder.OwnsOne(s => s.Context, context =>
        {
            context.Property(c => c.ViewportWidth).HasColumnName("viewport_width");
            context.Property(c => c.ViewportHeight).HasColumnName("viewport_height");
            context.Property(c => c.ScreenWidth).HasColumnName("screen_width");
            context.Property(c => c.ScreenHeight).HasColumnName("screen_height");
            context.Property(c => c.PixelRatio).HasColumnName("pixel_ratio").HasPrecision(4, 2);
            context.Property(c => c.Theme).HasColumnName("reported_theme")
                .HasConversion<string>().HasMaxLength(16);
            context.Property(c => c.DeviceClass).HasColumnName("reported_device_class")
                .HasConversion<string>().HasMaxLength(16);
            context.Property(c => c.OsFamily).HasColumnName("reported_os_family")
                .HasConversion<string>().HasMaxLength(16);
            context.Property(c => c.BrowserFamily).HasColumnName("reported_browser_family")
                .HasConversion<string>().HasMaxLength(24);
        });
        builder.Navigation(s => s.Context).IsRequired();

        builder.Property(s => s.AppVersion).HasMaxLength(FeedbackSubmission.AppVersionMaxLength);

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(s => s.SubmittedAt).IsRequired();
        builder.Property(s => s.StatusChangedAt);

        // Retention deletes by age, statistics and the admin list read by window.
        builder.HasIndex(s => s.SubmittedAt)
            .HasDatabaseName("ix_feedback_submissions_submitted_at");

        builder.Ignore(s => s.DomainEvents);
    }
}
