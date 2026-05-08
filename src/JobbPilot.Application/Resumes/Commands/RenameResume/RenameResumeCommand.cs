using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Resumes.Commands.RenameResume;

public sealed record RenameResumeCommand(
    Guid ResumeId,
    string Name) : ICommand<Result>, IAuthenticatedRequest;
