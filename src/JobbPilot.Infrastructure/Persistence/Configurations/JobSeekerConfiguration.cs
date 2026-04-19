using JobbPilot.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobbPilot.Infrastructure.Persistence.Configurations;

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

        builder.Property(js => js.CreatedAt).IsRequired();
        builder.Property(js => js.UpdatedAt);
        builder.Property(js => js.DeletedAt);

        builder.HasQueryFilter(js => js.DeletedAt == null);

        builder.Ignore(js => js.DomainEvents);
    }
}
