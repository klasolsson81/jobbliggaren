using Jobbliggaren.Domain.CompanyWatches;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// ADR 0087 D3 (#311 PR-3) — EF Core configuration for <see cref="CompanyWatch"/>.
///
/// <para>
/// <b>org.nr is PLAINTEXT (ADR 0087 D8(b), Klas Art. 32 risk-accept 2026-06-30).</b> The
/// <c>organization_number</c> column is stored plaintext — NOT DEK-encrypted — because the PR-4
/// <c>CompanyWatchScanJob</c> must equality/IN-match it (a DEK breaks SQL IN; the
/// <c>ef_strongly_typed_vo_contains</c> trap). Its protection is owner-scoped access + Art. 17
/// cascade erasability (the by-UserId RemoveRange in <c>AccountHardDeleter</c>), NOT encryption.
/// A sole-prop (enskild firma) org.nr can equal a personnummer — the guard for THAT lives at the
/// surfacing/log boundary (<c>OrganizationNumber.IsPersonnummerShaped</c> in the read DTO; org.nr
/// is never logged un-flagged, CLAUDE.md §5). The DEK envelope (ADR 0049/0066) stays reserved for
/// high-sensitivity user-authored PII (CV/cover-letter), not public-source ingest org.nr.
/// </para>
///
/// <para>
/// <b>Single-row resurrect (FORK B1):</b> the active-partial <c>UNIQUE(user_id, organization_number)
/// WHERE deleted_at IS NULL</c> guarantees ≤1 ACTIVE follow per (user, org.nr) — it guards the
/// concurrent-fresh-follow race; the resurrect handler avoids inserting a second physical row.
/// </para>
/// </summary>
public sealed class CompanyWatchConfiguration : IEntityTypeConfiguration<CompanyWatch>
{
    public void Configure(EntityTypeBuilder<CompanyWatch> builder)
    {
        builder.ToTable("company_watches");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasConversion(id => id.Value, value => new CompanyWatchId(value))
            .ValueGeneratedNever();

        builder.Property(w => w.UserId)
            .IsRequired();

        // org.nr VO ↔ plaintext text(10) (D8(b) — see class summary). FromTrusted: the DB value was
        // validated by OrganizationNumber.Create on write (parity with the strongly-typed Id idiom).
        builder.Property(w => w.OrganizationNumber)
            .HasConversion(o => o.Value, value => OrganizationNumber.FromTrusted(value))
            .HasColumnName("organization_number")
            .HasMaxLength(10)
            .IsRequired();

        // Single-member enum stored by NAME (reorder-safe; parity with NotifiableMatchGrade /
        // NotificationStatus). Forward-compatible: a new member (BRAND_GROUP, D4) is additive.
        builder.Property(w => w.TargetType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(w => w.CreatedAt).IsRequired();
        builder.Property(w => w.DeletedAt);

        // FORK B1 — active-partial UNIQUE: at most one ACTIVE follow per (user, org.nr). The
        // resurrect handler keeps it one physical row total. Quoted snake_case filter (parity with
        // JobAdConfiguration's external_id partial unique).
        builder.HasIndex(w => new { w.UserId, w.OrganizationNumber })
            .IsUnique()
            .HasFilter("\"deleted_at\" IS NULL")
            .HasDatabaseName("ux_company_watches_user_orgnr_active");

        // UserId index for the "my watches" scope query + the Art. 17 cascade-by-user sweep
        // (AccountHardDeleter) + the PR-4 scan's per-user grouping.
        builder.HasIndex(w => w.UserId)
            .HasDatabaseName("ix_company_watches_user_id");

        // Soft-delete: unfollowed rows hidden from normal queries (retained for the Art. 17 audit
        // posture; the cascade IgnoreQueryFilters to erase them on hard-delete).
        builder.HasQueryFilter(w => w.DeletedAt == null);

        builder.Ignore(w => w.DomainEvents);
    }
}
