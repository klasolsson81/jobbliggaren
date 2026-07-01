using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobSeekers;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Abstractions;

/// <summary>
/// ADR 0087 D5 (#311 PR-4) — pins the determinism + stability of the company-follow Resend
/// <c>Idempotency-Key</c> derivation. Load-bearing invariants:
/// <see cref="FollowedCompanyNotificationIdempotencyKey.ForDigest"/> is a CONTENT fingerprint
/// (equal for the same claimed hit set in ANY order, different for any different set); the key is
/// namespaced <c>follow/v1/…</c> so it can NEVER collide with a match-notification key; it stays
/// within Resend's 1–256-char bound; an empty/null claimed set is rejected.
/// </summary>
public class FollowedCompanyNotificationIdempotencyKeyTests
{
    [Fact]
    public void ForDigest_SameSetAnyOrder_ProducesEqualKey()
    {
        var userId = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        var key1 = FollowedCompanyNotificationIdempotencyKey.ForDigest(
            userId, DigestCadence.Weekly, [a, b, c]);
        var key2 = FollowedCompanyNotificationIdempotencyKey.ForDigest(
            userId, DigestCadence.Weekly, [c, a, b]);

        key1.ShouldBe(key2);
        key1.Value.ShouldBe(key2.Value);
    }

    [Fact]
    public void ForDigest_DifferentSet_ProducesDifferentKey()
    {
        var userId = Guid.NewGuid();

        var key1 = FollowedCompanyNotificationIdempotencyKey.ForDigest(
            userId, DigestCadence.Weekly, [Guid.NewGuid()]);
        var key2 = FollowedCompanyNotificationIdempotencyKey.ForDigest(
            userId, DigestCadence.Weekly, [Guid.NewGuid()]);

        key1.ShouldNotBe(key2);
    }

    [Fact]
    public void ForDigest_DifferentCadence_ProducesDifferentKey()
    {
        var userId = Guid.NewGuid();
        var hitId = Guid.NewGuid();

        var daily = FollowedCompanyNotificationIdempotencyKey.ForDigest(
            userId, DigestCadence.Daily, [hitId]);
        var weekly = FollowedCompanyNotificationIdempotencyKey.ForDigest(
            userId, DigestCadence.Weekly, [hitId]);

        daily.ShouldNotBe(weekly);
    }

    [Fact]
    public void ForDigest_Value_IsNamespacedAndVersioned()
    {
        var key = FollowedCompanyNotificationIdempotencyKey.ForDigest(
            Guid.NewGuid(), DigestCadence.Weekly, [Guid.NewGuid()]);

        key.Value.ShouldStartWith("follow/v1/");
        key.Value.Length.ShouldBeLessThanOrEqualTo(256);
    }

    [Fact]
    public void ForDigest_DoesNotCollideWithMatchDigestKey()
    {
        // The two dispatch payloads must never share a Resend key (distinct namespaces).
        var userId = Guid.NewGuid();
        var id = Guid.NewGuid();

        var follow = FollowedCompanyNotificationIdempotencyKey.ForDigest(
            userId, DigestCadence.Weekly, [id]);
        var match = MatchNotificationIdempotencyKey.ForDigest(
            userId, DigestCadence.Weekly, [id]);

        follow.Value.ShouldNotBe(match.Value);
    }

    [Fact]
    public void ForDigest_EmptySet_Throws()
    {
        Should.Throw<ArgumentException>(() =>
            FollowedCompanyNotificationIdempotencyKey.ForDigest(
                Guid.NewGuid(), DigestCadence.Weekly, []));
    }

    [Fact]
    public void ForDigest_NullSet_Throws()
    {
        Should.Throw<ArgumentNullException>(() =>
            FollowedCompanyNotificationIdempotencyKey.ForDigest(
                Guid.NewGuid(), DigestCadence.Weekly, null!));
    }

    [Fact]
    public void Default_Value_IsNull_RejectedAtSendBoundary()
    {
        // A default-constructed struct carries no key; the Resend sender fails loud on it.
        var key = default(FollowedCompanyNotificationIdempotencyKey);
        key.Value.ShouldBeNull();
    }
}
