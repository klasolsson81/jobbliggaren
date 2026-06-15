using Jobbliggaren.Infrastructure.Resumes.Rendering;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Rendering;

/// <summary>
/// Fas 4 STEG 10 (F4-10, ADR 0074) Phase A — the BCL-only WCAG relative-luminance /
/// contrast-ratio math used by the visual CV renderer (the QuestPDF IDocument that consumes
/// these palette pairs is Phase B; the math + palette guard ship in Phase A so the contrast
/// budget is pinned BEFORE any rendering code exists). Reference values are the published
/// WCAG 2.1 definitions (a fixture the test cannot drift from): black-on-white = 21:1,
/// white-on-white = 1:1.
///
/// <see cref="WcagContrast"/> is internal in <c>Jobbliggaren.Infrastructure.Resumes.Rendering</c>
/// (Infrastructure exposes internals to this assembly). RED until it ships.
/// </summary>
public class WcagContrastTests
{
    private const double Tolerance = 0.01;

    // ===============================================================
    // RelativeLuminance — WCAG 2.1 reference points
    // ===============================================================

    [Fact]
    public void RelativeLuminance_ShouldBeZero_WhenColorIsBlack()
    {
        WcagContrast.RelativeLuminance(0, 0, 0).ShouldBe(0.0, Tolerance);
    }

    [Fact]
    public void RelativeLuminance_ShouldBeOne_WhenColorIsWhite()
    {
        WcagContrast.RelativeLuminance(255, 255, 255).ShouldBe(1.0, Tolerance);
    }

    [Fact]
    public void RelativeLuminance_ShouldBeBetweenZeroAndOne_WhenColorIsMidGrey()
    {
        // #808080 mid-grey: a known reference luminance ≈ 0.2159 per the WCAG sRGB formula.
        WcagContrast.RelativeLuminance(128, 128, 128).ShouldBe(0.2159, 0.005);
    }

    // ===============================================================
    // ContrastRatio — WCAG 2.1 reference points
    // ===============================================================

    [Fact]
    public void ContrastRatio_ShouldBe21_WhenBlackOnWhite()
    {
        WcagContrast.ContrastRatio((0, 0, 0), (255, 255, 255)).ShouldBe(21.0, Tolerance);
    }

    [Fact]
    public void ContrastRatio_ShouldBe1_WhenWhiteOnWhite()
    {
        WcagContrast.ContrastRatio((255, 255, 255), (255, 255, 255)).ShouldBe(1.0, Tolerance);
    }

    [Fact]
    public void ContrastRatio_ShouldBeSymmetric_WhenForegroundAndBackgroundSwap()
    {
        // (L1+0.05)/(L2+0.05) by definition uses the lighter as L1, so swapping fg/bg yields
        // the same ratio — the renderer can pass either order.
        var ab = WcagContrast.ContrastRatio((12, 26, 46), (255, 255, 255));
        var ba = WcagContrast.ContrastRatio((255, 255, 255), (12, 26, 46));

        ab.ShouldBe(ba, Tolerance);
    }

    [Fact]
    public void ContrastRatio_ShouldBeAtLeast4Point5_WhenBodyInkOnWhite()
    {
        // The canonical body ink-1 #0C1A2E on #FFFFFF is comfortably above the 4.5:1 AA floor.
        WcagContrast.ContrastRatio((0x0C, 0x1A, 0x2E), (0xFF, 0xFF, 0xFF))
            .ShouldBeGreaterThanOrEqualTo(4.5);
    }
}
