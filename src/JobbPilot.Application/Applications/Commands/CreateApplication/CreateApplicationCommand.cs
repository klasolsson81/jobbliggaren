using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Applications.Commands.CreateApplication;

public sealed record CreateApplicationCommand(
    Guid? JobAdId,
    string? CoverLetter) : ICommand<Result<Guid>>, IAuthenticatedRequest;
