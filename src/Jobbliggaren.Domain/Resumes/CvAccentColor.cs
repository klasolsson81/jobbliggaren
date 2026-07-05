using Ardalis.SmartEnum;

namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// The curated accent colour for a templated CV (Fas 4b PR-3, ADR 0096 — design
/// handoff §5.5: exactly four curated accents, "text alltid svart på vitt";
/// kunskapsbank E4 caps the palette). A CLOSED set — never a free hex string
/// (CLAUDE.md §5: a free colour string is free text; a SmartEnum keeps the column
/// provably enumerated). The hex values (Marinblå #1E3A5F, Skogsgrön #15603F,
/// Vinröd #7A2E35, Grafit #3A4451, plus derived side/tint shades) are a rendering
/// concern and live with the renderer/palette in Infrastructure (PR-8b), not here —
/// the Domain stores only which curated colour was chosen.
/// </summary>
public sealed class CvAccentColor : SmartEnum<CvAccentColor>
{
    /// <summary>Marinblå (#1E3A5F). Default.</summary>
    public static readonly CvAccentColor NavyBlue = new(nameof(NavyBlue), 1);

    /// <summary>Skogsgrön (#15603F).</summary>
    public static readonly CvAccentColor ForestGreen = new(nameof(ForestGreen), 2);

    /// <summary>Vinröd (#7A2E35).</summary>
    public static readonly CvAccentColor WineRed = new(nameof(WineRed), 3);

    /// <summary>Grafit (#3A4451).</summary>
    public static readonly CvAccentColor Graphite = new(nameof(Graphite), 4);

    private CvAccentColor(string name, int value) : base(name, value) { }
}
