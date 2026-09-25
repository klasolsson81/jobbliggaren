using System.Text.RegularExpressions;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Common.Validation;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth.ExternalLogins;

/// <summary>#1744 (ADR 0142 D8) — the external login's value types: bounds, parsing and what they print.</summary>
public class PkceTests
{
    [Fact]
    public void ToChallenge_ShouldBeTheS256OfTheVerifier_WhenGivenRfc7636AppendixB()
    {
        // RFC 7636 Appendix B: the one published S256 vector.
        var verifier = PkceVerifier.FromRaw("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk");

        verifier.ToChallenge().Value.ShouldBe("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
    }

    [Fact]
    public void Generate_ShouldMintFortyThreeUnreservedCharacters_WhenCalled()
    {
        // RFC 7636 §4.1: 43 to 128 characters of [A-Z] [a-z] [0-9] "-" "." "_" "~".
        var raw = PkceVerifier.Generate().Reveal();

        raw.Length.ShouldBe(43);
        Regex.IsMatch(raw, "^[A-Za-z0-9_-]+$").ShouldBeTrue();
    }

    [Fact]
    public void Generate_ShouldMintADifferentVerifier_WhenCalledTwice() =>
        PkceVerifier.Generate().Reveal().ShouldNotBe(PkceVerifier.Generate().Reveal());

    [Fact]
    public void ToString_ShouldPrintNoPartOfTheVerifier_WhenInterpolated()
    {
        var verifier = PkceVerifier.Generate();

        $"{verifier}".ShouldNotContain(verifier.Reveal()[..6]);
    }

    [Fact]
    public void Method_ShouldBeS256_WhenRead() => PkceChallenge.Method.ShouldBe("S256");
}

public class OAuthStateTests
{
    [Fact]
    public void Generate_ShouldMintFortyThreeBase64UrlCharacters_WhenCalled()
    {
        var raw = OAuthState.Generate().Reveal();

        raw.Length.ShouldBe(OAuthState.EncodedLength);
        Regex.IsMatch(raw, "^[A-Za-z0-9_-]{43}$").ShouldBeTrue();
    }

    [Fact]
    public void Generate_ShouldMintADifferentState_WhenCalledTwice() =>
        OAuthState.Generate().ShouldNotBe(OAuthState.Generate());

    [Fact]
    public void ToString_ShouldPrintASixCharacterPrefixOnly_WhenInterpolated()
    {
        var state = OAuthState.Generate();

        $"{state}".ShouldBe($"{state.Reveal()[..6]}…");
    }
}

public class AuthorizationCodeTests
{
    [Fact]
    public void ToString_ShouldPrintNoPartOfTheCode_WhenInterpolated() =>
        $"{AuthorizationCode.FromRaw("4/0AVGzR1A-secret")}".ShouldNotContain("4/0A");
}

public class ExternalSubjectTests
{
    [Fact]
    public void TryCreate_ShouldAcceptTheLongestSubjectOidcAllows_WhenEveryCharacterIsVisibleAscii()
    {
        var raw = new string('7', ExternalSubject.MaximumLength);

        ExternalSubject.TryCreate(raw)!.Value.Reveal().ShouldBe(raw);
    }

    [Theory]
    [InlineData("!")]
    [InlineData("~")]
    public void TryCreate_ShouldAccept_WhenTheSubjectIsTheFirstOrLastVisibleAsciiCharacter(string raw) =>
        ExternalSubject.TryCreate(raw)!.Value.Reveal().ShouldBe(raw);

    [Fact]
    public void MaximumLength_ShouldBeOidcCoresBound_WhenRead() => ExternalSubject.MaximumLength.ShouldBe(255);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryCreate_ShouldRefuse_WhenTheSubjectIsMissing(string? raw) =>
        ExternalSubject.TryCreate(raw).ShouldBeNull();

    public static TheoryData<string> Refused => new()
    {
        new string('7', 256),
        "110248 495921",
        "1102484" + (char)9 + "95921",
        "1102484" + (char)127 + "95921",
        "1102484" + (char)0xE9 + "95921",
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void TryCreate_ShouldRefuse_WhenTheSubjectIsTooLongOrNotVisibleAscii(string raw) =>
        ExternalSubject.TryCreate(raw).ShouldBeNull();

    [Fact]
    public void ToString_ShouldPrintNoPartOfTheSubject_WhenInterpolated() =>
        $"{ExternalSubject.TryCreate("110248495921238986420")}".ShouldNotContain("110248");
}

public class VerifiedEmailTests
{
    [Fact]
    public void TryCreate_ShouldKeepTheSpellingItWasGiven_WhenTheAddressIsWellFormed() =>
        VerifiedEmail.TryCreate("Anna.Berg@firma.example")!.Value.ShouldBe("Anna.Berg@firma.example");

    [Fact]
    public void TryCreate_ShouldAcceptTheLongestAddressAValidatorAdmits_WhenAtTheBound()
    {
        var address = new string('a', EmailAddressRules.MaximumLength - "@x.example".Length) + "@x.example";

        VerifiedEmail.TryCreate(address).ShouldNotBeNull();
    }

    [Fact]
    public void TryCreate_ShouldRefuse_WhenTheAddressIsOneCharacterOverTheBound()
    {
        var address = new string('a', EmailAddressRules.MaximumLength - "@x.example".Length + 1) + "@x.example";

        VerifiedEmail.TryCreate(address).ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("anna")]
    [InlineData("@firma.example")]
    [InlineData("anna@")]
    [InlineData("anna@firma@example")]
    public void TryCreate_ShouldRefuse_WhenTheAddressIsNotOneLocalPartAndOneDomain(string? address) =>
        VerifiedEmail.TryCreate(address).ShouldBeNull();

    [Fact]
    public void ToString_ShouldPrintNoPartOfTheAddress_WhenInterpolated() =>
        $"{VerifiedEmail.TryCreate("anna@firma.example")}".ShouldNotContain("anna");
}

public class ExternalProviderKeyTests
{
    [Fact]
    public void TryParse_ShouldFindGoogle_WhenGivenItsKey()
    {
        ExternalProviderKey.TryParse("google", out var key).ShouldBeTrue();

        key.ShouldBe(ExternalProviderKey.Google);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Google")]
    [InlineData("GOOGLE")]
    [InlineData("google ")]
    [InlineData("github")]
    [InlineData("..")]
    public void TryParse_ShouldRefuse_WhenTheKeyIsNotKnownSpelledExactly(string? raw) =>
        ExternalProviderKey.TryParse(raw, out _).ShouldBeFalse();

    [Fact]
    public void Known_ShouldBeGoogleAlone_WhenOnlyPart6aHasShipped() =>
        ExternalProviderKey.Known.ShouldBe([ExternalProviderKey.Google]);

    [Fact]
    public void LoginMethod_ShouldBeGoogle_WhenTheKeyIsGoogle() =>
        ExternalProviderKey.Google.LoginMethod.ShouldBe(LoginMethod.Google);

    [Fact]
    public void LoginMethod_ShouldBeDefined_ForEveryKnownKey()
    {
        foreach (var key in ExternalProviderKey.Known)
            Enum.IsDefined(key.LoginMethod).ShouldBeTrue();
    }
}
