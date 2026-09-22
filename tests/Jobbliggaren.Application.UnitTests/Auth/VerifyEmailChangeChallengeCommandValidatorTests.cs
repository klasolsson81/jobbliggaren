using Jobbliggaren.Application.Auth.Commands.VerifyEmailChangeChallenge;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// The change-email verify step's validator (#1739): a malformed request is a clean 400 before an attempt is
/// spent.
/// </summary>
public sealed class VerifyEmailChangeChallengeCommandValidatorTests
{
    private readonly VerifyEmailChangeChallengeCommandValidator _verify = new();

    [Fact]
    public void A_challenge_id_and_a_six_digit_code_pass()
        => _verify.Validate(new VerifyEmailChangeChallengeCommand("AAECAwQFBgcICQoLDA0ODw", "042917"))
            .IsValid.ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("04291")]
    [InlineData("0429170")]
    [InlineData("04291a")]
    [InlineData("042917\n")]
    public void A_code_of_any_other_shape_fails(string? code)
    {
        var result = _verify.Validate(new VerifyEmailChangeChallengeCommand("AAECAwQFBgcICQoLDA0ODw", code));

        result.Errors.ShouldContain(e => e.PropertyName == nameof(VerifyEmailChangeChallengeCommand.Code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_missing_challenge_id_fails(string? challengeId)
    {
        var result = _verify.Validate(new VerifyEmailChangeChallengeCommand(challengeId, "042917"));

        result.Errors.ShouldContain(e => e.PropertyName == nameof(VerifyEmailChangeChallengeCommand.ChallengeId));
    }

    [Fact]
    public void A_challenge_id_longer_than_any_minted_one_fails()
    {
        var result = _verify.Validate(new VerifyEmailChangeChallengeCommand(new string('A', 65), "042917"));

        result.Errors.ShouldContain(e => e.PropertyName == nameof(VerifyEmailChangeChallengeCommand.ChallengeId));
    }
}
