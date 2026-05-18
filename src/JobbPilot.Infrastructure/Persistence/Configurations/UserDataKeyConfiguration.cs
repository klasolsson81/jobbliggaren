using JobbPilot.Domain.JobSeekers;
using JobbPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobbPilot.Infrastructure.Persistence.Configurations;

/// <summary>
/// TD-13 (ADR 0049) — <c>user_data_keys</c>. Keyless-på-aggregat-vis: ingen
/// EF-navigation till JobSeeker (CTO FRÅGA 2). FK till job_seekers läggs på
/// DB-nivå i migrationen (db-migration-writer) — inte som EF-relation, så
/// ingen navigations-yta uppstår. Ingen query-filter (ej soft-deletable;
/// crypto-erasure är hard-delete via C6).
/// </summary>
public sealed class UserDataKeyConfiguration : IEntityTypeConfiguration<UserDataKey>
{
    public void Configure(EntityTypeBuilder<UserDataKey> builder)
    {
        builder.ToTable("user_data_keys");

        // PK (job_seeker_id, dek_version) — stödjer DEK-rotation (ADR 0049 Beslut 4).
        builder.HasKey(k => new { k.JobSeekerId, k.DekVersion });

        builder.Property(k => k.JobSeekerId)
            .HasColumnName("job_seeker_id")
            .HasConversion(id => id.Value, value => new JobSeekerId(value))
            .IsRequired();

        builder.Property(k => k.DekVersion)
            .HasColumnName("dek_version")
            .IsRequired();

        builder.Property(k => k.WrappedDek)
            .HasColumnName("wrapped_dek")
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(k => k.CmkKeyId)
            .HasColumnName("cmk_key_id")
            .HasMaxLength(2048)
            .IsRequired();

        builder.Property(k => k.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();
    }
}
