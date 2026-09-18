using Jobbliggaren.Application.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// #1171 — the bounded in-process channel behind <see cref="IPasswordResetDispatcher"/>.
/// <para>
/// <b>Why a channel and not Hangfire</b> (senior-cto-advisor 2026-08-10). A Hangfire job would have to
/// carry the minted reset token in its arguments, because the Hangfire SERVER runs only in the Worker
/// and the Worker registers no token providers — so it cannot mint. That would persist a bearer
/// credential granting account takeover into <c>hangfire.job</c>, in the same database. Today a
/// read-only database compromise yields PBKDF2 hashes and DEK-enveloped PII, i.e. no live credential;
/// with the token in a job row it would yield an on-demand takeover of ANY account, because the
/// attacker can request the reset themselves. Every existing Hangfire wrapper in this repo serialises
/// only a <c>CancellationToken</c> or a <c>bool</c> and re-reads recipient data inside the job — this
/// would have been the first to break that. Keeping the mint in-process keeps the token in memory.
/// </para>
/// <para>
/// <b>One consequence of dropping rather than blocking, named so it is not rediscovered</b>
/// (security-auditor 2026-08-10): a saturated queue drops OTHER people's legitimate resets, not only
/// the flood that saturated it. That is new against the inline version, which had no queue to fill.
/// It is bounded by the per-address cooldown (an attacker needs many DISTINCT addresses) and by the
/// per-IP rate limit, and the only signal is the Warning below, which nothing alerts on (#1175 owns
/// the log sink). A second consumer would raise the drain rate if that trade stops being acceptable.
/// </para>
/// <para>
/// The cost accepted: work is lost if the process restarts, and there is no retry. Delivery
/// here is best-effort by design either way — the inline version already swallowed every send failure
/// into the uniform 202 for anti-enumeration reasons — and a reset link dies after
/// <c>PasswordResetTokenProviderOptions.LifespanMinutes</c> anyway, with the user's remedy being one
/// click on a form that is already throttled.
/// </para>
/// </summary>
internal sealed partial class PasswordResetDispatchChannel
    : BoundedDispatchChannel<PasswordResetDispatch>, IPasswordResetDispatcher
{
    private readonly ILogger<PasswordResetDispatchChannel> _logger;

    public PasswordResetDispatchChannel(
        IOptions<PasswordResetDispatchOptions> options,
        ILogger<PasswordResetDispatchChannel> logger)
        : base(options.Value.Capacity)
    {
        _logger = logger;
    }

    public bool TryEnqueue(PasswordResetDispatch dispatch)
    {
        // The bool means "the channel accepted the write", which under DropWrite is false ONLY once the
        // writer has been completed, i.e. during shutdown. A FULL queue returns true and silently
        // discards the item; that case is reported by OnItemDropped. The caller answers the uniform 202
        // in either case, so it does not branch on this — the value exists so a shutdown-time enqueue is
        // not mistaken for a queued one.
        return TryWrite(dispatch);
    }

    protected override void OnItemDropped() => LogQueueFull(_logger, Capacity);

    /// <summary>
    /// A drop is NOT silent, which is the whole answer to "then the user gets nothing and no error".
    /// <para>
    /// The line carries the capacity and nothing else: no address, no user id — the request path does
    /// not even know a user id at this point, and the dropped item is deliberately not read here. It is
    /// therefore written BEFORE any lookup and is byte-identical for an existing and a non-existent
    /// account, so it leaks nothing while still telling an operator the queue is saturated. Warning,
    /// because a saturated queue on this endpoint is either an incident or an attack, and Debug is
    /// filtered out where it matters.
    /// </para>
    /// </summary>
    [LoggerMessage(1007, LogLevel.Warning,
        "Password-reset dispatch queue is FULL (capacity {Capacity}) — request accepted with the uniform "
        + "202 but no email will be sent for it")]
    private static partial void LogQueueFull(ILogger logger, int capacity);
}
