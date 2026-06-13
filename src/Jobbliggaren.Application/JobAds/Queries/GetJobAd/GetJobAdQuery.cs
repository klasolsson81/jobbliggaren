using Mediator;

namespace Jobbliggaren.Application.JobAds.Queries.GetJobAd;

public sealed record GetJobAdQuery(Guid Id) : IQuery<JobAdDto?>;
