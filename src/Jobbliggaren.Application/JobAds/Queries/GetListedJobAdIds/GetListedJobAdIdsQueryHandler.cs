using Jobbliggaren.Application.JobAds.Listing;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetListedJobAdIds;

/// <summary>
/// One sitemap part from the listed-ad snapshot (ADR 0174). It does no I/O: the age is judged against the clock now,
/// on the same snapshot the ids are taken from, and a snapshot outside its bound is not served.
/// </summary>
public sealed class GetListedJobAdIdsQueryHandler(IListedJobAdSnapshotReader reader, IDateTimeProvider clock)
    : IQueryHandler<GetListedJobAdIdsQuery, ListedJobAdPart>
{
    public ValueTask<ListedJobAdPart> Handle(GetListedJobAdIdsQuery query, CancellationToken cancellationToken)
    {
        var snapshot = reader.Current;
        ListedJobAdPart answer = snapshot is not null && snapshot.IsFreshAt(clock.UtcNow)
            ? new ListedJobAdPart.Available(snapshot.IdsIn(query.Part))
            : new ListedJobAdPart.Unavailable();
        return ValueTask.FromResult(answer);
    }
}
