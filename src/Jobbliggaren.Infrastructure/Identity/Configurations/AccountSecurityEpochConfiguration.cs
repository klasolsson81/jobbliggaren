using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Identity.Configurations;

internal sealed class AccountSecurityEpochConfiguration : IEntityTypeConfiguration<AccountSecurityEpoch>
{
    public void Configure(EntityTypeBuilder<AccountSecurityEpoch> builder)
    {
        builder.ToTable("account_security_epoch", "identity", table =>
        {
            table.HasCheckConstraint("ck_account_security_epoch_singleton", "id = 1");
            table.HasCheckConstraint("ck_account_security_epoch_nonnegative", "value >= 0");
        });
        builder.HasKey(epoch => epoch.Id);
        builder.Property(epoch => epoch.Id).ValueGeneratedNever();
        builder.Property(epoch => epoch.Value).IsRequired();
        builder.HasData(new { Id = AccountSecurityEpoch.SingletonId, Value = 0L });
    }
}
