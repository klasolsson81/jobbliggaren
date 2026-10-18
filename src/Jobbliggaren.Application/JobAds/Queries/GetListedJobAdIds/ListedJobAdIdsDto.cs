namespace Jobbliggaren.Application.JobAds.Queries.GetListedJobAdIds;

/// <summary>One sitemap part on the wire: <c>{"ids": [...]}</c>, lower-case GUIDs in id order, and nothing else (ADR 0174).</summary>
public sealed record ListedJobAdIdsDto(IReadOnlyList<Guid> Ids);
