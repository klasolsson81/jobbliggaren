using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

public sealed class JobSeekerConfiguration : IEntityTypeConfiguration<JobSeeker>
{
    public void Configure(EntityTypeBuilder<JobSeeker> builder)
    {
        builder.ToTable("job_seekers");

        builder.HasKey(js => js.Id);
        builder.Property(js => js.Id)
            .HasConversion(id => id.Value, value => new JobSeekerId(value))
            .ValueGeneratedNever();

        builder.Property(js => js.UserId).IsRequired();
        builder.HasIndex(js => js.UserId).IsUnique();

        builder.Property(js => js.DisplayName).HasMaxLength(200).IsRequired();

        builder.OwnsOne(js => js.Preferences, prefs =>
        {
            prefs.ToJson();
        });

        // ADR 0058 + ADR 0059: primary-state ägs av JobSeeker-aggregatet
        // (Alt A2 per senior-cto-advisor 2026-05-20). Ingen FK till resumes
        // — soft-delete-mönster + cascade-handler i DeleteResumeCommandHandler
        // håller konsistens (motivering i ADR 0059 + architect-design).
        builder.Property(js => js.PrimaryResumeId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ResumeId(value.Value) : null)
            .HasColumnName("primary_resume_id");

        builder.HasIndex(js => js.PrimaryResumeId);

        builder.Property(js => js.CreatedAt).IsRequired();
        builder.Property(js => js.UpdatedAt);
        builder.Property(js => js.DeletedAt);

        builder.HasQueryFilter(js => js.DeletedAt == null);

        builder.Ignore(js => js.DomainEvents);
    }
}
