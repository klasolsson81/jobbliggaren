using Jobbliggaren.Infrastructure.Resumes.Rendering;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Rendering;

/// <summary>
/// Fas 4 STEG 10 (F4-10, ADR 0074) Phase A — a build-failing fitness function over the CV
/// renderer's colour palette. EVERY foreground/background pair the visual CV renderer uses
/// MUST clear the WCAG 2.1 AA 4.5:1 normal-text contrast floor (CLAUDE.md §1 civic-utility
/// tone + the project a11y bar). The palette is the canonical design-token hex (DESIGN.md):
/// body ink-1 #0C1A2E, accent #15603F, secondary ink-2 #455366 — all on #FFFFFF. If a future
/// token edit regresses a pair below 4.5:1 this test goes red, so the regression cannot merge.
///
/// <see cref="CvPalette"/> is internal in <c>Jobbliggaren.Infrastructure.Resumes.Rendering</c>
/// (Infrastructure exposes internals to this assembly). The test iterates
/// <c>CvPalette.Pairs</c> — each entry exposes a <c>Name</c>, a <c>Foreground</c>
/// (r,g,b) tuple, and a <c>Background</c> (r,g,b) tuple — so adding a new palette pair
/// automatically extends the contrast guard (no per-pair test to forget). RED until
/// <see cref="CvPalette"/> + <see cref="WcagContrast"/> ship.
/// </summary>
public class CvPaletteTests
{
    public static TheoryData<string> PalettePairNames()
    {
        var data = new TheoryData<string>();
        foreach (var pair in CvPalette.Pairs)
            data.Add(pair.Name);
        return data;
    }

    [Fact]
    public void Palette_ShouldDefineAtLeastTheThreeCanonicalTokenPairs_WhenLoaded()
    {
        // Body ink-1, accent, secondary ink-2 — the three DESIGN.md token pairs. The guard
        // must cover at least these (more pairs are welcome; each is contrast-checked below).
        CvPalette.Pairs.Count.ShouldBeGreaterThanOrEqualTo(3,
            "Paletten ska minst definiera body ink-1, accent och secondary ink-2 (DESIGN.md).");
    }

    [Theory]
    [MemberData(nameof(PalettePairNames))]
    public void ContrastRatio_ShouldMeetWcagAaNormalText_ForEveryPalettePair(string pairName)
    {
        var pair = CvPalette.Pairs.Single(p => p.Name == pairName);

        var ratio = WcagContrast.ContrastRatio(pair.Foreground, pair.Background);

        ratio.ShouldBeGreaterThanOrEqualTo(4.5,
            $"CV-palettparet '{pairName}' faller under WCAG AA 4.5:1 ({ratio:0.00}:1) — " +
            "en token-regression får inte mergas.");
    }
}
