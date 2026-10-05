using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jobbliggaren.Application.Auth.LoginChallenges;

namespace Jobbliggaren.Infrastructure.Auth.LoginChallenges;

/// <summary>
/// The code arm every code-bearing record on the volatile instance shares (ADR 0142 D1): how a code is minted, the
/// script that counts an attempt before anything is compared, and the stand-in a miss is compared against. One home,
/// so the stores that use it cannot drift apart on a security mechanic.
/// </summary>
internal static class ChallengeCodeArm
{
    // One atomic step: the existence guard keeps HINCRBY from recreating an expired or unknown record without
    // a TTL (a client chooses the key), and the increment happens BEFORE any compare, so a parallel burst of
    // guesses gets distinct attempt numbers.
    internal const string ConsumeScript = """
        if redis.call('EXISTS', KEYS[1]) == 0 then return false end
        local n = redis.call('HINCRBY', KEYS[1], 'a', 1)
        return { n, redis.call('HGET', KEYS[1], 'p') }
        """;

    // Compared against when there is nothing real to compare, so every path pays a fixed-time compare of the same
    // length.
    internal static readonly byte[] DummyCode = Encoding.ASCII.GetBytes(new string('0', LoginChallengePolicy.CodeLength));

    private static readonly int CodeSpace = (int)Math.Pow(10, LoginChallengePolicy.CodeLength);

    // RandomNumberGenerator.GetInt32 draws uniformly over the range (the runtime rejects biased samples), so
    // no `% 1_000_000` skew exists to correct for (ADR 0142 D10).
    internal static LoginCode Mint() =>
        LoginCode.FromRaw(RandomNumberGenerator.GetInt32(0, CodeSpace)
            .ToString("D" + LoginChallengePolicy.CodeLength, CultureInfo.InvariantCulture));
}
