using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.ConsumeLoginLink;

/// <summary>
/// #1735 — the link arm of a login challenge. Every failure is one answer: a link is opened from a mail, by
/// whoever holds it, so which of malformed, unknown, used or expired it was is told to nobody.
/// </summary>
public sealed class ConsumeLoginLinkCommandHandler(ILoginChallengeStore store, LoginProofOutcome outcome)
    : ICommandHandler<ConsumeLoginLinkCommand, Result<LoginOutcome>>
{
    public async ValueTask<Result<LoginOutcome>> Handle(
        ConsumeLoginLinkCommand command, CancellationToken cancellationToken)
    {
        var proof = await store.ConsumeLinkAsync(LoginLinkToken.FromRaw(command.Token!), cancellationToken);

        return proof is null
            ? Result.Failure<LoginOutcome>(DomainError.Gone(
                AuthErrorCodes.LoginLinkUnusable, AuthErrorCodes.LoginLinkUnusableMessage))
            : Result.Success(await outcome.ResolveAsync(proof, LoginMethod.Link, cancellationToken));
    }
}
