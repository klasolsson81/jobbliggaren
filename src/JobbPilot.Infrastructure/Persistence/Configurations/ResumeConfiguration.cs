using JobbPilot.Domain.JobSeekers;
using JobbPilot.Domain.Resumes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobbPilot.Infrastructure.Persistence.Configurations;

public sealed class ResumeConfiguration : IEntityTypeConfiguration<Resume>
{
    public void Configure(EntityTypeBuilder<Resume> builder)
    {
        builder.ToTable("resumes");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, value => new ResumeId(value))
            .ValueGeneratedNever();

        builder.Property(r => r.JobSeekerId)
            .HasConversion(id => id.Value, value => new JobSeekerId(value))
            .IsRequired();

        builder.HasIndex(r => r.JobSeekerId);

        builder.Property(r => r.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.UpdatedAt).IsRequired();
        builder.Property(r => r.DeletedAt);

        // xmin är PostgreSQL-systemkolumn — ingen DDL-kolumn behövs, Npgsql mappar automatiskt
        builder.Property<uint>("xmin")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.HasMany(r => r.Versions)
            .WithOne()
            .HasForeignKey("ResumeId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // Backing-field-access på _versions (privat List<ResumeVersion>) så EF kan
        // materialisera child-collection trots IReadOnlyList-exponering.
        builder.Metadata
            .FindNavigation(nameof(Resume.Versions))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasQueryFilter(r => r.DeletedAt == null);

        builder.Ignore(r => r.DomainEvents);
    }
}
