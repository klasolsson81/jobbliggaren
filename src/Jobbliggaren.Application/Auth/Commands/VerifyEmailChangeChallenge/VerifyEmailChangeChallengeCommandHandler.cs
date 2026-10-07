using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.AccountEmailChanges;
using Jobbliggaren.Application.Auth.Grants;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.VerifyEmailChangeChallenge;

/// <summary>
/// #1739 — the code arm of a change-email challenge (ADR 0142 D5). The store asserts the binding the handler passes —
/// the change-email purpose, the session's user — so a login challenge, a re-authentication challenge or another
/// user's is answered as missing. The grant carries the address the record was written for, which the request step
/// took from its validated command.
/// </summary>
public sealed class VerifyEmailChangeChallengeCommandHandler(
    ICurrentUser currentUser,
    ILoginChallengeStore store,
    IGrantStore grants,
    IAccountAccessReader access,
    IAccountAccessCoordinator coordinator,
    IAccountEmailChangeRequests requests,
    IDateTimeProvider clock)
    : ICommandHandler<VerifyEmailChangeChallengeCommand, Result<GrantToken>>
{
    public async ValueTask<Result<GrantToken>> Handle(
        VerifyEmailChangeChallengeCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return Result.Failure<GrantToken>(
                DomainError.Validation(AuthErrorCodes.NotAuthenticated, "Inloggning krävs för att bekräfta koden."));

        var userId = currentUser.UserId.Value;
        if (coordinator.HasActiveScope)
            throw new InvalidOperationException("Address-code verification must own its protected transaction.");
        await using var scope = await coordinator.BeginAsync([userId], false, cancellationToken);
        if (!scope.OwnsCommit)
            throw new InvalidOperationException("Address-code verification cannot borrow a commit.");
        var id = ChallengeId.FromRaw(command.ChallengeId!);
        var pending = await store.ReadEmailChangeRequestAsync(id, userId, cancellationToken);
        if (pending is null)
            return Result.Failure<GrantToken>(ChallengeVerdictErrors.For(ChallengeVerdict.Missing));
        var current = await access.ReadAsync(userId, cancellationToken);
        if (pending.EmailChangeRequest is not { IsValid: true } request
            || request.ExpiresAt <= clock.UtcNow || current is null
            || !pending.Access.Admits(current) || currentUser.AccessRevision != current.AccessRevision)
            return Unusable();
        if (!await requests.HasCommittedSelfRequestAsync(userId, request, cancellationToken))
            return Result.Failure<GrantToken>(DomainError.Conflict(
                AuthErrorCodes.EmailChangeNotActivated, AuthErrorCodes.EmailChangeNotActivatedMessage));

        var verdict = await store.ConsumeBoundCodeAsync(
            id,
            LoginCode.FromRaw(command.Code!),
            new ChallengeBinding(ChallengePurpose.ChangeEmail, userId),
            cancellationToken);

        if (!verdict.IsVerified)
            return Result.Failure<GrantToken>(ChallengeVerdictErrors.For(verdict));

        var account = await access.ReadAsync(userId, cancellationToken);
        if (account is null || !verdict.Proof.Access.Admits(account)
            || currentUser.AccessRevision != account.AccessRevision || verdict.Proof.EmailChangeRequest != request)
            return Unusable();

        var grant = await grants.IssueAsync(new GrantSubject.ChangeEmail(userId, verdict.Proof.ProvenEmail)
        { Access = verdict.Proof.Access, Request = request }, cancellationToken);
        await scope.CommitAsync(cancellationToken);
        return Result.Success(grant);
    }

    private static Result<GrantToken> Unusable() => Result.Failure<GrantToken>(DomainError.Gone(
        AuthErrorCodes.EmailChangeGrantUnusable, AuthErrorCodes.EmailChangeGrantUnusableMessage));
}
