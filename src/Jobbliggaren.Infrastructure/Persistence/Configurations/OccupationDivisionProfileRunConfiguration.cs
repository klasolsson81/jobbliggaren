using Jobbliggaren.Infrastructure.CompanyRegister;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// #1682 — schema for the profile's single state row. See
/// <see cref="OccupationDivisionProfileRowConfiguration"/> for why it is Infrastructure-internal.
/// </summary>
internal sealed class OccupationDivisionProfileRunConfiguration
    : IEntityTypeConfiguration<OccupationDivisionProfileRun>
{
    public void Configure(EntityTypeBuilder<OccupationDivisionProfileRun> builder)
    {
        builder.ToTable("occupation_division_profile_runs");

        builder.HasKey(r => r.ProfileKey);

        builder.Property(r => r.ProfileKey)
            .HasColumnName("profile_key")
            .HasMaxLength(20)
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(r => r.ProfiledAt)
            .HasColumnName("profiled_at")
            .IsRequired();

        builder.Property(r => r.OccupationGroupsProfiled)
            .HasColumnName("occupation_groups_profiled")
            .IsRequired();

        builder.Property(r => r.RowsWritten)
            .HasColumnName("rows_written")
            .IsRequired();

        builder.Property(r => r.AdsCounted)
            .HasColumnName("ads_counted")
            .IsRequired();

        builder.Property(r => r.AdsNotInRegister)
            .HasColumnName("ads_not_in_register")
            .IsRequired();

        builder.Property(r => r.AdsInRegisterWithoutSni)
            .HasColumnName("ads_in_register_without_sni")
            .IsRequired();
    }
}
