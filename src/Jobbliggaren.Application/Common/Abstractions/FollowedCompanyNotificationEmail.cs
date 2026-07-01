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
/// </para>
/// </summary>
public sealed record FollowedCompanyNotificationEmail(
    DigestCadence Cadence,
    IReadOnlyList<FollowedCompanyAdItem> Items,
    int TotalCount);
