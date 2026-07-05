using Ardalis.SmartEnum;

namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// Self-declared proficiency level for a spoken language on a canonical CV
/// (Fas 4b AppCopy superset, ADR 0093 D1 / LRM ADR 0095 D-C). A closed, ordered
/// vocabulary — the wizard offers exactly these levels (design handoff §4
/// <c>niva</c>: modersmål/flytande/god/grundläggande), so a SmartEnum, not a free
/// string, guards it (CLAUDE.md §5 — no magic strings, no primitive obsession).
/// </summary>
/// <remarks>
/// <para><b><see cref="NotStated"/> is load-bearing (ADR 0074 OQ3 honesty).</b> Import
/// parsing yields language <em>names</em> only (<c>ParsedResumeContent.Languages</c> is
/// name-only) — a language with an unknown level is not "basic", it is unknown. Promote
/// maps every imported language to <see cref="NotStated"/>; the user sets a real level in
/// the wizard. A proficiency is never synthesised (CLAUDE.md §5).</para>
///
/// <para>The <see cref="SmartEnum{T}.Name"/> tokens are English code identifiers
/// (language policy §1); the Swedish UI labels (modersmål/flytande/god/grundläggande)
/// are resolved at the frontend via <c>messages/sv.json</c>, not here. The name is the
/// stable persisted token inside the encrypted <c>content_enc</c> JSON blob (Form B,
/// ADR 0049) — serialised by <c>LanguageProficiencyJsonConverter</c>. Values are ordered
/// low→high so a future comparison (e.g. "at least Good") is a value comparison, but the
/// order is display/logic only and is never scored (Goodhart guard).</para>
/// </remarks>
public sealed class LanguageProficiency : SmartEnum<LanguageProficiency>
{
    /// <summary>Level not stated — the honest default for an imported language (never scored against the user).</summary>
    public static readonly LanguageProficiency NotStated = new(nameof(NotStated), 0);

    /// <summary>Grundläggande.</summary>
    public static readonly LanguageProficiency Basic = new(nameof(Basic), 1);

    /// <summary>God.</summary>
    public static readonly LanguageProficiency Good = new(nameof(Good), 2);

    /// <summary>Flytande.</summary>
    public static readonly LanguageProficiency Fluent = new(nameof(Fluent), 3);

    /// <summary>Modersmål.</summary>
    public static readonly LanguageProficiency Native = new(nameof(Native), 4);

    private LanguageProficiency(string name, int value) : base(name, value) { }
}
