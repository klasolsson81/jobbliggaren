using Jobbliggaren.Application.Common.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Abstractions;

/// <summary>
/// #679 — pins the change-email CONFIRMATION Resend <c>Idempotency-Key</c> derivation (parity with
/// <c>MatchNotificationIdempotencyKeyTests</c>). Load-bearing invariants: deterministic per
/// (user, token); discriminates on user AND token; namespaced/versioned
/// (<c>change-email-confirm/v1/{uid:N}/{sha256-hex}</c>) so it can NEVER collide with the old-address
/// notice key; PII-free by construction (a SHA-256 of the opaque token, never the email address or a
/// personnummer); within Resend's 1–256-char bound regardless of token length; a <c>default</c> struct
/// carries a null Value that is rejected fail-loud at the send boundary.
/// </summary>
public class EmailChangeConfirmationIdempotencyKeyTests
{
    private const string Token = "opaque-url-safe-token-abc"; // gitleaks:allow

    [Fact]
    public void For_SameUserAndToken_ProducesEqualKey()
    {
        var userId = Guid.NewGuid();

        var a = EmailChangeConfirmationIdempotencyKey.For(userId, Token);
        var b = EmailChangeConfirmationIdempotencyKey.For(userId, Token);

        a.ShouldBe(b);
        a.Value.ShouldBe(b.Value);
    }

    [Fact]
    public void For_DifferentToken_ProducesDifferentKey()
    {
        var userId = Guid.NewGuid();

        var a = EmailChangeConfirmationIdempotencyKey.For(userId, "token-one");
        var b = EmailChangeConfirmationIdempotencyKey.For(userId, "token-two");

        a.ShouldNotBe(b);
    }

    [Fact]
    public void For_DifferentUser_ProducesDifferentKey()
    {
        var a = EmailChangeConfirmationIdempotencyKey.For(Guid.NewGuid(), Token);
        var b = EmailChangeConfirmationIdempotencyKey.For(Guid.NewGuid(), Token);

        a.ShouldNotBe(b);
    }

    [Fact]
    public void For_Value_IsNamespacedVersionedAndHashed()
    {
        var key = EmailChangeConfirmationIdempotencyKey.For(Guid.NewGuid(), Token);

        // change-email-confirm/v1/{uid:N = 32 hex}/{SHA-256 = 64 hex} — never the raw token or the email.
        key.Value.ShouldStartWith("change-email-confirm/v1/");
        key.Value.ShouldMatch("^change-email-confirm/v1/[0-9a-f]{32}/[0-9a-f]{64}$");
    }

    [Fact]
    public void For_LongToken_StaysWithinResend256CharLimit()
    {
        // A very long token still hashes to a fixed-length digest, so the key length is bounded.
        var key = EmailChangeConfirmationIdempotencyKey.For(Guid.NewGuid(), new string('t', 4096));

        key.Value.Length.ShouldBeLessThanOrEqualTo(256);
        key.Value.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void For_EmptyOrWhitespaceToken_ThrowsArgumentException(string token)
        => Should.Throw<ArgumentException>(
            () => EmailChangeConfirmationIdempotencyKey.For(Guid.NewGuid(), token));

    [Fact]
    public void For_NullToken_ThrowsArgumentNullException()
        => Should.Throw<ArgumentNullException>(
            () => EmailChangeConfirmationIdempotencyKey.For(Guid.NewGuid(), null!));

    [Fact]
    public void ConfirmationAndNotice_ForSameUserAndToken_NeverCollide()
    {
        // The confirmation send and the old-address notice belong to the SAME change-email flow (same
        // user + token). Distinct namespaces must keep their Resend keys apart, or the second send would
        // 409 as a duplicate payload.
        var userId = Guid.NewGuid();

        var confirm = EmailChangeConfirmationIdempotencyKey.For(userId, Token);
        var notice = EmailChangedNotificationIdempotencyKey.For(userId, Token);

        confirm.Value.ShouldNotBe(notice.Value);
    }

    [Fact]
    public void Default_Value_IsNull_RejectedAtSendBoundary()
    {
        // A default-constructed struct carries no key; the Resend sender fails loud on it.
        var key = default(EmailChangeConfirmationIdempotencyKey);

        key.Value.ShouldBeNull();
    }
}
