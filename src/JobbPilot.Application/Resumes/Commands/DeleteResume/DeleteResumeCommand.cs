using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Resumes.Commands.DeleteResume;

public sealed record DeleteResumeCommand(Guid ResumeId)
    : ICommand<Result>, IAuthenticatedRequest;
