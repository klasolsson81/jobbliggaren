using Ardalis.SmartEnum;

namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// The typeface pair for a templated CV (Fas 4b PR-3, ADR 0096 — design handoff §5.5
/// TYPSNITT: Modern/Klassisk). <see cref="Modern"/> maps to the app's sans
/// (Source Sans 3, #564); <see cref="Classic"/> to a serif (Georgia) — the concrete
/// font stacks are a rendering concern (PR-8b), not stored here. Swedish display
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
