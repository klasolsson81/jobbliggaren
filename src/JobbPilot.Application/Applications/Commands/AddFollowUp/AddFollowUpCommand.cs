using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Applications.Commands.AddFollowUp;

public sealed record AddFollowUpCommand(
    Guid ApplicationId,
    string Channel,
    DateTimeOffset ScheduledAt,
    string? Note) : ICommand<Result<Guid>>, IAuthenticatedRequest;
