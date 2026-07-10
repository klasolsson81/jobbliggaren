using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Commands.MarkJobsSeen;

/// <summary>
/// #293 (ADR 0042 Beslut E amendment) — advances the authenticated user's last-seen-jobs
/// watermark. Called when the user loads the /jobb list (the sibling of
/// <see cref="Jobbliggaren.Application.Matching.Commands.MarkMatchesSeen.MarkMatchesSeenCommand"/>
/// for the matches surface). The next /jobb visit's "Ny" tag then flags only ads ingested
/// after the viewed window (NY = JobAd.CreatedAt &gt; LastSeenJobsAt). Owner-scoped.
/// Returns a non-generic <see cref="Result"/> (it mutates the caller's existing JobSeeker;
/// creates no id). Idempotent (the watermark is monotonic).
///
/// <para>Deliberately NOT <c>IAuditableCommand</c> (parity MarkMatchesSeen): a behavioural
/// "I viewed the job list" timestamp advanced on every page load is not an auditable
/// owner-action, and auditing each load would flood the audit trail (GDPR data-minimisation).</para>
/// </summary>
/// <param name="SeenThrough">
/// The max <c>CreatedAt</c> of the ads the user actually rendered on the loaded page (#759, the
/// sibling of #477 Low 4 — the watermark is set to this, NOT clock-now, so an ad ingested between
/// the fetch and this call is not silently swallowed). Unlike the nyast-först matches list, /jobb
/// may be relevance/match-rank sorted, so the FE sends <c>max(createdAt)</c> over the page, not
/// <c>list[0]</c>. Null (no body / an empty list / deploy-skew from an older FE) falls back to
/// clock-now in the handler — the old behaviour, safe when there is nothing newer to preserve.
/// </param>
public sealed record MarkJobsSeenCommand(DateTimeOffset? SeenThrough) : ICommand<Result>;
