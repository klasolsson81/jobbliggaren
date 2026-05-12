using JobbPilot.Application.Common;
using Mediator;

namespace JobbPilot.Application.JobAds.Queries.ListJobAds;

public sealed record ListJobAdsQuery(
    int Page = 1,
    int PageSize = 20,
    JobAdSortBy SortBy = JobAdSortBy.PublishedAtDesc) : IQuery<PagedResult<JobAdDto>>;
