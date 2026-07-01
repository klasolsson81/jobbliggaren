using System.Text.Json.Serialization;

namespace Jobbliggaren.Domain.CompanyWatches;

/// <summary>
/// What a <see cref="CompanyWatch"/> targets. <b>Single-member in v1 (ADR 0087 D3):</b>
/// EMPLOYER (a single org.nr). A one-member enum is deliberate — modelling
/// EMPLOYER|BRAND_GROUP now would be speculative generality (Fowler); BRAND_GROUP arrives
/// with D4 (deferred, PR-5). The discriminator exists so the persisted column is forward-
/// compatible (a new member is an additive enum change, not a schema migration), and is
/// stored by NAME (reorder-safe) — parity with <c>NotifiableMatchGrade</c> /
/// <c>NotificationStatus</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CompanyWatchTargetType
{
    Employer = 0,
}
