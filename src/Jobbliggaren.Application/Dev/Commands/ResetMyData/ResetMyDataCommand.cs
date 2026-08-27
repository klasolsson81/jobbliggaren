using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Dev.Commands.ResetMyData;

/// <summary>
/// DEV-ONLY throwaway tool — REMOVE BEFORE LAUNCH (Klas). Lets the current user
/// re-test the onboarding flow from scratch (welcome modal, empty /cv, fill it in
/// themselves) WITHOUT deleting the account or losing the login.
///
/// <para>
/// Owner-scoped: clears ONLY the current authenticated user's CV data
/// (<c>Resume</c> + versions, <c>ParsedResume</c>, the uploaded <c>ResumeFile</c>
/// originals), search artifacts (<c>SavedJobAd</c>, <c>RecentJobSearch</c>), the
/// graded <c>UserJobAdMatch</c> rows, <c>PrimaryResumeId</c>, and resets
/// <c>MatchPreferences</c> to <see cref="Jobbliggaren.Domain.JobSeekers.MatchPreferences.Empty"/>
/// (so <c>hasStatedDesiredOccupation</c> becomes false → the welcome modal
/// re-triggers). Deliberately does NOT touch Applications, the account
/// (<c>JobSeeker</c> itself), nor the user's DEKs (the master key is unchanged —
/// keeping <c>user_data_keys</c> lets a fresh CV upload reuse the valid DEK).
/// </para>
///
/// <para>
/// Distinct from <c>DeleteAccountCommand</c> (GDPR Art. 17, soft-deletes the whole
/// ownership tree incl. the account). This is a dev convenience, never a product
/// surface — the endpoint is mapped only where <c>DevTools:EnableResetMyData</c> is
/// explicitly true, and the handler refuses again on the same flag.
/// </para>
///
/// <para>
/// <b>Audited</b> (<c>IAuditableCommand</c>, Art. 5(2)). It is destructive, it reaches
/// PII, and since it became reachable outside Development an unrecorded invocation is
/// an accountability gap rather than a dev convenience. Every comparable command in the
/// codebase carries the marker — <c>DeleteAccountCommand</c>, <c>DeleteResumeCommand</c>,
/// <c>DiscardParsedResumeCommand</c>, <c>UnsaveJobAdCommand</c> — and this one performs
/// the union of them in bulk. <c>AuditBehavior</c> is marker-driven and skips failures,
/// so the two refusal branches write nothing.
/// </para>
///
/// Returns the owning <c>JobSeeker</c>'s id so the audit row can name the aggregate.
/// Tolerant of not-found (no JobSeeker yet) — returns Success carrying
/// <see cref="System.Guid.Empty"/>, so the dev can call it idempotently and the audit
/// row still records that the reset ran against nothing.
/// </summary>
public sealed record ResetMyDataCommand
    : ICommand<Result<Guid>>, IAuthenticatedRequest, IAuditableCommand<Result<Guid>>
{
    public string EventType => "Dev.DataReset";
    public string AggregateType => "JobSeeker";
    public Guid ExtractAggregateId(Result<Guid> response) => response.Value;
}
