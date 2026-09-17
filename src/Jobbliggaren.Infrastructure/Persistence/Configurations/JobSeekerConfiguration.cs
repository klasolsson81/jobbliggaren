using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jobbliggaren.Infrastructure.Persistence.Configurations;

public sealed class JobSeekerConfiguration : IEntityTypeConfiguration<JobSeeker>
{
    public void Configure(EntityTypeBuilder<JobSeeker> builder)
    {
        // #1736 (ADR 0142 D6 amendment) — the terms stamp is all-or-nothing at the database: the
        // optional owned mapping below reads "no stamp" from all three columns NULL, and this
        // constraint holds that sentinel where raw SQL lives (the account-deletion runbook updates
        // job_seekers directly). A half-stamped row is a state the aggregate has no reading for. The
        // pre-migration rows (all three NULL) satisfy it; every stamped row has all three set.
        builder.ToTable("job_seekers", t => t.HasCheckConstraint(
            "ck_job_seekers_terms_all_or_none",
            "num_nonnulls(terms_accepted_at, terms_version, privacy_policy_version) IN (0, 3)"));

        builder.HasKey(js => js.Id);
        builder.Property(js => js.Id)
            .HasConversion(id => id.Value, value => new JobSeekerId(value))
            .ValueGeneratedNever();

        builder.Property(js => js.UserId).IsRequired();
        builder.HasIndex(js => js.UserId).IsUnique();

        builder.Property(js => js.DisplayName).HasMaxLength(200).IsRequired();

        builder.OwnsOne(js => js.Preferences, prefs =>
        {
            prefs.ToJson();
        });

        // ADR 0142 D6 (#1736) — the terms-acceptance stamp as an OPTIONAL owned type over three plain
        // columns (parity with Preferences' OwnsOne; NOT ToJson — an accountability record stays
        // queryable). Explicit HasColumnName on every property: the global snake_case convention
        // would otherwise prefix the navigation name (terms_acceptance_*). Navigation.IsRequired(false)
        // is obligatory, the ManualPosting/AdSnapshot form: EF Core 10 defaults an owned reference to
        // required. The all-null sentinel is unambiguous by construction here: every property is non-nullable inside the
        // type, so a present stamp never writes an all-null row (the property AdSnapshot has to argue
        // for via captured_at). varchar(20) is headroom over a 10-char ISO date; the value has no
        // Domain reader that would give a shared constant a consumer.
        builder.OwnsOne(js => js.TermsAcceptance, terms =>
        {
            terms.Property(t => t.AcceptedAt).HasColumnName("terms_accepted_at");
            terms.Property(t => t.TermsVersion)
                .HasColumnName("terms_version")
                .HasMaxLength(20);
            terms.Property(t => t.PrivacyPolicyVersion)
                .HasColumnName("privacy_policy_version")
                .HasMaxLength(20);
        });
        builder.Navigation(js => js.TermsAcceptance).IsRequired(false);

        // F4-12 (ADR 0076) — MatchPreferences as a jsonb column via a property-level
        // ValueConverter (parity with SearchCriteria; OwnsOne().ToJson() does not map
        // IReadOnlyList<string> stably, Npgsql #3129). Comparer carries the VO's
        // structural equality. Column default '{}' → an existing row deserializes to
        // MatchPreferences.Empty (the converter treats missing keys as empty lists).
        var matchPreferences = builder.Property(js => js.MatchPreferences)
            .HasConversion(MatchPreferencesConversion.Converter)
            .HasColumnName("match_preferences")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();
        matchPreferences.Metadata.SetValueComparer(MatchPreferencesConversion.Comparer);

        // ADR 0058 + ADR 0059: primary-state ägs av JobSeeker-aggregatet
        // (Alt A2 per senior-cto-advisor 2026-05-20). Ingen FK till resumes
        // — soft-delete-mönster + cascade-handler i DeleteResumeCommandHandler
        // håller konsistens (motivering i ADR 0059 + architect-design).
        builder.Property(js => js.PrimaryResumeId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ResumeId(value.Value) : null)
            .HasColumnName("primary_resume_id");

        builder.HasIndex(js => js.PrimaryResumeId);

        // ADR 0080 Vag 4 — two distinct per-user watermarks (first-class columns,
        // NOT jsonb — both are hot-path-updated; see JobSeeker.cs field comments).
        builder.Property(js => js.LastMatchScanAt);
        builder.Property(js => js.LastSeenMatchesAt);
        // #293 (ADR 0042 Beslut E amendment) — user-read watermark for the /jobb surface
        // (first-class nullable column, sibling of last_seen_matches_at).
        builder.Property(js => js.LastSeenJobsAt);
        // ADR 0087 D5 (#311 PR-4) — company-follow scan high-water-mark (first-class nullable
        // column, sibling of last_match_scan_at; advanced atomically by CompanyWatchScanJob).
        builder.Property(js => js.LastCompanyWatchScanAt);
        // Bevakning F2 (#801, RF-6=6B) — company-follow USER-read watermark for the in-app follow
        // rail (first-class nullable column, sibling of last_seen_matches_at/last_seen_jobs_at;
        // advanced when the user visits /foretag). DISTINCT from last_company_watch_scan_at above
        // (system scan mark) — see JobSeeker.cs field comment.
        builder.Property(js => js.LastSeenFollowedAdsAt);

        builder.Property(js => js.CreatedAt).IsRequired();
        builder.Property(js => js.UpdatedAt);
        builder.Property(js => js.DeletedAt);

        builder.HasQueryFilter(js => js.DeletedAt == null);

        builder.Ignore(js => js.DomainEvents);
    }
}
