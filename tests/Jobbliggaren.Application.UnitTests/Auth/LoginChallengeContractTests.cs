using Jobbliggaren.Application.Auth.Commands.ConsumeLoginLink;
using Jobbliggaren.Application.Auth.Commands.VerifyLoginChallenge;
using Jobbliggaren.Application.Auth.LoginChallenges;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #1735 — the small contracts the proof path leans on: what the two validators refuse before a store
/// attempt is spent, the verdict's factory invariants, and the credentials' redacting <c>ToString</c> (an
/// interpolated log line or a failed assertion must never print a live credential in full).
/// </summary>
public sealed class LoginChallengeContractTests
{
    private static readonly string ValidId = ChallengeId.Generate().Reveal();

    [Fact]
    public void A_six_digit_code_against_an_id_passes_validation()
    {
        new VerifyLoginChallengeCommandValidator().Validate(new VerifyLoginChallengeCommand(ValidId, "042917"))
            .IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("123456\n")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    [InlineData("")]
    [InlineData(null)]
    public void A_code_of_any_other_shape_is_refused_before_it_can_spend_an_attempt(string? code)
    {
        new VerifyLoginChallengeCommandValidator().Validate(new VerifyLoginChallengeCommand(ValidId, code))
            .IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void A_missing_challenge_id_is_refused(string? challengeId)
    {
        new VerifyLoginChallengeCommandValidator().Validate(new VerifyLoginChallengeCommand(challengeId, "042917"))
            .IsValid.ShouldBeFalse();
    }

    [Fact]
    public void An_overlong_challenge_id_is_refused()
    {
        new VerifyLoginChallengeCommandValidator()
            .Validate(new VerifyLoginChallengeCommand(new string('A', 65), "042917"))
            .IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void A_missing_link_token_is_refused(string? token)
    {
        new ConsumeLoginLinkCommandValidator().Validate(new ConsumeLoginLinkCommand(token)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void A_link_token_is_refused_past_128_characters_and_admitted_at_it()
    {
        var validator = new ConsumeLoginLinkCommandValidator();

        validator.Validate(new ConsumeLoginLinkCommand(new string('A', 129))).IsValid.ShouldBeFalse();
        validator.Validate(new ConsumeLoginLinkCommand(new string('A', 128))).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void A_wrong_verdict_carries_between_one_and_two_attempts_left(int attemptsRemaining)
    {
        // The literal 3 is MaxAttempts: a Wrong with every attempt left, or with none, is a state the store
        // never answers.
        Should.Throw<ArgumentOutOfRangeException>(() => ChallengeVerdict.Wrong(attemptsRemaining));
    }

    [Fact]
    public void Only_a_verified_verdict_carries_a_proof()
    {
        Should.Throw<ArgumentNullException>(() => ChallengeVerdict.Verified(null!));

        var wrong = ChallengeVerdict.Wrong(1);
        (wrong.Proof, wrong.AttemptsRemaining, wrong.IsVerified).ShouldBe((null, 1, false));
        (ChallengeVerdict.Burned.Proof, ChallengeVerdict.Burned.AttemptsRemaining).ShouldBe((null, 0));
        (ChallengeVerdict.Missing.Proof, ChallengeVerdict.Missing.AttemptsRemaining).ShouldBe((null, 0));
        ChallengeVerdict.Verified(new LoginChallengeProof("person@example.com")).IsVerified.ShouldBeTrue();
    }

    [Fact]
    public void A_credential_prints_as_a_prefix_or_not_at_all()
    {
        var id = ChallengeId.Generate();
        var link = LoginLinkToken.FromRaw("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8"); // gitleaks:allow

        id.ToString().ShouldBe($"{id.Reveal()[..6]}…");
        link.ToString().ShouldBe("AAECAw…");
        LoginCode.FromRaw("042917").ToString().ShouldBe("…");
    }
}
