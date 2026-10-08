using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.Commands.ConfirmEmailChange;
using Jobbliggaren.Application.Auth.Commands.DeleteAccount;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Common.Behaviors;

public sealed class AccountAccessMutationBehavior<TMessage, TResponse>(
    IEnumerable<IAccountAccessCoordinator> coordinators,
    IEnumerable<IAccountAccessReader> readers,
    IEnumerable<IAccountAccessCleanup> cleanup,
    ICurrentUser currentUser,
    IEnumerable<ConfirmedAddressSwap> addressSwaps) : IPipelineBehavior<TMessage, TResponse>
    where TMessage : IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        if (message is not IAccountAccessMutation mutation)
            return await next(message, cancellationToken);
        if (message is IReplayOnConcurrencyConflict)
            throw new InvalidOperationException("Account lifecycle mutations cannot be replayed automatically.");

        var actorId = currentUser.UserId ?? throw new ReauthenticationFailedException();
        var coordinator = coordinators.Single();
        TResponse response;
        try
        {
            await using (var scope = await coordinator.BeginAsync(
                [actorId, mutation.TargetUserId ?? actorId], true, cancellationToken))
            {
                if (!scope.OwnsCommit)
                    throw new InvalidOperationException("An account mutation must own the commit and its post-commit effects.");
                var actor = await readers.Single().ReadAsync(actorId, cancellationToken);
                if (actor is null || !actor.CanAuthenticate
                    || currentUser.AccessRevision != actor.AccessRevision
                    || (message is IAdminRequest && !actor.IsAdmin))
                    throw new ReauthenticationFailedException();

                response = await next(message, cancellationToken);
                if (response is not Result { IsFailure: true })
                    await scope.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            foreach (var swap in addressSwaps)
                swap.DiscardNotices();
            throw new ConcurrencyConflictException();
        }
        catch
        {
            foreach (var swap in addressSwaps)
                swap.DiscardNotices();
            throw;
        }
        if (response is Result<ConfirmedEmailChange> { IsSuccess: true } confirmed)
            confirmed.Value.Authorization.ConfirmCommit();
        foreach (var swap in addressSwaps)
        {
            if (response is Result { IsFailure: true })
                swap.DiscardNotices();
            else
                await swap.NotifyCommittedAsync(CancellationToken.None);
        }
        if (response is Result<AccountAccessChanged> { IsSuccess: true } result)
            await cleanup.Single().CompleteAsync(result.Value, CancellationToken.None);
        if (response is Result<AccountDeletionScheduled> { IsSuccess: true } deletion)
            await cleanup.Single().CompleteAsync(new AccountAccessChanged(
                deletion.Value.UserId, deletion.Value.IsSuspended, deletion.Value.AccessRevision, true), CancellationToken.None);
        return response;
    }
}
