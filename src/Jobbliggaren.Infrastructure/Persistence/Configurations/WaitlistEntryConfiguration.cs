using Jobbliggaren.Domain.Invitations;
using Jobbliggaren.Domain.Waitlist;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

public sealed class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> builder)
    {
        builder.ToTable("waitlist_entries");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasConversion(id => id.Value, value => new WaitlistEntryId(value))
            .ValueGeneratedNever();

        builder.Property(w => w.Email)
            .HasMaxLength(254)
            .IsRequired();

        builder.Property(w => w.Name)
            .HasMaxLength(WaitlistEntry.NameMaxLength)
            .IsRequired();

        builder.Property(w => w.Motivation)
            .HasMaxLength(WaitlistEntry.MotivationMaxLength)
            .IsRequired();

        builder.Property(w => w.RequestedAt).IsRequired();

        builder.Property(w => w.Status)
            .HasConversion(
                s => s.Name,
                v => WaitlistStatus.FromName(v))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(w => w.ApprovedAt);
        builder.Property(w => w.ApprovedByAdminId);
        builder.Property(w => w.RejectedAt);
        builder.Property(w => w.RejectedByAdminId);

        builder.Property(w => w.ResultingInvitationId)
            .HasConversion(
                id => id == null ? (Guid?)null : id.Value.Value,
                value => value == null ? (InvitationId?)null : new InvitationId(value.Value));

        // GDPR Art. 7-acceptance-record. Användarvillkor + nödvändiga cookies
        // levereras under Art. 6(1)(b) "performance of contract" (submit = acceptance),
        // ingen separat consent-checkbox. Endast MarketingEmailAccepted är genuint
        // Art. 7-samtycke. CTO-dom 2026-05-24 Fynd 1 Approach B.
        builder.OwnsOne(w => w.Acceptance, acceptance =>
        {
            acceptance.Property(a => a.MarketingEmailAccepted)
                .HasColumnName("marketing_email_accepted")
                .IsRequired();
            acceptance.Property(a => a.AcceptedAt)
                .HasColumnName("accepted_at")
                .IsRequired();
            acceptance.Property(a => a.PrivacyPolicyVersion)
                .HasColumnName("privacy_policy_version")
                .HasMaxLength(50)
                .IsRequired();
        });
        builder.Navigation(w => w.Acceptance).IsRequired();

        // Admin pollar "vilka pending finns" → status-first compound index.
        builder.HasIndex(w => new { w.Status, w.RequestedAt });

        // Spam-skydd och dedup: en email kan inte ha flera Pending-poster.
        // Approved/Rejected får dock samexistera med ny Pending (re-application
        // efter rejection). Partial index löser detta — filter på Status.
        builder.HasIndex(w => w.Email)
            .HasFilter("status = 'Pending'")
            .IsUnique();

        builder.Ignore(w => w.DomainEvents);
    }
}
