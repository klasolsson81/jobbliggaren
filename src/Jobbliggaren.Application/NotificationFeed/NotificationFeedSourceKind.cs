using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.NotificationFeed;

/// <summary>
/// Why a notification exists (#1666). The unit of the bell is a (source, ad) pair: an ad that is both a
/// background match and a followed company's new ad is two notifications, for two reasons, each
/// acknowledged through its own watermark. The order of the members is the tie-break between sources.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<NotificationFeedSourceKind>))]
public enum NotificationFeedSourceKind
{
    Match,
    FollowedCompanyAd,
}
