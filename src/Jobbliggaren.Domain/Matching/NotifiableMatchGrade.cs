using System.Text.Json.Serialization;

namespace Jobbliggaren.Domain.Matching;

/// <summary>
/// ADR 0080 Vag 4 — the NOTIFIABLE subset of the match-grade ladder, persisted on
/// <see cref="UserJobAdMatch"/>. A named ordinal CATEGORY, never a number (Goodhart guard,
/// ADR 0071/0076 — an architecture test forbids any numeric field on the aggregate).
/// <para>
/// Deliberately a Domain-local enum, NOT the Application <c>MatchGrade</c>: the matching
/// engine (scorer + <c>MatchGradeCalculator</c>) lives in Application, so Domain must not
/// reference it (dependency rule). The Worker scan maps the computed
/// <c>MatchGrade</c> to this type when it persists a match — and that mapping is the
/// honest floor: only <see cref="Good"/>/<see cref="Strong"/>/<see cref="Top"/> are
/// notifiable. <c>Basic</c> and "no grade" are never persisted as a match (Beslut 6 / D1).
/// <para>
/// This type IS the realization of ADR 0080 Beslut 1's "<c>Grade</c> (MatchGrade enum,
/// persisted)": the ADR's literal text would have a Domain aggregate reference the
/// Application <c>MatchGrade</c> (a dependency-rule breach), so the notifiable subset is the
/// correct Clean-Architecture boundary, with the Application-side anti-corruption map.
/// </para>
/// </para>
/// <para>
/// <b>Routing (ADR 0080 D4, Klas 2026-06-24):</b> <see cref="Top"/> = direct (notified the
/// same scan run); <see cref="Strong"/> = digest (capped, user cadence default Weekly);
/// <see cref="Good"/> = in-app count only (no email). Serialized by NAME.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotifiableMatchGrade
{
    /// <summary>"Bra match" — surfaced in the in-app count only, never emailed.</summary>
    Good,

    /// <summary>"Stark match" — accumulates into the (capped) digest.</summary>
    Strong,

    /// <summary>"Toppmatch" — notified directly (the FULL grade the Worker is the first to produce).</summary>
    Top,
}
