using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Infrastructure.Resumes.Rendering;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Rendering;

/// <summary>
/// Fas 4 STEG 10 (F4-10, ADR 0074) Phase A — a build-failing fitness function over the CV
/// renderer's colour palette. EVERY foreground/background pair the visual CV renderer uses
/// MUST clear the WCAG 2.1 AA 4.5:1 normal-text contrast floor (CLAUDE.md §1 civic-utility
/// tone + the project a11y bar). The palette is the canonical design-token hex (DESIGN.md):
/// body ink-1 #0C1A2E and secondary ink-2 #455366, plus the four curated CV-template accents
/// (Marinblå #1E3A5F / Skogsgrön #15603F / Vinröd #7A2E35 / Grafit #3A4451, PR-8b) — all on
/// #FFFFFF; one {accent, white} pair guards both the heading-on-white and the white-on-accent-panel
/// use (symmetric). If a future token edit regresses a pair below 4.5:1 this test goes red, so the
/// regression cannot merge.
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

    [Fact]
    public void EveryCuratedAccent_ResolvesToADistinctHex_AndIsRegisteredInTheGuardedPairs()
    {
        // Couples the closed Domain CvAccentColor set to the two manually-maintained Infrastructure
        // surfaces (the Accent() resolver + the WCAG-guarded Pairs list). A new accent added to the
        // SmartEnum without a hex here throws in Accent() (fail-loud), and without a "accent-{name}" pair
        // it would escape the >=4.5:1 fitness guard above — this test makes that drift fail-loud at test
        // time (fail-safe default, Saltzer & Schroeder), parity CvTemplate.AtsSafe's Count pin.
        var resolved = CvAccentColor.List
            .Select(a => (a.Name, Rgb: CvPalette.Accent(a))) // throws if a member is unmapped
            .ToList();

        var pairNames = CvPalette.Pairs.Select(p => p.Name).ToHashSet();
        foreach (var (name, _) in resolved)
        {
            pairNames.ShouldContain($"accent-{name.ToLowerInvariant()}",
                $"Accenten '{name}' saknar ett WCAG-gardat par i CvPalette.Pairs.");
        }

        // No two accents silently share a hex (an unmapped one backfilling another's colour).
        resolved.Select(r => r.Rgb).Distinct().Count().ShouldBe(CvAccentColor.List.Count);
    }
}
