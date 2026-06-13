namespace Jobbliggaren.Application.JobAds.Queries;

public sealed record JobAdDto(
    Guid Id,
    string Title,
    string CompanyName,
    string Description,
    string Url,
    string Source,
    string Status,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    // ADR 0042 Beslut E — "Ny"-badge. Runtime-presentationskontext (ej i
    // SearchCriteria — analogt Page/PageSize). true om PublishedAt >= Since
    // (ListJobAdsQuery.Since); false när Since ej angivet / RunSavedSearch.
    bool IsNew
);
