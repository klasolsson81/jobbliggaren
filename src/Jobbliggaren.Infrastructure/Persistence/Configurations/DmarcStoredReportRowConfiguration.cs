using Jobbliggaren.Infrastructure.Email.Dmarc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// #1981 PR 4a (ADR 0184) — EF Core configuration for <c>dmarc_report_rows</c>. The key's prefix is the report. The two
/// results are stored by number, as PR 3's counts are.
/// </summary>
internal sealed class DmarcStoredReportRowConfiguration : IEntityTypeConfiguration<DmarcStoredReportRow>
{
    public void Configure(EntityTypeBuilder<DmarcStoredReportRow> builder)
    {
        builder.ToTable("dmarc_report_rows", t =>
            t.HasCheckConstraint(
                "ck_dmarc_report_rows_positive",
                "ordinal >= 0 AND message_count > 0 AND dkim_aligned > 0 AND spf_aligned > 0"));

        builder.HasKey(r => new { r.ReportId, r.Ordinal });
        builder.Property(r => r.ReportId).ValueGeneratedNever();
        builder.Property(r => r.Ordinal).ValueGeneratedNever();

        builder.Property(r => r.SourceIp).HasColumnType("inet").IsRequired();
        builder.Property(r => r.MessageCount).IsRequired();
        builder.Property(r => r.HeaderFrom).HasMaxLength(253).IsRequired();
        builder.Property(r => r.DkimAligned).HasConversion<short>().IsRequired();
        builder.Property(r => r.SpfAligned).HasConversion<short>().IsRequired();

        builder.HasOne<DmarcStoredReport>()
            .WithMany()
            .HasForeignKey(r => r.ReportId)
            .HasConstraintName("fk_dmarc_report_rows_dmarc_reports_report_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
