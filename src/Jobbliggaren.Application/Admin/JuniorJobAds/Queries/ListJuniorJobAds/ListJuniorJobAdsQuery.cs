using System.Text.Json.Serialization;
using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Mediator;

namespace Jobbliggaren.Application.Admin.JuniorJobAds.Queries.ListJuniorJobAds;

/// <summary>#2264 — the admin queue's three tabs.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum JuniorQueueView
{
    /// <summary>Listed ads the rules left unclear and nobody has decided: the queue.</summary>
    Unclear = 1,

    /// <summary>Listed ads that are on <c>/junior</c> now, by a rule or by a decision — where a wrong one is vetoed.</summary>
    Shown = 2,

    /// <summary>Listed ads an admin has decided, either way — where a decision is changed or handed back.</summary>
    Decided = 3,
}

/// <summary>
/// #2264 (ADR 0187) — one page of the admin junior queue. Every tab reads listed ads only
/// (<c>JobAdPublication.IsListed(now)</c>) in an enabled occupation field, so a closed or erased ad never reaches it.
/// </summary>
public sealed record ListJuniorJobAdsQuery(JuniorQueueView View = JuniorQueueView.Unclear, int Page = 1)
    : IQuery<JuniorJobAdQueueDto>, IAdminRequest
{
    public const int PageSize = 25;
    public const int MaxPage = 1000;
}

/// <summary>One row: what the ad itself shows, and what the rules and an admin said. Never the ad's text.</summary>
public sealed record JuniorJobAdQueueItemDto(
    Guid Id,
    string Title,
    string CompanyName,
    string? MunicipalityName,
    DateTimeOffset PublishedAt,
    DateTimeOffset? ExpiresAt,
    JuniorVerdict Verdict,
    string RuleId,
    JuniorDecision? Decision);

/// <summary>The number of listed ads in each tab, read at the same instant as the page.</summary>
public sealed record JuniorJobAdQueueCountsDto(int Unclear, int Shown, int Decided);

public sealed record JuniorJobAdQueueDto(PagedResult<JuniorJobAdQueueItemDto> Items, JuniorJobAdQueueCountsDto Counts);
