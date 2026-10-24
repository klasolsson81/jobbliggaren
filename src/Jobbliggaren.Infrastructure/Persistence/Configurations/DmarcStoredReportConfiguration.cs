using Jobbliggaren.Infrastructure.Email.Dmarc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// #1981 PR 4a (ADR 0183) — EF Core configuration for <c>dmarc_reports</c>. The table is written and read through
/// raw SQL inside Infrastructure, so this configuration exists for the migration schema and the retention's delete.
/// </summary>
internal sealed class DmarcStoredReportConfiguration : IEntityTypeConfiguration<DmarcStoredReport>
{
    public void Configure(EntityTypeBuilder<DmarcStoredReport> builder)
    {
        builder.ToTable("dmarc_reports", t =>
        {
            t.HasCheckConstraint("ck_dmarc_reports_period", "ends_at > begins_at");
            t.HasCheckConstraint("ck_dmarc_reports_content_sha256", "octet_length(content_sha256) = 32");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.Reporter).HasMaxLength(255).IsRequired();
        builder.Property(r => r.ReporterReportId).HasMaxLength(255).IsRequired();
        builder.Property(r => r.ContentSha256).IsRequired();
        builder.Property(r => r.BeginsAt).IsRequired();
        builder.Property(r => r.EndsAt).IsRequired();
        builder.Property(r => r.ReceivedAt).IsRequired();

        builder.HasIndex(r => new { r.Reporter, r.ReporterReportId, r.ContentSha256 })
            .IsUnique()
            .HasDatabaseName("ux_dmarc_reports_reporter_report_id_content_sha256");
        builder.HasIndex(r => r.EndsAt).HasDatabaseName("ix_dmarc_reports_ends_at");
        builder.HasIndex(r => r.ReceivedAt).HasDatabaseName("ix_dmarc_reports_received_at");
    }
}
