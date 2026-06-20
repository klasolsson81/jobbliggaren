using Jobbliggaren.Application.Common.Abstractions;
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
/// (<c>Resume</c> + versions, <c>ParsedResume</c>), search artifacts
/// (<c>SavedJobAd</c>, <c>RecentJobSearch</c>) and resets
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
/// surface — the endpoint is mapped ONLY in Development.
/// </para>
///
/// Returns a non-generic <see cref="Result"/> (it creates no new id — it mutates
/// the caller's existing aggregates). Tolerant of not-found (no JobSeeker yet) —
/// returns Success so the dev can call it idempotently.
/// </summary>
public sealed record ResetMyDataCommand : ICommand<Result>, IAuthenticatedRequest;
