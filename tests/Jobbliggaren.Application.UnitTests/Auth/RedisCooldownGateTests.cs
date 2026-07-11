using System.Text;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Infrastructure.Auth;
using Microsoft.Extensions.Caching.Distributed;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #733/#703 — UNIT cover for the generalised per-subject cooldown gate primitive
/// (<see cref="RedisCooldownGate"/> / <c>ICooldownGate</c>). Redis is faked via
/// <see cref="IDistributedCache"/> (parity <c>CachedCompanyRegistryTests</c>; GetStringAsync/SetStringAsync
/// are extensions over GetAsync/SetAsync). Pins the throttle mechanics an integration test cannot
/// discriminate: begins + SETs with the CALLER-PASSED window when the key is absent; returns false without
/// SETting when present; the key is a one-way SHA-256 of the NORMALIZED subject (case + surrounding
/// whitespace collapse to the same key, and the raw value is never written to Redis); and the SCOPE
/// namespaces the key so the same subject under two scopes never collides.
/// </summary>
public class RedisCooldownGateTests
{
    private readonly IDistributedCache _cache = Substitute.For<IDistributedCache>();

    private RedisCooldownGate Sut() => new(_cache);

    private const string Subject = "klas@example.com";

    private void KeyAbsent() =>
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((byte[]?)null);

    [Fact]
    public async Task TryBeginAsync_WhenKeyAbsent_ReturnsTrue_AndSetsKeyWithPassedWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        KeyAbsent();

        (await Sut().TryBeginAsync(CooldownScopes.ResendConfirm, Subject, TimeSpan.FromSeconds(90), ct))
            .ShouldBeTrue();

        // The TTL is the CALLER-PASSED window (the impl no longer owns it): a wrong-unit / hardcoded TTL
        // would still pass an integration test as long as it were >= the test window.
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

        (await Sut().TryBeginAsync(CooldownScopes.ResendConfirm, Subject, TimeSpan.FromSeconds(60), ct))
            .ShouldBeFalse();

        await _cache.DidNotReceiveWithAnyArgs().SetAsync(
            Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryBeginAsync_NormalizesSubject_SameKeyForCasingAndWhitespace()
    {
        var ct = TestContext.Current.CancellationToken;
        var keys = new List<string>();
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => { keys.Add(ci.Arg<string>()); return (byte[]?)null; });

        await Sut().TryBeginAsync(CooldownScopes.ResendConfirm, "User@Example.COM", TimeSpan.FromSeconds(60), ct);
        await Sut().TryBeginAsync(CooldownScopes.ResendConfirm, "  user@example.com  ", TimeSpan.FromSeconds(60), ct);

        keys.Count.ShouldBe(2);
        keys[0].ShouldBe(keys[1], "casing + surrounding whitespace must collapse to the same throttle key");
    }

    [Fact]
    public async Task TryBeginAsync_KeyIsHashed_NeverContainsRawSubject_AndIsScopeNamespaced()
    {
        var ct = TestContext.Current.CancellationToken;
        string? capturedKey = null;
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => { capturedKey = ci.Arg<string>(); return (byte[]?)null; });

        await Sut().TryBeginAsync(CooldownScopes.ResendConfirm, Subject, TimeSpan.FromSeconds(60), ct);

        capturedKey.ShouldNotBeNull();
        capturedKey!.ShouldStartWith($"cd/{CooldownScopes.ResendConfirm}/v1/");
        capturedKey.ShouldNotContain("klas", Case.Insensitive);
    }

    [Fact]
    public async Task TryBeginAsync_SameSubjectDifferentScope_ProducesDifferentKeys()
    {
        // The scope namespaces the window: the same address under two actions (e.g. resend vs change-email
        // target) MUST NOT share a throttle, or one action would silence the other.
        var ct = TestContext.Current.CancellationToken;
        var keys = new List<string>();
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => { keys.Add(ci.Arg<string>()); return (byte[]?)null; });

        await Sut().TryBeginAsync(CooldownScopes.ResendConfirm, Subject, TimeSpan.FromSeconds(60), ct);
        await Sut().TryBeginAsync(CooldownScopes.ChangeEmailTarget, Subject, TimeSpan.FromSeconds(60), ct);

        keys.Count.ShouldBe(2);
        keys[0].ShouldNotBe(keys[1], "distinct scopes must never collide on one subject");
    }
}
