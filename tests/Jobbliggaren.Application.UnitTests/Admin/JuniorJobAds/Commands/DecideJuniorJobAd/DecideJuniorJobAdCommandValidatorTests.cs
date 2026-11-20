using Jobbliggaren.Application.Admin.JuniorJobAds.Commands.DecideJuniorJobAd;
using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.JuniorJobAds.Commands.DecideJuniorJobAd;

/// <summary>
/// #2264 (ADR 0187) — the decision's shape: an ad id, and a decision that is one of the two defined values or
/// <see langword="null"/> (hand the ad back to the rules). Whether the ad is assessed, listed and in an enabled field is
/// live state, resolved in the handler.
/// </summary>
public class DecideJuniorJobAdCommandValidatorTests
{
    private readonly DecideJuniorJobAdCommandValidator _validator = new();

    [Theory]
    [InlineData(JuniorDecision.Junior)]
    [InlineData(JuniorDecision.NotJunior)]
    [InlineData(null)]
    public void Validate_ShouldPass_WhenTheIdIsSetAndTheDecisionIsDefinedOrNull(JuniorDecision? decision)
    {
        var result = _validator.Validate(new DecideJuniorJobAdCommand(Guid.NewGuid(), decision));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_ShouldFailOnTheId_WhenTheIdIsEmpty()
    {
        var result = _validator.Validate(new DecideJuniorJobAdCommand(Guid.Empty, JuniorDecision.Junior));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(DecideJuniorJobAdCommand.JobAdId));
    }

    // 0 sits below the enum's first value (Junior = 1), and 3 is Unclear's number in JuniorVerdict: a verdict is
    // never a decision.
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void Validate_ShouldFailOnTheDecision_WhenItIsNoDefinedValue(int value)
    {
        var result = _validator.Validate(new DecideJuniorJobAdCommand(Guid.NewGuid(), (JuniorDecision)value));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(DecideJuniorJobAdCommand.Decision));
    }
}
