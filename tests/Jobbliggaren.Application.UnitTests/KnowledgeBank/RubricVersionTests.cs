using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.KnowledgeBank;

/// <summary>
/// Fas 4 STEG 7 (F4-7) — <see cref="RubricVersion"/> value object (semver triple).
/// The rubric is versioned data (ADR 0071/0074 OQ3): a parse must FAIL LOUD on a
/// malformed version (FormatException) rather than silently coercing to 0.0.0, and
/// ordering must follow major → minor → patch so the loader can pick the highest /
/// reject an unexpected version.
///
/// RED until RubricVersion ships in Jobbliggaren.Application.KnowledgeBank.Abstractions.
/// </summary>
public class RubricVersionTests
{
    [Fact]
    public void Parse_ShouldRoundTripViaToString_WhenWellFormed()
    {
        var version = RubricVersion.Parse("1.0.0");

        version.Major.ShouldBe(1);
        version.Minor.ShouldBe(0);
        version.Patch.ShouldBe(0);
        version.ToString().ShouldBe("1.0.0");
    }

    [Fact]
    public void Parse_ShouldPreserveNonZeroComponents_WhenWellFormed()
    {
        var version = RubricVersion.Parse("2.13.7");

        version.Major.ShouldBe(2);
        version.Minor.ShouldBe(13);
        version.Patch.ShouldBe(7);
        version.ToString().ShouldBe("2.13.7");
    }

    [Theory]
    [InlineData("1.0")]      // missing patch — fail loud, NOT coerced to 1.0.0
    [InlineData("x.y.z")]    // non-numeric
    [InlineData("")]         // empty
    [InlineData("1.0.0.0")]  // too many components
    [InlineData("1.-1.0")]   // negative component
    public void Parse_ShouldThrowFormatException_WhenMalformed(string value)
    {
        // Fail-loud (ADR 0071/0074): a malformed rubric version must surface as a
        // FormatException, never be silently coerced to a default — a wrong version
        // would mis-route the loader's compatibility decision.
        Action act = () => RubricVersion.Parse(value);

        act.ShouldThrow<FormatException>();
    }

    [Fact]
    public void Parse_ShouldThrowFormatException_WhenNull()
    {
        Action act = () => RubricVersion.Parse(null!);

        act.ShouldThrow<FormatException>();
    }

    [Theory]
    [InlineData("1.0.0", 1, 0, 0)]
    [InlineData("0.9.0", 0, 9, 0)]
    [InlineData("10.20.30", 10, 20, 30)]
    public void TryParse_ShouldReturnTrueAndPopulateVersion_WhenWellFormed(
        string value, int major, int minor, int patch)
    {
        var ok = RubricVersion.TryParse(value, out var version);

        ok.ShouldBeTrue();
        version.Major.ShouldBe(major);
        version.Minor.ShouldBe(minor);
        version.Patch.ShouldBe(patch);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("x.y.z")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_ShouldReturnFalse_WhenMalformedOrNull(string? value)
    {
        var ok = RubricVersion.TryParse(value, out _);

        ok.ShouldBeFalse();
    }

    [Fact]
    public void CompareTo_ShouldOrderByMajorFirst_WhenMajorDiffers()
    {
        RubricVersion.Parse("0.9.0").CompareTo(RubricVersion.Parse("1.0.0"))
            .ShouldBeLessThan(0);
        RubricVersion.Parse("2.0.0").CompareTo(RubricVersion.Parse("1.9.9"))
            .ShouldBeGreaterThan(0);
    }

    [Fact]
    public void CompareTo_ShouldOrderByMinor_WhenMajorEqual()
    {
        RubricVersion.Parse("1.1.0").CompareTo(RubricVersion.Parse("1.2.0"))
            .ShouldBeLessThan(0);
    }

    [Fact]
    public void CompareTo_ShouldOrderByPatch_WhenMajorAndMinorEqual()
    {
        RubricVersion.Parse("1.0.1").CompareTo(RubricVersion.Parse("1.0.2"))
            .ShouldBeLessThan(0);
    }

    [Fact]
    public void CompareTo_ShouldReturnZero_WhenEqual()
    {
        RubricVersion.Parse("1.0.0").CompareTo(RubricVersion.Parse("1.0.0"))
            .ShouldBe(0);
    }

    [Fact]
    public void Equality_ShouldBeByValue_WhenSameTriple()
    {
        // record struct — structural value equality.
        RubricVersion.Parse("1.0.0").ShouldBe(RubricVersion.Parse("1.0.0"));
        RubricVersion.Parse("1.0.0").ShouldNotBe(RubricVersion.Parse("1.0.1"));
    }
}
