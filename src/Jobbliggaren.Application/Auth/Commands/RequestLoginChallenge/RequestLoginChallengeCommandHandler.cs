using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.RequestLoginChallenge;

/// <summary>
/// #1735 — the request path of the login challenge (ADR 0142 D2). It never reads the account: its gates and the
/// hand-off are <see cref="LoginChallengeAdmission"/>'s, which reaches nothing that could, and a test pins that
/// transitively, so its cost and its answer are the same for every well-formed address.
/// </summary>
public sealed class RequestLoginChallengeCommandHandler(LoginChallengeAdmission admission)
    : ICommandHandler<RequestLoginChallengeCommand, Result<ChallengeId>>
{
    public async ValueTask<Result<ChallengeId>> Handle(
        RequestLoginChallengeCommand command, CancellationToken cancellationToken) =>
        await admission.AdmitAsync(command.Email!, cancellationToken);
}
