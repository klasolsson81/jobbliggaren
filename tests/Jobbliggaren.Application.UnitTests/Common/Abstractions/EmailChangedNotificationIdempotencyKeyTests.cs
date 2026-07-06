using Jobbliggaren.Application.Common.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Abstractions;

/// <summary>
/// #679 (CTO-bind #4) — pins the old-address "your email was changed" security-notice Resend
/// <c>Idempotency-Key</c> derivation (parity with <c>EmailChangeConfirmationIdempotencyKeyTests</c>).
/// Load-bearing invariants: deterministic per (user, token); discriminates on user AND token;
/// namespaced/versioned (<c>change-email-notice/v1/{uid:N}/{sha256-hex}</c>) so it can never collide
/// with the confirmation key; PII-free (SHA-256 of the opaque token, never an email/personnummer);
/// within Resend's 1–256-char bound; a <c>default</c> struct carries a null Value.
/// </summary>
public class EmailChangedNotificationIdempotencyKeyTests
{
    private const string Token = "opaque-url-safe-token-abc"; // gitleaks:allow

    [Fact]
    public void For_SameUserAndToken_ProducesEqualKey()
    {
        var userId = Guid.NewGuid();

        var a = EmailChangedNotificationIdempotencyKey.For(userId, Token);
        var b = EmailChangedNotificationIdempotencyKey.For(userId, Token);

        a.ShouldBe(b);
        a.Value.ShouldBe(b.Value);
    }

    [Fact]
    public void For_DifferentToken_ProducesDifferentKey()
    {
        var userId = Guid.NewGuid();

        var a = EmailChangedNotificationIdempotencyKey.For(userId, "token-one");
        var b = EmailChangedNotificationIdempotencyKey.For(userId, "token-two");

        a.ShouldNotBe(b);
    }

    [Fact]
    public void For_DifferentUser_ProducesDifferentKey()
    {
        var a = EmailChangedNotificationIdempotencyKey.For(Guid.NewGuid(), Token);
        var b = EmailChangedNotificationIdempotencyKey.For(Guid.NewGuid(), Token);

        a.ShouldNotBe(b);
    }

    [Fact]
    public void For_Value_IsNamespacedVersionedAndHashed()
    {
        var key = EmailChangedNotificationIdempotencyKey.For(Guid.NewGuid(), Token);

        // change-email-notice/v1/{uid:N = 32 hex}/{SHA-256 = 64 hex} — a DISTINCT namespace from confirm.
        key.Value.ShouldStartWith("change-email-notice/v1/");
        key.Value.ShouldMatch("^change-email-notice/v1/[0-9a-f]{32}/[0-9a-f]{64}$");
    }

    [Fact]
    public void For_LongToken_StaysWithinResend256CharLimit()
    {
        var key = EmailChangedNotificationIdempotencyKey.For(Guid.NewGuid(), new string('t', 4096));

        key.Value.Length.ShouldBeLessThanOrEqualTo(256);
        key.Value.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void For_EmptyOrWhitespaceToken_ThrowsArgumentException(string token)
        => Should.Throw<ArgumentException>(
            () => EmailChangedNotificationIdempotencyKey.For(Guid.NewGuid(), token));

    [Fact]
    public void For_NullToken_ThrowsArgumentNullException()
        => Should.Throw<ArgumentNullException>(
            () => EmailChangedNotificationIdempotencyKey.For(Guid.NewGuid(), null!));

    [Fact]
    public void Default_Value_IsNull_RejectedAtSendBoundary()
    {
        var key = default(EmailChangedNotificationIdempotencyKey);

        key.Value.ShouldBeNull();
    }
}
