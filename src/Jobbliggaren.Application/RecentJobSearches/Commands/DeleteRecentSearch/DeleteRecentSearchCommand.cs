using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.RecentJobSearches.Commands.DeleteRecentSearch;

public sealed record DeleteRecentSearchCommand(Guid Id)
    : ICommand<Result>, IAuthenticatedRequest, IAuditableCommand<Result>
{
    public string EventType => "RecentJobSearch.Deleted";
    public string AggregateType => "RecentJobSearch";
    public Guid ExtractAggregateId(Result response) => Id;
}
