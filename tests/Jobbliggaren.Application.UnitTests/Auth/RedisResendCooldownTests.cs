using System.Text;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Infrastructure.Auth;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #733 — UNIT cover for the per-target resend cooldown primitive. Redis is faked via
/// <see cref="IDistributedCache"/> (parity <c>CachedCompanyRegistryTests</c>; GetStringAsync/SetStringAsync
/// are extensions over GetAsync/SetAsync). Pins the throttle mechanics an integration test cannot
/// discriminate: begins + SETs with the OPTIONS TTL when the key is absent; returns false without SETting
/// when present; the key is a one-way SHA-256 of the NORMALIZED email (case + surrounding whitespace
/// collapse to the same key, and the raw address is never written to Redis).
/// </summary>
public class RedisResendCooldownTests
{
    private readonly IDistributedCache _cache = Substitute.For<IDistributedCache>();

    private RedisResendCooldown Sut(int windowSeconds = 60) =>
        new(_cache, Options.Create(new ResendCooldownOptions { WindowSeconds = windowSeconds }));

    private const string Email = "klas@example.com";

    private void KeyAbsent() =>
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((byte[]?)null);

    [Fact]
    public async Task TryBeginAsync_WhenKeyAbsent_ReturnsTrue_AndSetsKeyWithOptionsTtl()
    {
        var ct = TestContext.Current.CancellationToken;
        KeyAbsent();

        (await Sut(windowSeconds: 90).TryBeginAsync(Email, ct)).ShouldBeTrue();

        // TTL comes from the options, not a hardcoded constant — a wrong-unit / hardcoded TTL would still
        // pass the integration test as long as it were >= the test window.
        await _cache.Received(1).SetAsync(
            Arg.Any<string>(),
            Arg.Any<byte[]>(),
            Arg.Is<DistributedCacheEntryOptions>(o =>
                o.AbsoluteExpirationRelativeToNow == TimeSpan.FromSeconds(90)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryBeginAsync_WhenKeyPresent_ReturnsFalse_AndDoesNotSet()
    {
        var ct = TestContext.Current.CancellationToken;
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Encoding.UTF8.GetBytes("1"));

        (await Sut().TryBeginAsync(Email, ct)).ShouldBeFalse();

        await _cache.DidNotReceiveWithAnyArgs().SetAsync(
            Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryBeginAsync_NormalizesEmail_SameKeyForCasingAndWhitespace()
    {
        var ct = TestContext.Current.CancellationToken;
        var keys = new List<string>();
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => { keys.Add(ci.Arg<string>()); return (byte[]?)null; });

        await Sut().TryBeginAsync("User@Example.COM", ct);
        await Sut().TryBeginAsync("  user@example.com  ", ct);

        keys.Count.ShouldBe(2);
        keys[0].ShouldBe(keys[1], "casing + surrounding whitespace must collapse to the same throttle key");
    }

    [Fact]
    public async Task TryBeginAsync_KeyIsHashed_NeverContainsRawEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        string? capturedKey = null;
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => { capturedKey = ci.Arg<string>(); return (byte[]?)null; });

        await Sut().TryBeginAsync(Email, ct);

        capturedKey.ShouldNotBeNull();
        capturedKey!.ShouldStartWith("resend-confirm-cd/v1/");
        capturedKey.ShouldNotContain("klas", Case.Insensitive);
    }
}
