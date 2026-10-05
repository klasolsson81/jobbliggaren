using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.CompleteAccountEmailChange;

/// <summary>
/// #1975 (ADR 0153) — an account's owner completes a change an administrator started, on the public page and with no
/// session, by presenting the account's current address, the new address and the code mailed to it.
/// <para>
/// Public, so not <c>IAuthenticatedRequest</c>; outside <c>Application.Admin</c>, whose messages all carry the admin
/// gate; and not <c>IAuditableCommand</c>, because its one audit row names the account as its user, which AuditBehavior
/// cannot do on an anonymous route. The handler writes that row itself.
/// </para>
/// </summary>
public sealed record CompleteAccountEmailChangeCommand(string? CurrentEmail, string? NewEmail, string? Code)
    : ICommand<Result<AccountEmailChangeOutcome>>
{
    public override string ToString() => "CompleteAccountEmailChangeCommand(addresses and code redacted)";
}

/// <summary>What a presentation that matched in full did. Every other presentation is one refusal.</summary>
public abstract record AccountEmailChangeOutcome
{
    private AccountEmailChangeOutcome()
    {
    }

    /// <summary>
    /// The account moved. <see cref="AuditRecorded"/> is false when the row could not be written after the swap
    /// committed, which the endpoint answers as a failure once its teardown has run.
    /// </summary>
    public sealed record Completed(Guid UserId, bool AuditRecorded) : AccountEmailChangeOutcome;

    /// <summary>The code and the current address matched before the delay had run. Nothing was spent.</summary>
    public sealed record NotYet(DateTimeOffset CompletableFrom) : AccountEmailChangeOutcome;
}
