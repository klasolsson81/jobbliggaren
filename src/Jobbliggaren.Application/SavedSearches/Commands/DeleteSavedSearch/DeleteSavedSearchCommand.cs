using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.SavedSearches.Commands.DeleteSavedSearch;

public sealed record DeleteSavedSearchCommand(Guid Id)
    : ICommand<Result>, IAuthenticatedRequest, IAuditableCommand<Result>
{
    public string EventType => "SavedSearch.Deleted";
    public string AggregateType => "SavedSearch";
    public Guid ExtractAggregateId(Result response) => Id;
}
