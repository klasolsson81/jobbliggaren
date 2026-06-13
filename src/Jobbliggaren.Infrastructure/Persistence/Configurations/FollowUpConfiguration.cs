using Jobbliggaren.Domain.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

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

        // TD-13 (ADR 0049 C3): krypteras via interceptor-paret. HasMaxLength
        // borttagen (ciphertext > klartext-cap; TEXT obegränsad). Nullable —
        // null Note förblir null (ej krypterad).
        builder.Property(f => f.Note);

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
