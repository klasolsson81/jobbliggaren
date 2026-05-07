using JobbPilot.Domain.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobbPilot.Infrastructure.Persistence.Configurations;

public sealed class FollowUpConfiguration : IEntityTypeConfiguration<FollowUp>
{
    public void Configure(EntityTypeBuilder<FollowUp> builder)
    {
        builder.ToTable("follow_ups");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id)
            .HasConversion(id => id.Value, value => new FollowUpId(value))
            .ValueGeneratedNever();

        builder.Property(f => f.Channel)
            .HasConversion(
                c => c.Name,
                v => FollowUpChannel.FromName(v))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(f => f.ScheduledAt).IsRequired();

        // TODO(GDPR): kryptera med KMS-backed value converter innan prod-release
        builder.Property(f => f.Note).HasMaxLength(2000);

        builder.Property(f => f.Outcome)
            .HasConversion(
                o => o.Name,
                v => FollowUpOutcome.FromName(v))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(f => f.OutcomeAt);
        builder.Property(f => f.CreatedAt).IsRequired();
        builder.Property(f => f.DeletedAt);

        builder.HasQueryFilter(f => f.DeletedAt == null);
    }
}
