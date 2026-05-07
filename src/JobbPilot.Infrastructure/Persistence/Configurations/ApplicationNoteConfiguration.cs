using JobbPilot.Domain.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobbPilot.Infrastructure.Persistence.Configurations;

public sealed class ApplicationNoteConfiguration : IEntityTypeConfiguration<ApplicationNote>
{
    public void Configure(EntityTypeBuilder<ApplicationNote> builder)
    {
        builder.ToTable("application_notes");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id)
            .HasConversion(id => id.Value, value => new ApplicationNoteId(value))
            .ValueGeneratedNever();

        // TODO(GDPR): kryptera med KMS-backed value converter innan prod-release
        builder.Property(n => n.Content).HasMaxLength(5000).IsRequired();

        builder.Property(n => n.CreatedAt).IsRequired();
        builder.Property(n => n.DeletedAt);

        builder.HasQueryFilter(n => n.DeletedAt == null);
    }
}
