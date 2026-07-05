using Jobbliggaren.Domain.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// StatusChange timeline (ADR 0092 D4). Same aggregate-owned-child pattern as
/// <see cref="FollowUpConfiguration"/>: a related entity with a shadow
/// <c>ApplicationId</c> FK + cascade delete (wired in
/// <see cref="ApplicationConfiguration"/>), NOT an EF owned type. From/To are the
/// ApplicationStatus SmartEnum stored as its name string (parity with
/// Status/Channel/Outcome). Explicit <c>from_status</c>/<c>to_status</c> column
/// names because bare <c>from</c>/<c>to</c> are SQL reserved words. Plaintext (no
/// DEK — status names + timestamps are not PII, ADR 0086 D5). Soft-delete query
/// filter mirrors FollowUp so an account cascade filters these rows out too.
/// </summary>
public sealed class StatusChangeConfiguration : IEntityTypeConfiguration<StatusChange>
{
    public void Configure(EntityTypeBuilder<StatusChange> builder)
    {
        builder.ToTable("application_status_changes");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasConversion(id => id.Value, value => new StatusChangeId(value))
            .ValueGeneratedNever();

        builder.Property(s => s.From)
            .HasConversion(
                st => st.Name,
                v => ApplicationStatus.FromName(v))
            .HasColumnName("from_status")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(s => s.To)
            .HasConversion(
                st => st.Name,
                v => ApplicationStatus.FromName(v))
            .HasColumnName("to_status")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(s => s.ChangedAt).IsRequired();
        builder.Property(s => s.DeletedAt);

        builder.HasQueryFilter(s => s.DeletedAt == null);
    }
}
