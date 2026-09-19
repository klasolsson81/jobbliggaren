using Jobbliggaren.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// The one home of the address fingerprint every Redis key derived from an address uses (ADR 0142 D1,
/// security-auditor Major 3). The property under test: two spellings Identity resolves to ONE account
/// get ONE fingerprint, so no key built on it can be split into independent windows or budgets by
/// re-spelling the address.
/// </summary>
public sealed class SubjectFingerprintTests
{
    [Fact]
    public void Hex_GivesEverySpellingIdentityFoldsTogetherOneFingerprint()
    {
        // Swept over the whole BMP in the runtime that ships, so a character nobody listed cannot slip
        // through. Each character is compared against ITS OWN upper-case form: a fixed probe letter would
        // iterate over nothing and pass while measuring zero characters.
        var normalizer = new UpperInvariantLookupNormalizer();
        var offenders = new List<string>();
        var swept = 0;

        for (var cp = 0x21; cp <= 0xFFFF; cp++)
        {
            if (cp is >= 0xD800 and <= 0xDFFF) continue;
            var c = (char)cp;
            var upper = char.ToUpperInvariant(c);
            if (upper == c) continue;

            var lowerForm = $"a{c}b@example.com";
            var upperForm = $"a{upper}b@example.com";
            if (normalizer.NormalizeEmail(lowerForm) != normalizer.NormalizeEmail(upperForm)) continue;
            swept++;

            if (SubjectFingerprint.Hex(lowerForm) != SubjectFingerprint.Hex(upperForm))
                offenders.Add($"U+{cp:X4}");
        }

        offenders.ShouldBeEmpty(
            "every spelling Identity resolves to one account must share one fingerprint");
        swept.ShouldBeGreaterThan(500, "the sweep must actually have exercised the function");
        char.ToUpperInvariant('ſ').ShouldBe('S', "U+017F is the character the audit measured");
        char.ToUpperInvariant('ı').ShouldBe('ı', "U+0131 is NOT a bypass character in .NET");
    }

    [Fact]
    public void Hex_CollapsesADecomposedSpellingOntoItsComposedForm()
    {
        const string composed = "bö@example.com";
        const string decomposed = "bö@example.com";
        composed.ShouldNotBe(decomposed);
        new UpperInvariantLookupNormalizer().NormalizeEmail(decomposed)
            .ShouldBe(new UpperInvariantLookupNormalizer().NormalizeEmail(composed));

        SubjectFingerprint.Hex(decomposed).ShouldBe(SubjectFingerprint.Hex(composed));
    }

    [Fact]
    public void Hex_IgnoresSurroundingWhitespaceAndCase()
    {
        SubjectFingerprint.Hex("  Klas@Example.COM ").ShouldBe(SubjectFingerprint.Hex("klas@example.com"));
    }

    [Fact]
    public void Hex_KeepsDifferentAddressesApart()
    {
        // The counterfactual: without it the tests above are satisfied by a function that maps every
        // subject to one constant.
        SubjectFingerprint.Hex("a@example.com").ShouldNotBe(SubjectFingerprint.Hex("b@example.com"));
    }

    [Fact]
    public void Hex_IsTheLowerHexSha256OfTheUpperCasedSubject_NeverTheRawValue()
    {
        // Golden master: sha256("KLAS@EXAMPLE.COM"), lower hex. RedisCooldownGateTests pins the same value
        // inside the shipped cooldown key, so a change here that resets in-flight windows fails both.
        var hex = SubjectFingerprint.Hex("klas@example.com");

        hex.ShouldBe("fac6ba54474a51ba1a02f54ae399a4998d235bcf2c44f1b3a04ffb4572ac70f8");
        hex.ShouldNotContain("klas", Case.Insensitive);
    }
}
