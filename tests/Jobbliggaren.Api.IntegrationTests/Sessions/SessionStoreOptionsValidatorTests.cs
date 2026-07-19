using Jobbliggaren.Infrastructure.Auth.Sessions;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Sessions;

// #746: the sliding-write throttle ceiling is enforced at startup (ValidateOnStart), not by
// discipline. A SlideThreshold outside [0.0, MaxSlideThreshold] must fail validation — and thus the
// boot — so a config typo can never widen the Art.17 orphan self-heal window past the ceiling.
public class SessionStoreOptionsValidatorTests
{
    private readonly SessionStoreOptionsValidator _validator = new();

    [Theory]
    [InlineData(0.0)] // disabled — the shipped default
    [InlineData(0.1)] // the intended production starting point
    [InlineData(SessionStoreOptionsValidator.MaxSlideThreshold)] // 0.25 ceiling, inclusive
    public void Validate_ShouldSucceed_WhenSlideThresholdInRange(double threshold)
    {
        var result = _validator.Validate(name: null, new SessionStoreOptions { SlideThreshold = threshold });

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(-0.01)] // negative
    [InlineData(0.26)] // just over the ceiling
    [InlineData(0.9)] // the foot-gun the CTO named (would blow a 27d self-heal window on Persistent)
    [InlineData(1.0)]
    public void Validate_ShouldFail_WhenSlideThresholdOutOfRange(double threshold)
    {
        var result = _validator.Validate(name: null, new SessionStoreOptions { SlideThreshold = threshold });

        result.Failed.ShouldBeTrue();
    }
}
