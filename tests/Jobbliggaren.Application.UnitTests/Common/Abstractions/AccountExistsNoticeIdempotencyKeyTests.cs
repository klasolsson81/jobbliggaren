using Jobbliggaren.Application.Common.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Abstractions;

/// <summary>
/// #714 — pins the registration ACCOUNT-EXISTS notice Resend <c>Idempotency-Key</c> derivation. Unlike
/// the confirmation key there is no token/userId available on the duplicate-swallow branch (it
/// deliberately does not look up the existing account), so the key is a SHA-256 of the NORMALIZED
/// recipient address. Load-bearing invariants: deterministic per address; case- and whitespace-
/// insensitive (so every attempt on the same taken address collapses to ONE key — the anti-email-bomb
/// property, CTO-bind Risk 6); namespaced/versioned (<c>account-exists-notice/v1/{sha256-hex}</c>) so it
/// can NEVER collide with any confirmation key; PII-free by construction (never the raw address);
/// within Resend's 1–256-char bound; a <c>default</c> struct carries a null Value rejected fail-loud at
/// the send boundary.
/// </summary>
public class AccountExistsNoticeIdempotencyKeyTests
{
    private const string Email = "klas@example.com";

    [Fact]
    public void For_SameAddress_ProducesEqualKey()
    {
        var a = AccountExistsNoticeIdempotencyKey.For(Email);
        var b = AccountExistsNoticeIdempotencyKey.For(Email);

        a.ShouldBe(b);
        a.Value.ShouldBe(b.Value);
    }

    [Fact]
    public void For_DifferentAddress_ProducesDifferentKey()
    {
        var a = AccountExistsNoticeIdempotencyKey.For("one@example.com");
        var b = AccountExistsNoticeIdempotencyKey.For("two@example.com");

        a.ShouldNotBe(b);
    }

    [Theory]
    [InlineData("KLAS@EXAMPLE.COM")]
    [InlineData("  klas@example.com  ")]
    [InlineData("Klas@Example.Com")]
    public void For_NormalizesCaseAndWhitespace_SoRepeatedAttemptsCollapseToOneKey(string variant)
    {
        // Anti-email-bomb: every register attempt on the same taken address (regardless of casing or
        // surrounding whitespace) must produce the SAME key so Resend delivers at most one notice per
        // dedup window.
        AccountExistsNoticeIdempotencyKey.For(variant)
            .ShouldBe(AccountExistsNoticeIdempotencyKey.For(Email));
    }

    [Fact]
    public void For_Value_IsNamespacedVersionedAndHashed()
    {
        var key = AccountExistsNoticeIdempotencyKey.For(Email);

        // account-exists-notice/v1/{SHA-256 = 64 hex} — never the raw address.
        key.Value.ShouldStartWith("account-exists-notice/v1/");
        key.Value.ShouldMatch("^account-exists-notice/v1/[0-9a-f]{64}$");
    }

    [Fact]
    public void For_Value_DoesNotContainTheRawAddress()
        // PII hygiene: the address is one-way hashed, never embedded verbatim (parity with the login
        // audit's email hashing).
        => AccountExistsNoticeIdempotencyKey.For(Email)
            .Value.ShouldNotContain(Email);

    [Fact]
    public void For_Value_StaysWithinResend256CharLimit()
    {
        var key = AccountExistsNoticeIdempotencyKey.For(new string('a', 500) + "@example.com");

        key.Value.Length.ShouldBeLessThanOrEqualTo(256);
        key.Value.ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void For_EmptyOrWhitespaceEmail_ThrowsArgumentException(string email)
        => Should.Throw<ArgumentException>(() => AccountExistsNoticeIdempotencyKey.For(email));

    [Fact]
    public void For_NullEmail_ThrowsArgumentNullException()
        => Should.Throw<ArgumentNullException>(() => AccountExistsNoticeIdempotencyKey.For(null!));

    [Fact]
    public void Default_Value_IsNull_RejectedAtSendBoundary()
    {
        // A default-constructed struct carries no key; the Resend sender fails loud on it.
        var key = default(AccountExistsNoticeIdempotencyKey);

        key.Value.ShouldBeNull();
    }
}
