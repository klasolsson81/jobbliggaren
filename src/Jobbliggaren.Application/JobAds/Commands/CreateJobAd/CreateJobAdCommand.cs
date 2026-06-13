using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Commands.CreateJobAd;

public sealed record CreateJobAdCommand(
    string? Title,
    string? CompanyName,
    string? Description,
    string? Url,
    string? Source,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ExpiresAt) : ICommand<Result<Guid>>;
