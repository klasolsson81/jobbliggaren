using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.ChangeEmail;

/// <summary>
/// Self-service change-email — REQUEST step (#679, C5-email of epik #481; two codes since #1739, ADR 0142 D5).
/// The re-authentication credential is a purpose-scoped grant: <see cref="IReauthenticatingRequest"/> makes
/// <c>ReauthenticationBehavior</c> redeem it server-side (oracle-safe 401) BEFORE the handler runs, so a hijacked
/// long-lived session cannot repoint the account-recovery vector on its own. The architecture tripwire
/// (<c>ReauthenticationTripwireTests</c>) forces this marker + a validator on any <c>ChangeEmail…</c> command.
///
/// <para>
/// This step does NOT change the email and does NOT touch sessions: it writes a challenge bound to the user and
/// the change-email purpose, addressed to the NEW address, and mails that address a code. The swap happens only
/// once the code is verified (<c>VerifyEmailChangeChallengeCommand</c>) and the grant it yields is confirmed
/// (<c>ConfirmEmailChangeCommand</c>). Returns the user id for <c>AuditBehavior</c>'s
/// <c>User.EmailChangeRequested</c> row, and the challenge id for the caller. Neither the grant nor the new email
/// ever reaches a log or audit projection.
/// </para>
/// </summary>
public sealed record ChangeEmailCommand(string? ReauthGrant, string? NewEmail)
    : ICommand<Result<EmailChangeChallenge>>, IAuthenticatedRequest, IReauthenticatingRequest,
        IAuditableCommand<Result<EmailChangeChallenge>>
{
    public string EventType => "User.EmailChangeRequested";
    public string AggregateType => "User";
    public Guid ExtractAggregateId(Result<EmailChangeChallenge> response) => response.Value.UserId;
}

/// <summary>What a change-email request answers: whose request it was, and the challenge its code belongs to.</summary>
public sealed record EmailChangeChallenge(Guid UserId, ChallengeId ChallengeId);
