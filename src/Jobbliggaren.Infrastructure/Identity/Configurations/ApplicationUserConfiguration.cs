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

        // #508 (ADR 0024 D6) — created_at drives the orphan-sweep grace window.
        // HasDefaultValueSql("now()") makes the column ValueGeneratedOnAdd: the DB
        // stamps now() on INSERT when the property is the CLR sentinel
        // (default(DateTimeOffset)), so UserManager.CreateAsync needs no extra wiring
        // in RegisterCommandHandler. An explicit non-default value (tests) is inserted
        // verbatim. Migration backfills pre-existing rows to a past timestamp so they
        // are immediately sweepable (they predate the column → not mid-registration).
        builder.Property(u => u.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(u => new { u.Provider, u.ProviderUserId })
            .IsUnique()
            .HasFilter("\"provider_user_id\" IS NOT NULL")
            .HasDatabaseName("ix_asp_net_users_provider_provider_user_id");
    }
}
