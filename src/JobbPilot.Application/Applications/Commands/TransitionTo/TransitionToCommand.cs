using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Applications.Commands.TransitionTo;

public sealed record TransitionToCommand(
    Guid ApplicationId,
    string TargetStatus) : ICommand<Result>, IAuthenticatedRequest;
