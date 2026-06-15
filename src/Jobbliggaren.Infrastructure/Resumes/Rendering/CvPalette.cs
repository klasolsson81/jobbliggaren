namespace Jobbliggaren.Infrastructure.Resumes.Rendering;

/// <summary>
/// The visual CV renderer's colour palette (Fas 4 STEG 10, F4-10) — the canonical design-token
/// hex (DESIGN.md / jobbpilot-design-tokens): body ink-1 <c>#0C1A2E</c>, accent <c>#15603F</c>,
/// secondary ink-2 <c>#455366</c>, all on surface <c>#FFFFFF</c>. Every pair MUST clear the
/// WCAG 2.1 AA 4.5:1 normal-text floor — enforced by a build-failing fitness test over
/// <see cref="Pairs"/> (CLAUDE.md §1 civic-utility a11y bar). Colours are NOT invented here;
/// they mirror the locked tokens, so a token regression that drops a pair below 4.5:1 turns
/// the test red and cannot merge.
/// </summary>
internal static class CvPalette
{
    /// <summary>A named foreground/background colour pair used by the visual renderer.</summary>
    internal sealed record CvPalettePair(
        string Name,
        (byte R, byte G, byte B) Foreground,
        (byte R, byte G, byte B) Background);

    private static readonly (byte R, byte G, byte B) Surface = (0xFF, 0xFF, 0xFF);

    /// <summary>The contrast-guarded colour pairs. Adding a pair automatically extends the guard.</summary>
    public static IReadOnlyList<CvPalettePair> Pairs { get; } =
    [
        new("body-ink-1", (0x0C, 0x1A, 0x2E), Surface),
        new("accent", (0x15, 0x60, 0x3F), Surface),
        new("secondary-ink-2", (0x45, 0x53, 0x66), Surface),
    ];
}
