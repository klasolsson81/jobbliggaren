using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobSeekers;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Abstractions;

/// <summary>
/// ADR 0080 Vag 4 PR-4 item 4 (#187) — pins the determinism + stability of the Resend
/// <c>Idempotency-Key</c> derivation at the value-object level (the test-pyramid base; no HTTP, no
/// fake sender). The load-bearing invariants:
/// <list type="bullet">
/// <item><see cref="MatchNotificationIdempotencyKey.ForDirect"/> is deterministic (same user+ad →
///   equal key) and discriminates on both components;</item>
/// <item><see cref="MatchNotificationIdempotencyKey.ForDigest"/> is a CONTENT fingerprint — equal
///   for the same claimed set in ANY order, different for any different set — so a reused key never
///   carries a different payload (Resend 409 <c>invalid_idempotent_request</c>);</item>
/// <item>Direct and Digest keys can never collide (distinct namespaces);</item>
/// <item>both forms stay within Resend's 1–256-char bound;</item>
/// <item>an empty/null claimed set is rejected (the digest never sends for an empty window).</item>
/// </list>
/// </summary>
public class MatchNotificationIdempotencyKeyTests
{
    // ───────────────────────────── ForDirect — deterministic + discriminating

    [Fact]
    public void ForDirect_SameUserAndAd_ProducesEqualKey()
    {
        var userId = Guid.NewGuid();
        var jobAdId = Guid.NewGuid();

        var a = MatchNotificationIdempotencyKey.ForDirect(userId, jobAdId);
        var b = MatchNotificationIdempotencyKey.ForDirect(userId, jobAdId);

        a.ShouldBe(b);
        a.Value.ShouldBe(b.Value);
    }

    [Fact]
    public void ForDirect_DifferentAd_ProducesDifferentKey()
    {
        var userId = Guid.NewGuid();

        var a = MatchNotificationIdempotencyKey.ForDirect(userId, Guid.NewGuid());
        var b = MatchNotificationIdempotencyKey.ForDirect(userId, Guid.NewGuid());

        a.ShouldNotBe(b);
    }

    [Fact]
    public void ForDirect_DifferentUser_ProducesDifferentKey()
    {
        var jobAdId = Guid.NewGuid();

        var a = MatchNotificationIdempotencyKey.ForDirect(Guid.NewGuid(), jobAdId);
        var b = MatchNotificationIdempotencyKey.ForDirect(Guid.NewGuid(), jobAdId);

        a.ShouldNotBe(b);
    }

    [Fact]
    public void ForDirect_Value_IsNamespacedAndVersioned()
    {
        var key = MatchNotificationIdempotencyKey.ForDirect(Guid.NewGuid(), Guid.NewGuid());

        key.Value.ShouldStartWith("direct/v1/");
    }

    // ───────────────────────────── ForDigest — content fingerprint (order-independent)

    [Fact]
    public void ForDigest_SameSetInDifferentOrder_ProducesEqualKey()
    {
        var userId = Guid.NewGuid();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var reversed = ids.Reverse().ToArray();

        var a = MatchNotificationIdempotencyKey.ForDigest(userId, DigestCadence.Weekly, ids);
        var b = MatchNotificationIdempotencyKey.ForDigest(userId, DigestCadence.Weekly, reversed);

        a.ShouldBe(b, "the key is a content fingerprint — claim ordering must not change it");
    }

    [Fact]
    public void ForDigest_DifferentSet_ProducesDifferentKey()
    {
        var userId = Guid.NewGuid();
        var shared = Guid.NewGuid();

        var a = MatchNotificationIdempotencyKey.ForDigest(
            userId, DigestCadence.Weekly, [shared, Guid.NewGuid()]);
        var b = MatchNotificationIdempotencyKey.ForDigest(
            userId, DigestCadence.Weekly, [shared, Guid.NewGuid()]);

        a.ShouldNotBe(b);
    }

    [Fact]
    public void ForDigest_AddingAMatchToTheSet_ProducesDifferentKey()
    {
        var userId = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var one = MatchNotificationIdempotencyKey.ForDigest(userId, DigestCadence.Weekly, [first]);
        var two = MatchNotificationIdempotencyKey.ForDigest(
            userId, DigestCadence.Weekly, [first, second]);

        one.ShouldNotBe(two, "a superset is a different payload → must be a different key");
    }

    [Fact]
    public void ForDigest_DifferentCadence_ProducesDifferentKey()
    {
        var userId = Guid.NewGuid();
        var ids = new[] { Guid.NewGuid() };

        var daily = MatchNotificationIdempotencyKey.ForDigest(userId, DigestCadence.Daily, ids);
        var weekly = MatchNotificationIdempotencyKey.ForDigest(userId, DigestCadence.Weekly, ids);

        daily.ShouldNotBe(weekly);
    }

    [Fact]
    public void ForDigest_DifferentUser_ProducesDifferentKey()
    {
        var ids = new[] { Guid.NewGuid() };

        var a = MatchNotificationIdempotencyKey.ForDigest(Guid.NewGuid(), DigestCadence.Weekly, ids);
        var b = MatchNotificationIdempotencyKey.ForDigest(Guid.NewGuid(), DigestCadence.Weekly, ids);

        a.ShouldNotBe(b);
    }

    [Fact]
    public void ForDigest_Value_IsNamespacedAndVersioned()
    {
        var key = MatchNotificationIdempotencyKey.ForDigest(
            Guid.NewGuid(), DigestCadence.Weekly, [Guid.NewGuid()]);

        key.Value.ShouldStartWith("digest/v1/");
    }

    [Fact]
    public void ForDigest_EmptySet_Throws()
    {
        Should.Throw<ArgumentException>(() => MatchNotificationIdempotencyKey.ForDigest(
            Guid.NewGuid(), DigestCadence.Weekly, []));
    }

    [Fact]
    public void ForDigest_NullSet_Throws()
    {
        Should.Throw<ArgumentNullException>(() => MatchNotificationIdempotencyKey.ForDigest(
            Guid.NewGuid(), DigestCadence.Weekly, null!));
    }

    // ───────────────────────────── Cross-kind + Resend bound

    [Fact]
    public void DirectAndDigest_ForTheSameUser_NeverCollide()
    {
        var userId = Guid.NewGuid();
        var jobAdId = Guid.NewGuid();

        var direct = MatchNotificationIdempotencyKey.ForDirect(userId, jobAdId);
        var digest = MatchNotificationIdempotencyKey.ForDigest(
            userId, DigestCadence.Weekly, [jobAdId]);

        direct.ShouldNotBe(digest, "distinct namespaces keep the two send kinds from ever colliding");
    }

    [Fact]
    public void BothForms_StayWithinResend256CharLimit()
    {
        var userId = Guid.NewGuid();
        var direct = MatchNotificationIdempotencyKey.ForDirect(userId, Guid.NewGuid());
        // A generous claimed set still produces a fixed-length SHA-256 digest, so length is bounded.
        var manyIds = Enumerable.Range(0, 200).Select(_ => Guid.NewGuid()).ToArray();
        var digest = MatchNotificationIdempotencyKey.ForDigest(userId, DigestCadence.Weekly, manyIds);

        direct.Value.Length.ShouldBeLessThanOrEqualTo(256);
        digest.Value.Length.ShouldBeLessThanOrEqualTo(256);
        direct.Value.ShouldNotBeNullOrWhiteSpace();
        digest.Value.ShouldNotBeNullOrWhiteSpace();
    }
}
