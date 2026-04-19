using JobbPilot.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobbPilot.Infrastructure.Persistence.Configurations;

public sealed class JobAdConfiguration : IEntityTypeConfiguration<JobAd>
{
    public void Configure(EntityTypeBuilder<JobAd> builder)
    {
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id)
            .HasConversion(id => id.Value, value => new JobAdId(value))
            .ValueGeneratedNever();

        builder.Property(j => j.Title).HasMaxLength(300).IsRequired();
        builder.Property(j => j.Description).IsRequired();
        builder.Property(j => j.Url).HasMaxLength(2000).IsRequired();
        builder.Property(j => j.PublishedAt).IsRequired();
        builder.Property(j => j.ExpiresAt);
        builder.Property(j => j.CreatedAt).IsRequired();
        builder.Property(j => j.DeletedAt);

        builder.OwnsOne(j => j.Company, company =>
        {
            company.Property(c => c.Name)
                .HasMaxLength(200)
                .IsRequired();
        });

        builder.Property(j => j.Status)
            .HasConversion(s => s.Value, v => JobAdStatus.FromValue(v).Value)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(j => j.Source)
            .HasConversion(s => s.Value, v => JobSource.FromValue(v).Value)
            .HasMaxLength(50)
            .IsRequired();

        builder.HasQueryFilter(j => j.DeletedAt == null);

        builder.Ignore(j => j.DomainEvents);
    }
}
