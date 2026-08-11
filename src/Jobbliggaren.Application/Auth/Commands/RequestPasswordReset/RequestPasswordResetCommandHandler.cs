using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Application.Auth.Commands.RequestPasswordReset;

public sealed class RequestPasswordResetCommandHandler(
    IEmailSender emailSender,
    ICooldownGate cooldown,
    IOptions<AuthEmailCooldownOptions> cooldownOptions,
    IPasswordResetDispatcher dispatchQueue,
    IRequestContextProvider requestContext)
    : ICommandHandler<RequestPasswordResetCommand, Result>
{
    private readonly TimeSpan _window =
        TimeSpan.FromSeconds(cooldownOptions.Value.PasswordResetWindowSeconds);

    public async ValueTask<Result> Handle(
        RequestPasswordResetCommand command, CancellationToken cancellationToken)
    {
        // ── Three steps, and NONE of them may read the account. That is the contract. ──
        //
        // This surface is unauthenticated and answers a uniform 202 for known and unknown addresses
        // alike, so any work whose COST depends on whether the address resolves is a timing oracle. The
        // lookup, the token mint and the provider round trip therefore all live behind
        // IPasswordResetDispatcher. What remains here is a capability read, a hash-keyed cooldown
        // check, and a non-blocking channel write — none of which looks at the address.

        // 1. CAPABILITY, and it MUST be the first statement.
        //
        // #1171 is delivery-dependent in the strictest sense on this port: the password changes only
        // when the emailed link is opened, so a dropped send leaves someone who has already lost access
        // with a "check your inbox" message and no link. NullEmailSender is the live default outside
        // Development/Test, so this is the ordinary configuration, not an edge case.
        //
        // Why FIRST rather than merely present. Checked here the gate reads NO input, so the 503/202
        // partition is a property of the server's configuration and can carry no information about any
        // account. Checked after a lookup it would be reachable only when an account exists, and the 503
        // would become precisely the existence oracle the uniform 202 exists to prevent — which is why
        // ResendEmailConfirmationCommandHandler, whose check sits after its lookup, must never return
        // 503 at all and settles for suppressing its audit line. Same property, and the same wording, as
        // AuthErrorCodes.RegistrationsClosed: the gate never reads the submitted address, so the
        // response cannot vary with it.
        if (!emailSender.CanDeliver)
            return Result.Failure(DomainError.Validation(
                AuthErrorCodes.EmailDeliveryUnavailable,
                AuthErrorCodes.EmailDeliveryUnavailableMessage));

        // 2. COOLDOWN — check-and-set uniformly for every non-cooled well-formed request. A cooled
        // repeat returns the SAME uniform success, never a 409 or 429: a visible throttle on an
        // unauthenticated surface would answer differently for an address someone had recently
        // requested, which is an enumeration oracle assembled out of the anti-abuse control.
        //
        // It sits AFTER the capability check because a static server refusal must not burn the
        // requester's window for a request the server could never have fulfilled — the same relative
        // order, for the same reason, as ChangeEmailCommandHandler's. The gate hashes a subject
        // normalised the way Identity's lookup normalises it, so two spellings of one account share one
        // window (RedisCooldownGate.Key).
        //
        // Its purpose here is anti-email-bomb and nothing else. It does NOT rate-cap a timing channel —
        // a per-address window limits repeated sampling of one address, while enumeration needs exactly
        // one measurement per candidate. That claim was made and is withdrawn (security-auditor
        // 2026-08-10); the channel is closed by step 3's shape instead.
        if (!await cooldown.TryBeginAsync(
                CooldownScopes.PasswordReset, command.Email!, _window, cancellationToken))
            return Result.Success();

        // 3. HAND OFF, and this is what actually closes the oracle. The call costs the same whether or
        // not the address resolves, because nothing on this side ever looks at it. TryEnqueue is
        // synchronous and non-blocking by contract — an awaiting or blocking enqueue would put a
        // load-dependent delay back on the endpoint, which is the same defect one step sideways.
        //
        // A refused enqueue (full queue) still answers the uniform 202: the response may not vary with
        // server load any more than with account existence. The drop is logged server-side by the queue,
        // before any lookup, so that line carries no existence information either.
        //
        // The client context is captured HERE because the consumer runs outside a request scope and
        // AuthAuditLogger's HttpContext read would return nothing there — letting the audit line
        // degrade to "unknown" on the auth event most tied to account takeover would silently drop the
        // defence-in-depth ADR 0024 D7 ratified. Both values are anonymised/truncated by the provider.
        dispatchQueue.TryEnqueue(new PasswordResetDispatch(
            command.Email!,
            requestContext.IpAddress,
            requestContext.UserAgent));

        return Result.Success();
    }
}
