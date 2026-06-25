using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.JobSeekers.Commands.UpdateMyProfile;

// TD-115 (2026-06-25): the legacy EmailNotifications/WeeklySummary flags were retired
// (they gated no email path — see Preferences) so this command no longer carries them.
public sealed record UpdateMyProfileCommand(
    string? DisplayName,
    string? Language) : ICommand<Result>, IAuthenticatedRequest;
