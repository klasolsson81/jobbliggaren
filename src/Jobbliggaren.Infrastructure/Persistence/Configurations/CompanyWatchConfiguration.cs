using Jobbliggaren.Domain.CompanyWatches;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

/// <summary>
/// ADR 0087 D3 (#311 PR-3) — EF Core configuration for <see cref="CompanyWatch"/>.
///
/// <para>
/// <b>org.nr at-rest posture — AB plaintext, enskild firma HMAC-tokenised (ADR 0090 D5 / #544;
/// supersedes the ADR 0087 D8(b) plaintext-only accept for the pnr-shaped subset).</b> A legal-entity
/// (AB etc.) <c>organization_number</c> is public data and stays PLAINTEXT — <c>CompanyWatchScanJob</c>
/// equality/IN-matches it directly (a DEK breaks SQL IN; the <c>ef_strongly_typed_vo_contains</c>
/// trap). A personnummer-shaped (enskild firma) org.nr <i>equals</i> the owner's personnummer, so it
/// is stored as a keyed HMAC token (<c>IProtectedIdentityTokenizer</c>) — never plaintext at rest —
/// while staying deterministically equality-matchable. The write-time discriminator is the
/// single-sourced <c>OrganizationNumber.IsPersonnummerShaped</c> (B2), applied once at the
/// <c>CompanyWatchFollowExecutor</c> seam. Both forms share owner-scoped access + Art. 17 cascade
/// erasability (the by-UserId RemoveRange in <c>AccountHardDeleter</c>). The surfacing/log guard
/// (<c>IsPersonnummerShaped</c> in the read DTO; org.nr never logged un-flagged, CLAUDE.md §5) is
/// unchanged and still fires on a token (length≠10 fail-safe). The DEK envelope (ADR 0049/0066) stays
/// reserved for high-sensitivity user-authored PII (CV/cover-letter), not this key.
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

        // org.nr VO ↔ text (#544, ADR 0090 D5 — see class summary). The column holds EITHER a public
        // 10-digit AB org.nr (plaintext) OR a 64-char keyed HMAC token for an enskild-firma
        // (pnr-shaped) org.nr. FromTrusted: the DB value is a validated org.nr OR a token, both written
        // through the executor seam (parity with the strongly-typed Id idiom). Widened 10 → 64 to hold
        // the token; the deterministic token keeps the unique index + equality-match intact.
        builder.Property(w => w.OrganizationNumber)
            .HasConversion(o => o.Value, value => OrganizationNumber.FromTrusted(value))
            .HasColumnName("organization_number")
            .HasMaxLength(64)
            .IsRequired();

        // Single-member enum stored by NAME (reorder-safe; parity with NotifiableMatchGrade /
        // NotificationStatus). Forward-compatible: a new member (BRAND_GROUP, D4) is additive.
        builder.Property(w => w.TargetType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(w => w.CreatedAt).IsRequired();
        builder.Property(w => w.DeletedAt);

        // Per-watch filter (RF-2, 2026-07-12) — nullable jsonb via property-level converter
        // (NOT OwnsOne().ToJson(): it does not map IReadOnlyList<string> stably, Npgsql #3129 —
        // the MatchPreferences precedent). EF never invokes the converter for null → SQL NULL
        // round-trips as CLR null = no filter; existing rows are back-compatible for free.
        // Non-generic ValueConverter cast: the helper is non-null-typed while the property is
        // nullable (the known nullable-jsonb-VO trap). No index — the filter is always read
        // via the watch row, never queried by content.
        var filter = builder.Property(w => w.Filter)
            .HasConversion((ValueConverter)WatchFilterSpecConversion.Converter)
            .HasColumnName("filter")
            .HasColumnType("jsonb");
        filter.Metadata.SetValueComparer(WatchFilterSpecConversion.Comparer);

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
