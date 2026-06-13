using Jobbliggaren.Application.Auth.Dtos;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Invitations.Commands.RedeemInvitation;

public sealed class RedeemInvitationCommandHandler(
    IAppDbContext db,
    IInvitationTokenGenerator tokenGenerator,
    IUserAccountService userAccountService,
    ISessionStore sessionStore,
    IAuthAuditLogger auditLogger,
    IDateTimeProvider clock)
    : ICommandHandler<RedeemInvitationCommand, Result<SessionDto>>
{
    public async ValueTask<Result<SessionDto>> Handle(
        RedeemInvitationCommand command, CancellationToken cancellationToken)
    {
        var tokenHash = tokenGenerator.Hash(command.Token!);

        var invitation = await db.Invitations
            .FirstOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);

        if (invitation is null)
            return Result.Failure<SessionDto>(
                DomainError.Validation("Invitation.NotFound", "Inbjudan kunde inte hittas."));

        // Email kommer från invitation — INTE från command body. Skyddar mot
        // token-stöld där angripare lurar offer klicka länk + tar över offers
        // konto med eget email.
        var createResult = await userAccountService.CreateUserAsync(
            invitation.Email, command.Password!, cancellationToken);

        if (createResult.IsFailure)
            return Result.Failure<SessionDto>(createResult.Error);

        var userId = createResult.Value;

        var redeemResult = invitation.Redeem(userId, clock);
        if (redeemResult.IsFailure)
        {
            await userAccountService.DeleteUserAsync(userId, cancellationToken);
            return Result.Failure<SessionDto>(redeemResult.Error);
        }

        var seekerResult = JobSeeker.Register(userId, command.DisplayName, clock);
        if (seekerResult.IsFailure)
        {
            await userAccountService.DeleteUserAsync(userId, cancellationToken);
            return Result.Failure<SessionDto>(seekerResult.Error);
        }

        db.JobSeekers.Add(seekerResult.Value);

        var session = await sessionStore.CreateAsync(userId, cancellationToken);
        auditLogger.LoginSucceeded(userId, session.Id.ToString());

        return Result.Success(new SessionDto(session.Id.Reveal()));
    }
}
