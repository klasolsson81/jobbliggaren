using Jobbliggaren.Application.Auth.LoginChallenges;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Auth.LoginChallenges;

/// <summary>
/// The bounded in-process queue behind <see cref="ILoginChallengeDispatcher"/> (#1735, ADR 0142 D2): its own
/// instance with its own capacity and its own drop event, so a forgot-password flood cannot drop logins.
/// The code and the link are minted by the consumer, so no credential ever sits in the queue.
/// </summary>
internal sealed partial class LoginChallengeDispatchChannel
    : BoundedDispatchChannel<LoginChallengeDispatch>, ILoginChallengeDispatcher
{
    private readonly ILogger<LoginChallengeDispatchChannel> _logger;

    public LoginChallengeDispatchChannel(
        IOptions<LoginChallengeDispatchOptions> options,
        ILogger<LoginChallengeDispatchChannel> logger)
        : base(options.Value.Capacity)
    {
        _logger = logger;
    }

    // A write refused at shutdown and one dropped on a full queue are the same to the caller, who answers
    // the uniform 202 either way; the port returns nothing so it cannot branch.
    public void Enqueue(LoginChallengeDispatch dispatch) => _ = TryWrite(dispatch);

    protected override void OnItemDropped() => LogQueueFull(_logger, Capacity);

    // The capacity and nothing else: written before any lookup, byte-identical for any address.
    [LoggerMessage(1009, LogLevel.Warning,
        "Login-challenge dispatch queue is FULL (capacity {Capacity}) — request accepted with the uniform "
        + "202 but no email will be sent for it")]
    private static partial void LogQueueFull(ILogger logger, int capacity);
}
