using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.NotificationFeed;

/// <summary>
/// Why a notification exists (#1666). The order of the members is the tie-break between sources.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<NotificationFeedSourceKind>))]
public enum NotificationFeedSourceKind
{
    Match,
    FollowedCompanyAd,
}
