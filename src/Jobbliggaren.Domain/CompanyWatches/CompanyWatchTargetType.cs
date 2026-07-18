using System.Text.Json.Serialization;

namespace Jobbliggaren.Domain.CompanyWatches;

/// <summary>
/// What a <see cref="CompanyWatch"/> targets. Two members (ADR 0087 D3/D4):
/// <see cref="Employer"/> (a single org.nr) and <see cref="BrandGroup"/> (a curated set of
/// org.nrs, resolved via <c>IBrandGroupProvider</c>). The one-member v1 was deliberate — modelling
/// the discriminator ahead of a consumer would have been speculative generality (Fowler) — but the
/// column was built forward-compatible precisely so <see cref="BrandGroup"/> is an additive enum
/// change (PR-5), not a schema migration. Stored by NAME (reorder-safe) — parity with
/// <c>NotifiableMatchGrade</c> / <c>NotificationStatus</c>; <c>"BrandGroup"</c> (10 chars) fits the
/// <c>target_type varchar(20)</c> column.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CompanyWatchTargetType
{
    /// <summary>A follow of a single employer org.nr (<see cref="CompanyWatch.OrganizationNumber"/> set).</summary>
    Employer = 0,

    /// <summary>
    /// A follow of a curated brand group (<see cref="CompanyWatch.BrandGroupId"/> set): the group's
    /// member org.nrs are resolved from the versioned catalogue at scan/read time, never persisted
    /// on the watch (ADR 0087 D4 — no denormalised snapshot; the catalogue is the SSOT).
    /// </summary>
    BrandGroup = 1,
}
