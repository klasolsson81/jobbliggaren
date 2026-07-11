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

    [Fact]
    public async Task TryBeginAsync_KeyIsStable_GoldenMasterForKnownScopeAndSubject()
    {
        // The key format "MUST NOT change once shipped" (RedisCooldownGate XML doc) — in-flight windows
        // would reset on deploy. Golden-master the EXACT key for a known (scope, subject): a hash-algorithm
        // swap, a dropped normalization, or a format change breaks THIS even though the prefix / scope /
        // normalization tests all still pass.
        var ct = TestContext.Current.CancellationToken;
        string? capturedKey = null;
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => { capturedKey = ci.Arg<string>(); return (byte[]?)null; });

        await Sut().TryBeginAsync(CooldownScopes.ResendConfirm, Subject, TimeSpan.FromSeconds(60), ct);

        // sha256("klas@example.com") lower-hex, scope "resend-confirm", version v1.
        capturedKey.ShouldBe(
            "cd/resend-confirm/v1/6e85b9891be626310e414a86b4bd050f51e6f7d0fb4fd3426695aeaa20e74e4f");
    }
}
