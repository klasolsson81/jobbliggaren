using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Commands.MarkJobsSeen;

/// <summary>
/// #293 (ADR 0042 Beslut E amendment) — advances the authenticated user's last-seen-jobs
/// watermark to now. Called when the user loads the /jobb list (the sibling of
/// <see cref="Jobbliggaren.Application.Matching.Commands.MarkMatchesSeen.MarkMatchesSeenCommand"/>
/// for the matches surface). The next /jobb visit's "Ny" tag then flags only ads ingested
/// after this moment (NY = JobAd.CreatedAt &gt; LastSeenJobsAt). Parameterless — owner-scoped.
/// Returns a non-generic <see cref="Result"/> (it mutates the caller's existing JobSeeker;
/// creates no id). Idempotent (the watermark is monotonic).
///
/// <para>Deliberately NOT <c>IAuditableCommand</c> (parity MarkMatchesSeen): a behavioural
/// "I viewed the job list" timestamp advanced on every page load is not an auditable
/// owner-action, and auditing each load would flood the audit trail (GDPR data-minimisation).</para>
/// </summary>
public sealed record MarkJobsSeenCommand : ICommand<Result>;
