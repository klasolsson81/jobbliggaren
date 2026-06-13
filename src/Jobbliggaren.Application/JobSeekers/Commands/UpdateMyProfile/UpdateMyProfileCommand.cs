using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.JobSeekers.Commands.UpdateMyProfile;

public sealed record UpdateMyProfileCommand(
    string? DisplayName,
    string? Language,
    bool? EmailNotifications,
    bool? WeeklySummary) : ICommand<Result>, IAuthenticatedRequest;
