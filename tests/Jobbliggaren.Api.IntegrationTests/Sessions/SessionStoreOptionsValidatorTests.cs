using Jobbliggaren.Infrastructure.Auth.Sessions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
    [InlineData(double.NaN)] // NaN is neither < 0 nor > Max — must be rejected, else TimeSpan * NaN throws per-read
    [InlineData(double.PositiveInfinity)]
    public void Validate_ShouldFail_WhenSlideThresholdOutOfRange(double threshold)
    {
        var result = _validator.Validate(name: null, new SessionStoreOptions { SlideThreshold = threshold });

        result.Failed.ShouldBeTrue();
    }

    // The guarantee itself is tested (not just the validator in isolation): the options-pipeline
    // wiring (AddOptions().Bind().ValidateOnStart() + the registered IValidateOptions) must actually
    // reject a bad value. Resolving IOptions<>.Value runs that pipeline, so an out-of-range
    // SlideThreshold surfaces as OptionsValidationException — and via ValidateOnStart, fails the boot.
    [Fact]
    public void Resolving_SessionStoreOptions_ShouldThrow_WhenSlideThresholdOutOfRange()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Session:SlideThreshold"] = "0.9" })
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<SessionStoreOptions>()
            .Bind(config.GetSection(SessionStoreOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SessionStoreOptions>, SessionStoreOptionsValidator>();

        using var sp = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(
            () => sp.GetRequiredService<IOptions<SessionStoreOptions>>().Value);
    }

    // The mirror: a valid (default) value resolves cleanly through the same wiring.
    [Fact]
    public void Resolving_SessionStoreOptions_ShouldSucceed_WhenSlideThresholdDefault()
    {
        var services = new ServiceCollection();
        services.AddOptions<SessionStoreOptions>()
            .Bind(new ConfigurationBuilder().Build().GetSection(SessionStoreOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SessionStoreOptions>, SessionStoreOptionsValidator>();

        using var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<IOptions<SessionStoreOptions>>().Value;
        options.SlideThreshold.ShouldBe(0.0); // unset → default → throttle off
    }
}
