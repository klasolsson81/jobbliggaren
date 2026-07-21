namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #1013 — a dedicated collection binding the single-owner <see cref="JobAdBrowsePlanFixture"/> so the
/// browse-sort plan-CHOICE guard owns its Postgres statistics (see the fixture docblock). Separate from
/// <c>[Collection("Api")]</c> on purpose: this test must NOT share the accumulating, never-truncated
/// job_ads table of the 171-class Api collection.
/// </summary>
[CollectionDefinition("JobAdBrowsePlan")]
public sealed class JobAdBrowsePlanFixtureGroup : ICollectionFixture<JobAdBrowsePlanFixture>;
