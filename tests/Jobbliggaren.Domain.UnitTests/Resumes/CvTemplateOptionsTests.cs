using Jobbliggaren.Domain.Resumes;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Resumes;

// Fas 4b CV-motor v2 PR-3 (issue #652, epic #649, ADR 0096). The CvTemplateOptions VO plus the
// persisted-name contract of the source-metadata SmartEnums.
//
// Two contracts are pinned here:
//   1. The handoff-bound defaults (§5.5/§8) — a silent drift in the Default preset is a product
//      regression, so it must fail a test.
//   2. The SmartEnum .Name tokens — these strings ARE the persisted column vocabulary
//      (Name-string mapping, ADR 0096 / CvTemplate remarks). A rename is a breaking schema change
//      and must break a test, not slip through silently.
public class CvTemplateOptionsTests
{
    // ---------------------------------------------------------------
    // Default preset — design handoff §5.5/§8
    // ---------------------------------------------------------------

    [Fact]
    public void Default_MatchesHandoffDefaults()
    {
        var d = CvTemplateOptions.Default;

        d.Template.ShouldBe(CvTemplate.Klar);          // §8 "Default. Maximal läsbarhet"
        d.AccentColor.ShouldBe(CvAccentColor.NavyBlue); // Marinblå
        d.FontPair.ShouldBe(CvFontPair.Modern);
        d.Density.ShouldBe(CvDensity.Normal);
        d.PhotoEnabled.ShouldBeFalse();                 // FOTO-ETIK — foto_default=false
        d.PhotoShape.ShouldBe(CvPhotoShape.Circle);     // preset even while photo is OFF
        d.IsComplete.ShouldBeTrue();
    }

    // ---------------------------------------------------------------
    // IsComplete — completeness guard used by Resume.ChangeTemplateOptions
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(-1, true)]  // nothing nulled — the Default is complete
    [InlineData(0, false)]  // Template null
    [InlineData(1, false)]  // AccentColor null
    [InlineData(2, false)]  // FontPair null
    [InlineData(3, false)]  // Density null
    [InlineData(4, false)]  // PhotoShape null
    public void IsComplete_TrueForDefault_FalseWhenAnyMemberNull(int nulledMember, bool expected)
    {
        // PhotoEnabled is a bool (never null), so only the five reference members are exercised.
        var options = nulledMember switch
        {
            -1 => CvTemplateOptions.Default,
            0 => CvTemplateOptions.Default with { Template = null! },
            1 => CvTemplateOptions.Default with { AccentColor = null! },
            2 => CvTemplateOptions.Default with { FontPair = null! },
            3 => CvTemplateOptions.Default with { Density = null! },
            4 => CvTemplateOptions.Default with { PhotoShape = null! },
            _ => throw new ArgumentOutOfRangeException(nameof(nulledMember)),
        };

        options.IsComplete.ShouldBe(expected);
    }

    // ---------------------------------------------------------------
    // Value equality — the ChangeTemplateOptions no-op depends on it
    // ---------------------------------------------------------------

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        var a = new CvTemplateOptions(
            CvTemplate.Klar, CvAccentColor.NavyBlue, CvFontPair.Modern,
            CvDensity.Normal, PhotoEnabled: false, CvPhotoShape.Circle);
        var b = new CvTemplateOptions(
            CvTemplate.Klar, CvAccentColor.NavyBlue, CvFontPair.Modern,
            CvDensity.Normal, PhotoEnabled: false, CvPhotoShape.Circle);

        // Distinct instances, equal by value (SmartEnum singletons + bool compare cleanly).
        a.ShouldBe(b);
        (a == b).ShouldBeTrue();
        a.GetHashCode().ShouldBe(b.GetHashCode());
        // Value-equal to the Default singleton too.
        a.ShouldBe(CvTemplateOptions.Default);
        // A single differing member breaks equality — the no-op guard must react to any change.
        (a with { PhotoEnabled = true }).ShouldNotBe(a);
    }

    // ---------------------------------------------------------------
    // Persisted-name contract — Name tokens are the stored column vocabulary
    // ---------------------------------------------------------------

    [Fact]
    public void ResumeSourceOrigin_PersistedNames_AreStable()
    {
        ResumeSourceOrigin.Legacy.Name.ShouldBe("Legacy");
        ResumeSourceOrigin.Import.Name.ShouldBe("Import");
        ResumeSourceOrigin.Template.Name.ShouldBe("Template");
    }

    [Fact]
    public void CvTemplate_PersistedNames_AreStable()
    {
        CvTemplate.Klar.Name.ShouldBe("Klar");
        CvTemplate.Accentlinje.Name.ShouldBe("Accentlinje");
        CvTemplate.MorkPanel.Name.ShouldBe("MorkPanel");
    }

    [Fact]
    public void CvAccentColor_PersistedNames_AreStable()
    {
        CvAccentColor.NavyBlue.Name.ShouldBe("NavyBlue");
        CvAccentColor.ForestGreen.Name.ShouldBe("ForestGreen");
        CvAccentColor.WineRed.Name.ShouldBe("WineRed");
        CvAccentColor.Graphite.Name.ShouldBe("Graphite");
    }

    [Fact]
    public void CvFontPair_PersistedNames_AreStable()
    {
        CvFontPair.Modern.Name.ShouldBe("Modern");
        CvFontPair.Classic.Name.ShouldBe("Classic");
    }

    [Fact]
    public void CvDensity_PersistedNames_AreStable()
    {
        CvDensity.Airy.Name.ShouldBe("Airy");
        CvDensity.Normal.Name.ShouldBe("Normal");
        CvDensity.Compact.Name.ShouldBe("Compact");
    }

    [Fact]
    public void CvPhotoShape_PersistedNames_AreStable()
    {
        CvPhotoShape.Circle.Name.ShouldBe("Circle");
        CvPhotoShape.Rounded.Name.ShouldBe("Rounded");
        CvPhotoShape.Square.Name.ShouldBe("Square");
    }
}
