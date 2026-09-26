using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Identity.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        // #508 (ADR 0024 D6) — created_at drives the orphan-sweep grace window.
        // HasDefaultValueSql("now()") makes the column ValueGeneratedOnAdd: the DB
        // stamps now() on INSERT when the property is the CLR sentinel
        // (default(DateTimeOffset)), so UserManager.CreateAsync needs no extra wiring.
        // An explicit non-default value (tests) is inserted
        // verbatim. Migration backfills pre-existing rows to a past timestamp so they
        // are immediately sweepable (they predate the column → not mid-registration).
        builder.Property(u => u.CreatedAt)
            .HasDefaultValueSql("now()")
            .IsRequired();
    }
}
