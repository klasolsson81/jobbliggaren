using Ardalis.SmartEnum;

namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// The typeface pair for a templated CV (Fas 4b PR-3, ADR 0096 — design handoff §5.5
/// TYPSNITT: Modern/Klassisk). <see cref="Modern"/> is intended as the app's sans
/// (Source Sans 3, #564) and <see cref="Classic"/> as a serif; the concrete font stacks
/// are a rendering concern (Infrastructure), not stored here. <b>Interim (PR-8b 8b.1):</b>
/// both values render with the QuestPDF-bundled Lato — the only embedded, deterministic,
/// åäö-covering font available — because the intended sans/serif faces need an embedded
/// OFL asset that is a flagged follow-up (no system-font resolution, per determinism/CI
/// stability). The distinction becomes visible when that asset lands. Swedish display
/// labels ("Modern"/"Klassisk") resolve via <c>messages/sv.json</c>.
/// </summary>
public sealed class CvFontPair : SmartEnum<CvFontPair>
{
    /// <summary>Sans-serif pair (Source Sans 3). Default.</summary>
    public static readonly CvFontPair Modern = new(nameof(Modern), 1);

    /// <summary>Serif heading pair (Georgia).</summary>
    public static readonly CvFontPair Classic = new(nameof(Classic), 2);

    private CvFontPair(string name, int value) : base(name, value) { }
}
