using Jobbliggaren.Infrastructure.Email.Dmarc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// #1981 PR 4a (ADR 0183) — EF Core configuration for the ledger <c>dmarc_mailbox_messages</c>. Epoch and position
/// are <c>bigint</c> because IMAP's are unsigned 32-bit. The limits on retries live in code, so retuning one is a
/// commit, not a migration.
/// </summary>
internal sealed class DmarcMailboxMessageConfiguration : IEntityTypeConfiguration<DmarcMailboxMessage>
{
    public void Configure(EntityTypeBuilder<DmarcMailboxMessage> builder)
    {
        builder.ToTable("dmarc_mailbox_messages", t =>
            t.HasCheckConstraint(
                "ck_dmarc_mailbox_messages_positive",
                "uid_validity > 0 AND uid > 0 AND outcome > 0 AND attempts BETWEEN 1 AND 2 AND retries >= 0"));

        builder.HasKey(m => new { m.UidValidity, m.Uid });
        builder.Property(m => m.UidValidity).ValueGeneratedNever();
        builder.Property(m => m.Uid).ValueGeneratedNever();

        builder.Property(m => m.ReceivedAt).IsRequired();
        builder.Property(m => m.Outcome).HasConversion<short>().IsRequired();
        builder.Property(m => m.Attempts).IsRequired();
        builder.Property(m => m.Retries).IsRequired();

        builder.HasIndex(m => m.ReceivedAt).HasDatabaseName("ix_dmarc_mailbox_messages_received_at");
    }
}
