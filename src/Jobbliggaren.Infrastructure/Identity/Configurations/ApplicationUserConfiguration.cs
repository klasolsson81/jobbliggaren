using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Identity.Configurations;

// Jobbliggaren-konvention: standard C# enums lagras som string i DB.
// Ger läsbar data i pg_dump och migrationssäkerhet vid enum-refactoring.
// SmartEnum-records (JobAdStatus, JobSource) använder lambda-conversion separat.
internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.Provider)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(AuthProvider.Local)
            .IsRequired();

        builder.Property(u => u.ProviderUserId)
            .HasMaxLength(255);

        builder.HasIndex(u => new { u.Provider, u.ProviderUserId })
            .IsUnique()
            .HasFilter("\"provider_user_id\" IS NOT NULL")
            .HasDatabaseName("ix_asp_net_users_provider_provider_user_id");
    }
}
