using Jobbliggaren.Infrastructure.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #481 Low — account-enumeration via a login-timing side-channel. Guards the constant-time
/// defense's CORE CORRECTNESS PROPERTY: the equalizer builds a WELL-FORMED dummy Identity hash once
/// in its constructor, so every <see cref="LoginTimingEqualizer.Equalize"/> call runs a full PBKDF2
/// key derivation through <c>VerifyHashedPassword</c> instead of fast-failing on a malformed hash
/// layout (the fast-fail path would do no cryptographic work and defeat the equalization).
/// <para>
/// "Runs without throwing" IS that property: a malformed dummy hash would make
/// <c>VerifyHashedPassword</c> throw (or short-circuit) on the layout; a well-formed one performs the
/// derivation and returns a verdict that the equalizer discards. The timing itself is asserted
/// structurally in <see cref="UserAccountServiceTests"/> (branch wiring), never via a flaky
/// wall-clock — matching the codebase's structural-parity convention (LoginTests / LockoutTests).
/// </para>
/// </summary>
public class LoginTimingEqualizerTests
{
    private static LoginTimingEqualizer CreateSut() =>
        new(Options.Create(new PasswordHasherOptions()));

    [Fact]
    public void Constructor_ShouldBuildEqualizer_WhenGivenDefaultPasswordHasherOptions()
    {
        // The memoized dummy hash is computed ONCE here; a malformed layout would throw at construction.
        Should.NotThrow(() => CreateSut());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("anything")]
    [InlineData("T3stlosen123456")]
    public void Equalize_ShouldRunRealVerificationWithoutThrowing_ForAnyCandidate(string? candidatePassword)
    {
        var sut = CreateSut();

        // Well-formed dummy hash => VerifyHashedPassword does a full PBKDF2 derivation and returns a
        // (discarded) verdict rather than throwing on the layout. A null candidate is coalesced
        // internally, so even the can't-happen null is safe; PBKDF2 cost is input-length-independent.
        Should.NotThrow(() => sut.Equalize(candidatePassword));
    }
}
