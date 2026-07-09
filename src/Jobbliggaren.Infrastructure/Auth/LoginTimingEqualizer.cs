using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// Singleton timing-equalizer for the login flow (#481 Low — user-enumeration via a timing
/// side-channel). It owns its OWN <see cref="PasswordHasher{TUser}"/> built from
/// <see cref="PasswordHasherOptions"/> rather than injecting the Identity-registered
/// <c>IPasswordHasher&lt;ApplicationUser&gt;</c>, which is registered SCOPED — injecting a scoped
/// service into this singleton would be a captive dependency (a <c>ValidateOnBuild</c> failure).
/// Building an equivalent hasher from the same options is cost-faithful (the PBKDF2 work is a
/// function of the options, not the injected instance) — CTO-bind #3. This equalization assumes
/// Identity's stock PBKDF2 <see cref="PasswordHasher{TUser}"/>; if a custom
/// <c>IPasswordHasher&lt;ApplicationUser&gt;</c> (e.g. Argon2) is ever registered, the real
/// credential-check cost would diverge from this stock cost and the timing defence would weaken
/// silently — revisit this equalizer then.
/// <para>
/// The dummy hash is computed ONCE in the constructor and reused for every call. It is a well-formed
/// Identity hash, so <see cref="PasswordHasher{TUser}.VerifyHashedPassword"/> performs a full PBKDF2
/// key derivation instead of fast-failing on a malformed layout — the fast-fail path would do no work
/// and defeat the equalization.
/// </para>
/// </summary>
public sealed class LoginTimingEqualizer : ILoginTimingEqualizer
{
    // The stock PasswordHasher ignores the user argument; a throwaway instance is fine and shared.
    private static readonly ApplicationUser DummyUser = new();

    private readonly PasswordHasher<ApplicationUser> _hasher;
    private readonly string _dummyHash;

    public LoginTimingEqualizer(IOptions<PasswordHasherOptions> options)
    {
        _hasher = new PasswordHasher<ApplicationUser>(options);

        // A fixed filler that is never a user password → a well-formed hash whose derivation cost
        // equals a real one. The salt is baked in here once; verifying against it always performs the
        // same PBKDF2 work regardless of the candidate password.
        _dummyHash = _hasher.HashPassword(DummyUser, "login-timing-equalizer-fixed-filler");
    }

    public void Equalize(string? candidatePassword)
    {
        // Discard the verdict (the filler can never match a real password), and the discard cannot be
        // dead-code-eliminated across assemblies, so the PBKDF2 derivation always runs. A null
        // candidate (can't happen after the login validator's NotEmpty rule) is coalesced so the
        // verify never throws; PBKDF2 cost is independent of the input length either way.
        _ = _hasher.VerifyHashedPassword(DummyUser, _dummyHash, candidatePassword ?? string.Empty);
    }
}
