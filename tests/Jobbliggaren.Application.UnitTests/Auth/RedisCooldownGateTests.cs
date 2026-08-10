using System.Text;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;
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
            Arg.Is<DistributedCacheEntryOptions>(o => o != null &&
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
            .Returns(ci => { keys.Add(ci.ArgAt<string>(0)); return (byte[]?)null; });

        await Sut().TryBeginAsync(CooldownScopes.ResendConfirm, "User@Example.COM", TimeSpan.FromSeconds(60), ct);
        await Sut().TryBeginAsync(CooldownScopes.ResendConfirm, "  user@example.com  ", TimeSpan.FromSeconds(60), ct);

        keys.Count.ShouldBe(2);
        keys[0].ShouldBe(keys[1], "casing + surrounding whitespace must collapse to the same throttle key");
    }

    /// <summary>
    /// #1171 (security-auditor 2026-08-10) — the gate's normalisation must be IDENTITY'S, not merely
    /// "a" normalisation. Every subject is an email address, and a throttle only throttles if two
    /// spellings that reach the SAME ACCOUNT land on the SAME KEY.
    /// <para>
    /// The parity is asserted against <see cref="UpperInvariantLookupNormalizer"/> ITSELF rather than
    /// against a hand-written expectation, so the test cannot drift from what Identity actually does —
    /// and it is the reason this is a measurement rather than a restatement of the fix.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TryBeginAsync_CollapsesEverySpellingIdentityTreatsAsOneAccount()
    {
        // THE RULE, not an enumeration of the characters someone happened to find. The property is an
        // implication: if Identity resolves two spellings to ONE account, the gate must give them ONE
        // window. Swept over the whole BMP so a character nobody thought of cannot slip through.
        //
        // Enumerating was tried first, and that is what makes the sweep worth writing. The audit named
        // U+017F (long s, upper-cases to S). A probe run in PYTHON claimed U+0131 (dotless i) as a
        // second — and .NET disagrees: its invariant upper-casing leaves that character alone, so it is
        // NOT one account to Identity and never was a bypass. The probe measured the wrong runtime. A
        // sweep run in the runtime that ships cannot make that mistake.
        var ct = TestContext.Current.CancellationToken;
        var normalizer = new UpperInvariantLookupNormalizer();
        var sut = Sut();

        var offenders = new List<string>();
        var swept = 0;
        for (var cp = 0x21; cp <= 0xFFFF; cp++)
        {
            if (cp is >= 0xD800 and <= 0xDFFF) continue;      // lone surrogates are not text
            var c = (char)cp;
            var upper = char.ToUpperInvariant(c);
            if (upper == c) continue;                         // no case pair, nothing to collapse

            // Compare each character against ITS OWN upper-case form, never against one fixed probe
            // letter. A fixed letter is the trap this sweep exists to avoid: 'X' has no exotic aliases,
            // so a sweep keyed on it iterates over nothing and passes while measuring zero characters.
            var lowerForm = $"a{c}b@example.com";
            var upperForm = $"a{upper}b@example.com";

            // Only characters Identity actually folds are in scope. Asserting instead of assuming keeps
            // the sweep honest if Identity's normaliser ever changes.
            if (normalizer.NormalizeEmail(lowerForm) != normalizer.NormalizeEmail(upperForm)) continue;
            swept++;

            var keys = new List<string>();
            _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(ci => { keys.Add(ci.ArgAt<string>(0)); return (byte[]?)null; });

            await sut.TryBeginAsync(CooldownScopes.PasswordReset, upperForm, TimeSpan.FromSeconds(60), ct);
            await sut.TryBeginAsync(CooldownScopes.PasswordReset, lowerForm, TimeSpan.FromSeconds(60), ct);

            if (keys[0] != keys[1]) offenders.Add($"U+{cp:X4}");
        }

        offenders.ShouldBeEmpty(
            "every spelling Identity resolves to one account must share one throttle window - otherwise "
            + "an address with k such letters yields 2^k independent windows and the throttle is bypassed");

        // The counterfactual for the sweep ITSELF. Without it an empty offender list is satisfied by a
        // loop that iterated over nothing, which is exactly how the first draft of this test passed.
        swept.ShouldBeGreaterThan(500, "the sweep must actually have exercised the gate");
        char.ToUpperInvariant('ſ').ShouldBe('S', "U+017F is the character the audit measured");

        // And the correction above becomes a measurement rather than a sentence. If a future runtime
        // ever DID fold U+0131 onto 'I', this line falls and the comment stops being false in silence.
        char.ToUpperInvariant('ı').ShouldBe('ı',
            "U+0131 is NOT a bypass character in .NET - the Python probe that claimed it measured "
            + "another runtime's casing rules");
    }

    [Fact]
    public async Task TryBeginAsync_CollapsesADecomposedSpellingOntoItsComposedForm()
    {
        // The second axis, independent of casing: Identity runs Normalize() (NFC) before upper-casing,
        // so an NFD spelling of any accented address is the same account. Built from code points rather
        // than pasted, because an editor normalising this file would silently make the test vacuous.
        var ct = TestContext.Current.CancellationToken;
        const string composed = "bö@example.com";        // o-with-diaeresis, one code point
        const string decomposed = "bö@example.com";     // o + combining diaeresis
        composed.ShouldNotBe(decomposed, "the two spellings must actually differ before normalisation");

        var normalizer = new UpperInvariantLookupNormalizer();
        normalizer.NormalizeEmail(decomposed).ShouldBe(normalizer.NormalizeEmail(composed));

        var keys = new List<string>();
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => { keys.Add(ci.ArgAt<string>(0)); return (byte[]?)null; });

        await Sut().TryBeginAsync(CooldownScopes.PasswordReset, composed, TimeSpan.FromSeconds(60), ct);
        await Sut().TryBeginAsync(CooldownScopes.PasswordReset, decomposed, TimeSpan.FromSeconds(60), ct);

        keys.Count.ShouldBe(2);
        keys[0].ShouldBe(keys[1]);
    }

    /// <summary>
    /// The counterfactual. Without it the parity theory above is satisfied by a gate that hashes every
    /// subject to one constant key — which would "pass" while throttling the entire product to a single
    /// shared window.
    /// </summary>
    [Fact]
    public async Task TryBeginAsync_StillSeparatesGenuinelyDifferentSubjects()
    {
        var ct = TestContext.Current.CancellationToken;
        var keys = new List<string>();
        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => { keys.Add(ci.ArgAt<string>(0)); return (byte[]?)null; });

        await Sut().TryBeginAsync(CooldownScopes.PasswordReset, "a@example.com", TimeSpan.FromSeconds(60), ct);
        await Sut().TryBeginAsync(CooldownScopes.PasswordReset, "b@example.com", TimeSpan.FromSeconds(60), ct);

        keys.Count.ShouldBe(2);
        keys[0].ShouldNotBe(keys[1]);
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
            .Returns(ci => { keys.Add(ci.ArgAt<string>(0)); return (byte[]?)null; });

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

        // sha256("KLAS@EXAMPLE.COM") lower-hex, scope "resend-confirm", version v1.
        //
        // CHANGED 2026-08-10 (#1171). This test did exactly its job: the normalisation fix altered the
        // derived key and this assertion caught it, which is the whole reason a golden master exists.
        // The old value hashed the LOWER-cased subject; the gate now upper-cases as Identity's lookup
        // normaliser does, so two spellings of one account can no longer buy two windows. Updating the
        // expectation is correct HERE and only because the change was deliberate and reviewed — every
        // in-flight cooldown window resets once on deploy, which is harmless at 60 s. A future diff that
        // touches this line without a matching, argued change to Key() is a defect, not a rebase.
        capturedKey.ShouldBe(
            "cd/resend-confirm/v1/fac6ba54474a51ba1a02f54ae399a4998d235bcf2c44f1b3a04ffb4572ac70f8");
    }
}
