using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Applications.Commands.AddNote;

public sealed record AddNoteCommand(
    Guid ApplicationId,
    string? Content) : ICommand<Result<Guid>>, IAuthenticatedRequest;
