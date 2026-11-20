using System.Text.Json.Serialization;

namespace Jobbliggaren.Domain.JobAds;

/// <summary>
/// #2264 — an admin's decision about one ad in an enabled occupation field. It outranks every rule and is never
/// overwritten by a re-classification. A type of its own, so Unclear can never be a decision.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum JuniorDecision
{
    Junior = 1,
    NotJunior = 2,
}
