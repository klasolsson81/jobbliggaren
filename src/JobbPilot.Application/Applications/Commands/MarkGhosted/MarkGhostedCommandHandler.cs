using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.Applications.Commands.MarkGhosted;

public sealed class MarkGhostedCommandHandler(IAppDbContext db, IDateTimeProvider clock)
    : ICommandHandler<MarkGhostedCommand, Result>
{
    public async ValueTask<Result> Handle(
        MarkGhostedCommand command, CancellationToken cancellationToken)
    {
        var appId = new JobbPilot.Domain.Applications.ApplicationId(command.ApplicationId);
        var app = await db.Applications
            .FirstOrDefaultAsync(a => a.Id == appId, cancellationToken);

        if (app is null)
            return Result.Success(); // idempotent — inget att göra om aggregatet inte finns

        return app.MarkGhosted(clock);
    }
}
