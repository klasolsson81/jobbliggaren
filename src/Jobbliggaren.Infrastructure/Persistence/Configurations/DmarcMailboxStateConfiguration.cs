using Jobbliggaren.Infrastructure.Email.Dmarc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// #1981 PR 4a (ADR 0183) — the one-row ingestion state (the <c>email_send_counting</c> singleton form, without a
/// seed). The cursor's two halves are set together or not at all.
/// </summary>
internal sealed class DmarcMailboxStateConfiguration : IEntityTypeConfiguration<DmarcMailboxState>
{
    public void Configure(EntityTypeBuilder<DmarcMailboxState> builder)
    {
        builder.ToTable("dmarc_mailbox_state", t =>
        {
            t.HasCheckConstraint("ck_dmarc_mailbox_state_singleton", "id = 1");
            t.HasCheckConstraint(
                "ck_dmarc_mailbox_state_positive",
                "last_outcome > 0 AND (uid_validity IS NULL OR uid_validity > 0) AND (last_uid IS NULL OR last_uid >= 0) "
                + "AND ((uid_validity IS NULL) = (last_uid IS NULL))");
        });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.LastAttemptAt).IsRequired();
        builder.Property(s => s.LastOutcome).HasConversion<short>().IsRequired();
        builder.Property(s => s.LastSuccessAt);
        builder.Property(s => s.UidValidity);
        builder.Property(s => s.LastUid);
    }
}
