namespace Jobbliggaren.Domain.JobSeekers;

/// <summary>
/// The job-seeker's locale + notification preferences. Stored as jsonb on
/// <c>job_seekers.preferences</c>; the Vag 4 fields are ADDITIVE — a row written before
/// ADR 0080 deserializes the missing keys to their safe defaults (background-match
/// notifications OFF), so no migration is needed for the consent fields.
/// <para>
/// <b>ADR 0080 Vag 4 — consent (opt-in default OFF, GDPR Art. 6/7):</b>
/// <see cref="BackgroundMatchNotificationsEnabled"/> defaults <c>false</c> — background-match
/// notifications require an explicit user action (the SavedSearch opt-in precedent, ADR
/// 0039 Beslut 4). <see cref="NotificationConsentAt"/> is the Art. 7(1) consent evidence
/// (stamped once on first opt-in, immutable); <see cref="NotificationConsentWithdrawnAt"/>
/// is the Art. 7(3) revocation proof (stamped on opt-out, cleared on re-consent). Mutated
/// only via <see cref="JobSeeker.UpdateNotificationConsent"/>.
/// </para>
/// <para>
/// <b>ADR 0087 D5 (#311 PR-4) — SEPARATE company-follow consent (GDPR Art. 6/7 granularity):</b>
/// <see cref="FollowedCompanyNotificationsEnabled"/> is a DISTINCT opt-in (default <c>false</c>)
/// gating company-follow notifications — a different processing purpose than background-match
/// notifications, so it carries its OWN Art. 7(1)/7(3) evidence pair
/// (<see cref="FollowedCompanyNotificationConsentAt"/> / <see cref="FollowedCompanyNotificationConsentWithdrawnAt"/>).
/// Collapsing the two purposes onto one flag/timestamp would be an Art. 7 consent-granularity
/// violation (ADR 0087 D5). The digest CADENCE is deliberately SHARED (<see cref="DigestCadence"/>,
/// ADR 0087 D2) — cadence is one delivery-rhythm preference, not per-source. Mutated only via
/// <see cref="JobSeeker.UpdateFollowedCompanyNotificationConsent"/>. Additive jsonb — a pre-PR-4 row
/// deserializes the missing keys to OFF (no migration; the back-compat test covers it).
/// </para>
/// <para>
/// TD-115 (2026-06-25): the legacy <c>EmailNotifications</c> (opt-OUT default, Art.
/// 7-noncompliant) + <c>WeeklySummary</c> flags were RETIRED — they gated no email path
/// (the Vag 4 dispatch reads <see cref="BackgroundMatchNotificationsEnabled"/> + the
/// consent timestamps, never them), so they were dead, dishonest UI controls. Removing them
/// is back-compat-safe: this owned type maps via EF <c>OwnsOne(...).ToJson()</c>, which
/// ignores the now-unmapped <c>EmailNotifications</c>/<c>WeeklySummary</c> keys still present
/// in pre-TD-115 jsonb rows (no migration needed; proven by the back-compat test).
/// </para>
/// </summary>
public sealed record Preferences(
    string Language = "sv",
    bool BackgroundMatchNotificationsEnabled = false,
    DigestCadence DigestCadence = DigestCadence.Weekly,
    DateTimeOffset? NotificationConsentAt = null,
    DateTimeOffset? NotificationConsentWithdrawnAt = null,
    bool FollowedCompanyNotificationsEnabled = false,
    DateTimeOffset? FollowedCompanyNotificationConsentAt = null,
    DateTimeOffset? FollowedCompanyNotificationConsentWithdrawnAt = null);
