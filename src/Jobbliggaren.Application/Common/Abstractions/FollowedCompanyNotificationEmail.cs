using Jobbliggaren.Domain.JobSeekers;

namespace Jobbliggaren.Application.Common.Abstractions;

/// <summary>
/// ONE new ad in a company-follow notification email (ADR 0087 D5, #311 PR-4). ONLY non-PII,
/// named PUBLIC ad fields: job title + company name (both public Platsbanken data on
/// <c>job_ads</c>). Deliberately carries NO grade LABEL — a company-follow hit is NOT scored
/// (it is "a new ad appeared at an employer you follow", not a skill match), so the grade concept
/// (and the Goodhart guard around it) does not apply. No CV content, no org.nr, no recipient
/// address.
/// </summary>
public sealed record FollowedCompanyAdItem(
    string JobTitle,
    string CompanyName);

/// <summary>
/// The content contract for a company-follow notification email (ADR 0087 D5, #311 PR-4). A NEW,
/// SEPARATE contract from <see cref="MatchNotificationEmail"/> (senior-cto-advisor D1, 2026-07-01):
/// that record's <see cref="MatchNotificationItem.GradeLabel"/> is a required, sealed, grade-only
/// field, and a company-follow hit has no grade — reusing it would nullable-ise a deliberately
/// sealed field and add a Kind-branch (the "corrupt a sealed type to save a table" pattern ADR 0087
/// D5 already rejected for the <c>UserJobAdMatch</c> reuse). Two content contracts = two honest
/// change-reasons (SRP).
/// <para>
/// <b>Non-PII by construction:</b> <see cref="IEmailSender.SendFollowedCompanyNotificationEmailAsync"/>
/// takes the recipient address as a SEPARATE argument — this type NEVER carries it (or any other
/// PII). <see cref="Items"/> are public ad fields only; the settings/unsubscribe link is built
/// template-side from <c>EmailOptions.BaseUrl</c>. In particular this contract carries NO org.nr
/// (ADR 0087 D8 — the personnummer-shaped-org.nr guard lives at the surfacing boundary; the follow
/// email surfaces the public company NAME, never the org.nr).
/// </para>
/// <para>
/// A company-follow notification is always a DIGEST (there is no direct/Top concept for follows), so
/// this contract has no Kind discriminator. <see cref="Cadence"/> drives the "daglig/veckovis"
/// phrasing; <see cref="TotalCount"/> is the honest window total (≥ <see cref="Items"/>.Count when
/// the display is capped) so the template can render "och N till".
/// <para>
/// <b><see cref="FilterSummary"/> (bevakning-reconcile RF-13=13B, 2026-07-12):</b> an OPTIONAL
/// per-email disclosure of the per-watch filters that shaped this digest (null = no filter → no
/// disclosure). Email-level, never per-item, so the D1 seal holds. F3 populates it; the Swedish
/// disclosure copy is rendered by PR-F4.
/// </para>
/// </summary>
public sealed record FollowedCompanyNotificationEmail(
    DigestCadence Cadence,
    IReadOnlyList<FollowedCompanyAdItem> Items,
    int TotalCount,
    FollowedCompanyFilterSummary? FilterSummary = null);

/// <summary>
/// Bevakning-reconcile RF-13=13B (2026-07-12) — a disclosure that the follow digest may be narrowed,
/// so the copy can say so honestly (§5 transparency) WITHOUT breaking the D1 seal (per-item stays
/// grade-free — this is email-level, not per-item).
///
/// <para>
/// <b>Booleans only, aggregated across ALL of the user's ACTIVE watch filters</b> (CTO sub-bind A′,
/// 2026-07-12 — NOT just the watches that contributed a hit to this email).
/// <see cref="OnlyMatchedActive"/> = at least one active watch filters to "endast matchade annonser"
/// (read-time ≥Good, applied at dispatch, and only when the user is assessable — a profile-less filter
/// is INERT); <see cref="LocationFilterActive"/> = at least one active watch filters by municipality or
/// län (applied SCAN-time, 8A).
/// </para>
///
/// <para>
/// The quantifier's domain is the user's SETTINGS, not this email's hit set, and that is load-bearing:
/// a watch whose filter suppressed 100% of that company's new ads contributes ZERO hits, so a
/// contributing-watches-only summary would stay silent about a real narrowing — the silent narrowing
/// RF-13 rejected, reached by another route. It also matches what the rendered sentence actually says
/// ("ett eller flera av företagen du följer"). Consequently the summary being null is itself a TRUE
/// claim: none of the companies you follow is filtered.
/// </para>
///
/// <para>
/// Carries NO ort NAMES and NO grade value (data-minimizing; Goodhart-safe). The Swedish disclosure
/// copy is rendered by <c>EmailTemplates.FollowedCompanyNotification</c> (F4a).
/// </para>
/// </summary>
public sealed record FollowedCompanyFilterSummary(
    bool OnlyMatchedActive,
    bool LocationFilterActive);
