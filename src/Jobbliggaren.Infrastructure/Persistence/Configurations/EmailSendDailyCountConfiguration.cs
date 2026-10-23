using Jobbliggaren.Infrastructure.Email.Counting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// #1981 PR 3 (ADR 0180) — EF Core configuration for <c>email_send_daily_counts</c>. The table is reached only through
/// SQL inside Infrastructure, so this configuration exists for the migration schema. Kind and outcome are stored by
/// number, against the by-name house rule: a <c>smallint</c> column cannot carry text, so the model's text sweeps
/// would catch any text column added here later.
/// </summary>
internal sealed class EmailSendDailyCountConfiguration : IEntityTypeConfiguration<EmailSendDailyCount>
{
    public void Configure(EntityTypeBuilder<EmailSendDailyCount> builder)
    {
        builder.ToTable("email_send_daily_counts", t =>
            t.HasCheckConstraint(
                "ck_email_send_daily_counts_positive", "attempts > 0 AND kind > 0 AND outcome > 0"));

        builder.HasKey(c => new { c.Day, c.Kind, c.Outcome });

        builder.Property(c => c.Day).HasColumnType("date").ValueGeneratedNever();
        builder.Property(c => c.Kind).HasConversion<short>().ValueGeneratedNever();
        builder.Property(c => c.Outcome).HasConversion<short>().ValueGeneratedNever();
        builder.Property(c => c.Attempts).IsRequired();
    }
}
