using System.Text.Json.Serialization;

namespace Jobbliggaren.Domain.Feedback;

/// <summary>Where the operator is with a submission: Ny, Pågår, Åtgärdad, Avstår.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FeedbackStatus
{
    New,
    InProgress,
    Resolved,
    Declined,
}
