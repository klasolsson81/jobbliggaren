using Jobbliggaren.Application.Common.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Abstractions;

/// <summary>
/// #714 — pins the registration email-confirmation Resend <c>Idempotency-Key</c> derivation (parity
/// with <c>EmailChangeConfirmationIdempotencyKeyTests</c>). Load-bearing invariants: deterministic per
/// (user, token); discriminates on user AND token; namespaced/versioned
/// (<c>email-confirm/v1/{uid:N}/{sha256-hex}</c>) so it can NEVER collide with the change-email
/// confirmation, the change-email notice, or the account-exists notice key; PII-free by construction
/// (a SHA-256 of the opaque token, never the email address or a personnummer); within Resend's
/// 1–256-char bound regardless of token length; a <c>default</c> struct carries a null Value that is
/// rejected fail-loud at the send boundary.
/// </summary>
public class EmailConfirmationIdempotencyKeyTests
{
    private const string Token = "opaque-url-safe-token-abc"; // gitleaks:allow

    [Fact]
    public void For_SameUserAndToken_ProducesEqualKey()
    {
        var userId = Guid.NewGuid();

        var a = EmailConfirmationIdempotencyKey.For(userId, Token);
        var b = EmailConfirmationIdempotencyKey.For(userId, Token);

        a.ShouldBe(b);
        a.Value.ShouldBe(b.Value);
    }

    [Fact]
    public void For_DifferentToken_ProducesDifferentKey()
    {
        var userId = Guid.NewGuid();

        var a = EmailConfirmationIdempotencyKey.For(userId, "token-one");
        var b = EmailConfirmationIdempotencyKey.For(userId, "token-two");

        a.ShouldNotBe(b);
    }

    [Fact]
    public void For_DifferentUser_ProducesDifferentKey()
    {
        var a = EmailConfirmationIdempotencyKey.For(Guid.NewGuid(), Token);
        var b = EmailConfirmationIdempotencyKey.For(Guid.NewGuid(), Token);

        a.ShouldNotBe(b);
    }

    [Fact]
    public void For_Value_IsNamespacedVersionedAndHashed()
    {
        var key = EmailConfirmationIdempotencyKey.For(Guid.NewGuid(), Token);

        // email-confirm/v1/{uid:N = 32 hex}/{SHA-256 = 64 hex} — never the raw token or the email.
        key.Value.ShouldStartWith("email-confirm/v1/");
        key.Value.ShouldMatch("^email-confirm/v1/[0-9a-f]{32}/[0-9a-f]{64}$");
    }

    [Fact]
    public void For_Value_DoesNotContainTheRawToken()
        // PII/secret hygiene: the opaque token is hashed, never embedded verbatim.
        => EmailConfirmationIdempotencyKey.For(Guid.NewGuid(), Token)
            .Value.ShouldNotContain(Token);

    [Fact]
    public void For_LongToken_StaysWithinResend256CharLimit()
    {
        // A very long token still hashes to a fixed-length digest, so the key length is bounded.
        var key = EmailConfirmationIdempotencyKey.For(Guid.NewGuid(), new string('t', 4096));

        key.Value.Length.ShouldBeLessThanOrEqualTo(256);
        key.Value.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void For_EmptyOrWhitespaceToken_ThrowsArgumentException(string token)
        => Should.Throw<ArgumentException>(
            () => EmailConfirmationIdempotencyKey.For(Guid.NewGuid(), token));

    [Fact]
    public void For_NullToken_ThrowsArgumentNullException()
        => Should.Throw<ArgumentNullException>(
            () => EmailConfirmationIdempotencyKey.For(Guid.NewGuid(), null!));

    [Fact]
    public void EmailConfirmAndChangeEmailConfirm_ForSameUserAndToken_NeverCollide()
    {
        // Distinct namespaces keep the registration-confirm key apart from the change-email-confirm key
        // even for the identical (user, token) inputs, or a send would 409 as a duplicate payload.
        var userId = Guid.NewGuid();

        var registrationConfirm = EmailConfirmationIdempotencyKey.For(userId, Token);
        var changeEmailConfirm = EmailChangeConfirmationIdempotencyKey.For(userId, Token);

        registrationConfirm.Value.ShouldNotBe(changeEmailConfirm.Value);
    }

    [Fact]
    public void Default_Value_IsNull_RejectedAtSendBoundary()
    {
        // A default-constructed struct carries no key; the Resend sender fails loud on it.
        var key = default(EmailConfirmationIdempotencyKey);

        key.Value.ShouldBeNull();
    }
}
