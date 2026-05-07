using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Applications.Commands.MarkGhosted;

public sealed record MarkGhostedCommand(Guid ApplicationId) : ICommand<Result>;
