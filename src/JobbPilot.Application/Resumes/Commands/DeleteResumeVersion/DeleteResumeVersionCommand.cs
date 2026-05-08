using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Resumes.Commands.DeleteResumeVersion;

public sealed record DeleteResumeVersionCommand(
    Guid ResumeId,
    Guid VersionId) : ICommand<Result>, IAuthenticatedRequest;
