using Jobbliggaren.Domain.Resumes;

namespace Jobbliggaren.Infrastructure.Resumes.Rendering;

/// <summary>
/// The visual CV renderer's colour palette (Fas 4 STEG 10 / PR-8b). Body ink-1 <c>#0C1A2E</c> and
/// secondary ink-2 <c>#455366</c> on surface <c>#FFFFFF</c> are the canonical DESIGN.md tokens; the
/// four curated accents (Marinblå/Skogsgrön/Vinröd/Grafit — the closed <see cref="CvAccentColor"/>
/// set, ADR 0096) live here in Infrastructure per the <see cref="CvAccentColor"/> contract ("the hex
/// values … live with the renderer/palette in Infrastructure, not [the Domain]"). Every
/// foreground/background pair the renderer uses MUST clear the WCAG 2.1 AA 4.5:1 normal-text floor —
/// enforced build-failing over <see cref="Pairs"/> (CLAUDE.md §1 civic a11y bar). An accent is used
/// two ways, both the SAME <c>{accent, white}</c> pair (contrast is symmetric): as a heading/stroke on
/// the white surface (Klar/Accentlinje) AND as the MorkPanel side-panel background under white panel
/// text (Klas 2026-07-12: "panelfärg = vald accent"). Registering each accent once therefore guards
/// both — if a token edit drops any accent below 4.5:1 the fitness test turns red.
/// </summary>
internal static class CvPalette
{
    /// <summary>A named foreground/background colour pair used by the visual renderer.</summary>
    internal sealed record CvPalettePair(
        string Name,
        (byte R, byte G, byte B) Foreground,
        (byte R, byte G, byte B) Background);

    /// <summary>Page surface (white).</summary>
    public static (byte R, byte G, byte B) Surface { get; } = (0xFF, 0xFF, 0xFF);

    /// <summary>Primary body text (ink-1).</summary>
    public static (byte R, byte G, byte B) BodyInk { get; } = (0x0C, 0x1A, 0x2E);

    /// <summary>Secondary text — meta lines (ink-2).</summary>
    public static (byte R, byte G, byte B) Secondary { get; } = (0x45, 0x53, 0x66);

    /// <summary>
    /// Text on the MorkPanel side panel (whose background is the chosen accent). White maximises
    /// contrast against every one of the four dark accents, so the panel stays legible regardless of
    /// the accent chosen (the <c>{accent, white}</c> pair already guards this).
    /// </summary>
    public static (byte R, byte G, byte B) PanelText { get; } = (0xFF, 0xFF, 0xFF);

    // The four curated accent hexes (design handoff §5.5). Private — resolved only through Accent(...)
    // from the closed Domain SmartEnum, never as free strings (CLAUDE.md §5).
    private static (byte R, byte G, byte B) NavyBlue { get; } = (0x1E, 0x3A, 0x5F);
    private static (byte R, byte G, byte B) ForestGreen { get; } = (0x15, 0x60, 0x3F);
    private static (byte R, byte G, byte B) WineRed { get; } = (0x7A, 0x2E, 0x35);
    private static (byte R, byte G, byte B) Graphite { get; } = (0x3A, 0x44, 0x51);

    /// <summary>
    /// Resolves a curated accent choice to its hex colour (closed set — never a free string). Fail-loud
    /// on an unmapped accent: a new <see cref="CvAccentColor"/> member added without a hex here throws
    /// rather than silently backfilling one accent's colour (which would also escape the WCAG guard on
    /// <see cref="Pairs"/>). The <c>CvPalette_EveryAccentResolvesAndIsGuarded</c> test drives
    /// <c>CvAccentColor.List</c> so the drift is caught at test time, before production.
    /// </summary>
    public static (byte R, byte G, byte B) Accent(CvAccentColor accent) => accent.Name switch
    {
        nameof(CvAccentColor.NavyBlue) => NavyBlue,
        nameof(CvAccentColor.ForestGreen) => ForestGreen,
        nameof(CvAccentColor.WineRed) => WineRed,
        nameof(CvAccentColor.Graphite) => Graphite,
        _ => throw new ArgumentOutOfRangeException(
            nameof(accent), accent.Name,
            "Ny CvAccentColor saknar hex i CvPalette.Accent + ett par i CvPalette.Pairs (WCAG-gardat)."),
    };

    /// <summary>"#RRGGBB" for a colour (QuestPDF accepts a hex string for FontColor/Background).</summary>
    public static string Hex((byte R, byte G, byte B) color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>The contrast-guarded colour pairs. Adding a pair automatically extends the guard.</summary>
    public static IReadOnlyList<CvPalettePair> Pairs { get; } =
    [
        new("body-ink-1", BodyInk, Surface),
        new("secondary-ink-2", Secondary, Surface),
        // Each accent must clear AA as a heading/stroke on white AND (symmetrically) as the MorkPanel
        // background under white panel text — one pair guards both uses.
        new("accent-navyblue", NavyBlue, Surface),
        new("accent-forestgreen", ForestGreen, Surface),
        new("accent-winered", WineRed, Surface),
        new("accent-graphite", Graphite, Surface),
    ];
}
