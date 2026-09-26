using Jobbliggaren.Application.Auth.LoginChallenges;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Infrastructure.Auth.LoginChallenges;

/// <summary>
/// The consumer for <see cref="LoginChallengeDispatchChannel"/>. The drain is the shared base's; the decision
/// is <see cref="LoginChallengeIssuer"/>'s. Api composition only: the store protects with the Api's
/// Data-Protection keyring and runs on the Api's volatile Redis connection, neither of which the Worker has
/// (ADR 0023, ADR 0142 D2).
/// </summary>
internal sealed partial class LoginChallengeDispatchService(
    LoginChallengeDispatchChannel queue,
    IServiceScopeFactory scopeFactory,
    ILogger<LoginChallengeDispatchService> logger)
    : BoundedDispatchService<LoginChallengeDispatch>(queue, scopeFactory)
{
    protected override Task HandleAsync(
        LoginChallengeDispatch dispatch, IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<LoginChallengeIssuer>().IssueAsync(dispatch, ct);

    protected override void OnDispatchFailed(string errorType) => LogDispatchFailed(logger, errorType);

    [LoggerMessage(1010, LogLevel.Warning,
        "Login-challenge dispatch failed ({ErrorType}) — no challenge written or no email sent; the "
        + "requester already received the uniform 202 and cannot be told")]
    private static partial void LogDispatchFailed(ILogger logger, string errorType);
}
