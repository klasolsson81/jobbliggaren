using Jobbliggaren.Infrastructure.Email.Counting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>#1981 PR 3 (ADR 0180) — the one-row recording start (the <c>taxonomy_snapshot_meta</c> singleton form).</summary>
internal sealed class EmailSendCountingConfiguration : IEntityTypeConfiguration<EmailSendCounting>
{
    public void Configure(EntityTypeBuilder<EmailSendCounting> builder)
    {
        builder.ToTable("email_send_counting", t =>
            t.HasCheckConstraint("ck_email_send_counting_singleton", "id = 1"));

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.StartedAt).IsRequired();
    }
}
