using System.Text.Json.Serialization;

namespace Jobbliggaren.Domain.JobSeekers;

/// <summary>
/// ADR 0080 Vag 4 — how often a consenting user's accumulated Strong-match digest is sent
/// (Top matches are always direct, regardless of cadence). Only meaningful when
/// <see cref="Preferences.BackgroundMatchNotificationsEnabled"/> is true. The C# default
/// parameter on <see cref="Preferences"/> is <see cref="Weekly"/> (the gentler civic
/// default; matches the existing WeeklySummary mental model). Klas product decision
/// 2026-06-24: user-choice, default Weekly.
/// <para>
/// <b>Two serialization forms:</b> the <c>[JsonConverter]</c> below makes it serialize by
/// NAME on the WIRE (System.Text.Json — the future settings DTO). EF Core's owned-JSON
/// (<c>OwnsOne(...).ToJson()</c> on <c>Preferences</c>) does NOT honour that attribute and
/// persists this as an ORDINAL inside the <c>preferences</c> jsonb — so this enum is
/// <b>APPEND-ONLY, never reorder</b> (inserting a value before an existing one would silently
/// misread persisted rows). A legacy row missing the key reads <see cref="Daily"/> (ordinal
/// 0), which is moot while notifications are disabled (the GDPR-critical default OFF is the
/// load-bearing one — proven by PreferencesConsentBackcompatTests).
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DigestCadence
{
    // APPEND-ONLY (ordinal-persisted in the preferences jsonb) — never reorder.
    /// <summary>One digest per day.</summary>
    Daily,

    /// <summary>One digest per week (the civic default).</summary>
    Weekly,
}
