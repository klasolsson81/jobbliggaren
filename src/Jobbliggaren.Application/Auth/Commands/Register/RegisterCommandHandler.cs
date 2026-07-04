using Jobbliggaren.Application.Auth.Dtos;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.Register;

public sealed class RegisterCommandHandler(
    IAppDbContext db,
    IUserAccountService userAccountService,
    ISessionStore sessionStore,
    IAuthAuditLogger auditLogger,
    IDateTimeProvider clock)
    : ICommandHandler<RegisterCommand, Result<SessionDto>>
{
    public async ValueTask<Result<SessionDto>> Handle(
        RegisterCommand command, CancellationToken cancellationToken)
    {
        var createResult = await userAccountService.CreateUserAsync(
            command.Email!, command.Password!, cancellationToken);

        if (createResult.IsFailure)
            return Result.Failure<SessionDto>(createResult.Error);

        var userId = createResult.Value;

        var seekerResult = JobSeeker.Register(userId, command.DisplayName, clock);
        if (seekerResult.IsFailure)
        {
            await userAccountService.DeleteUserAsync(userId, cancellationToken);
            return Result.Failure<SessionDto>(seekerResult.Error);
        }

        db.JobSeekers.Add(seekerResult.Value);

        // Legacy profile = today's reach; the opt-in persistent choice ships in 2b.
        var session = await sessionStore.CreateAsync(userId, SessionLifetime.Legacy, cancellationToken);

        auditLogger.LoginSucceeded(userId, session.Id.ToString());

        return Result.Success(new SessionDto(session.Id.Reveal()));
    }
}
