namespace Jobbliggaren.Infrastructure.Resumes.Rendering;

/// <summary>
/// Deterministic WCAG 2.1 relative-luminance + contrast-ratio math (Fas 4 STEG 10, F4-10) —
/// pure BCL, no dependency. Used to validate the visual CV renderer's palette against the AA
/// 4.5:1 normal-text floor (the QuestPDF document that consumes the palette is Phase B; the
/// math + the palette guard ship in Phase A so the contrast budget is pinned before any
/// rendering code exists). Reference: WCAG 2.1 relative-luminance definition.
/// </summary>
internal static class WcagContrast
{
    /// <summary>The WCAG 2.1 relative luminance of an sRGB colour (0.0 black → 1.0 white).</summary>
    public static double RelativeLuminance(byte r, byte g, byte b) =>
        (0.2126 * Linearize(r)) + (0.7152 * Linearize(g)) + (0.0722 * Linearize(b));

    /// <summary>The WCAG 2.1 contrast ratio between two colours (1.0 → 21.0). Symmetric:
    /// the lighter luminance is always the numerator, so foreground/background order is free.</summary>
    public static double ContrastRatio((byte R, byte G, byte B) fg, (byte R, byte G, byte B) bg)
    {
        var l1 = RelativeLuminance(fg.R, fg.G, fg.B);
        var l2 = RelativeLuminance(bg.R, bg.G, bg.B);
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    // sRGB channel linearisation (WCAG 2.1): c/255, then gamma-expand.
    private static double Linearize(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}
