using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.NotificationFeed;

/// <summary>
/// Why a notification exists (#1666). The order of the members is the tie-break between sources and the precedence
/// between them (ADR 0173 D7, ADR 0175 D8): a match outranks a followed company's ad, which outranks a Branschbevakning's.
/// <c>NotificationFeedSources.All</c> and <c>Markers</c> follow the same order.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<NotificationFeedSourceKind>))]
public enum NotificationFeedSourceKind
{
    Match,
    FollowedCompanyAd,
    CompanyWatchCriterionAd,
}
