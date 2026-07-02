using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.CompanyWatches.Commands.MarkFollowedCompanyAdSeen;

/// <summary>
/// #453 (cross-channel dedup; ADR 0087 D5-addendum; senior-cto-advisor 2026-07-02) — records that the
/// authenticated user OPENED this ad in-app, so the company-follow notification digest suppresses the
/// redundant email for it ("aldrig mejla något jag sett i appen"). Marks every still-Pending
/// <c>FollowedCompanyAdHit</c> for <c>(currentUser, JobAdId)</c> seen (a Pending-only transition — a
/// post-claim/Queued/Sent hit is a deliberate no-op; the Sent-dedup already prevents repetition).
///
/// <para>
/// <b>Owner-scoped, IDOR-safe (CLAUDE.md §5/§12):</b> the UserId is resolved in the handler from
/// <c>ICurrentUser</c> — it is NEVER a command/wire parameter, so a user can only stamp their OWN hits.
/// The non-PII <see cref="JobAdId"/> travels on the wire (it is already in the <c>/jobb/{id}</c> URL);
/// the raw org.nr never appears here (this touches a timestamp only — ADR 0087 D8(c)).
/// </para>
///
/// <para>
/// <b>Not audited (parity <c>MarkMatchesSeenCommand</c>):</b> a "seen" signal is high-frequency
/// behavioural data with no security-relevant state transition — auditing every ad-open would flood the
/// trail. Returns a non-generic <see cref="Result"/> (it mutates existing rows; creates no id) and is a
/// benign no-op (Success) when the user follows nothing for this ad — never a NotFound (that would leak
/// hit existence).
/// </para>
/// </summary>
public sealed record MarkFollowedCompanyAdSeenCommand(Guid JobAdId) : ICommand<Result>;
