using System.Text.Json.Serialization;

namespace Jobbliggaren.Domain.Feedback;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FeedbackNotificationState
{
    /// <summary>Waiting for its send, now or after a backoff.</summary>
    Queued,

    /// <summary>Claimed and handed to the provider; the outcome is not yet recorded.</summary>
    Sending,

    /// <summary>The provider accepted the message. Not a claim that it reached the inbox.</summary>
    Accepted,

    /// <summary>Every attempt was refused by the provider; nothing was sent.</summary>
    Failed,

    /// <summary>The provider may or may not have taken it. Never resent on its own.</summary>
    Unknown,
}
