using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Resumes.Commands.CreateResume;

public sealed record CreateResumeCommand(
    string Name,
    string FullName) : ICommand<Result<Guid>>, IAuthenticatedRequest;
