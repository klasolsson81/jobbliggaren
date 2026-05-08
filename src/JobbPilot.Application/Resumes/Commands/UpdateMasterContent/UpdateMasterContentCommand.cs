using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Resumes.Queries;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Resumes.Commands.UpdateMasterContent;

public sealed record UpdateMasterContentCommand(
    Guid ResumeId,
    ResumeContentDto Content) : ICommand<Result>, IAuthenticatedRequest;
