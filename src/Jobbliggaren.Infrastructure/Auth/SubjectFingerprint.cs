using System.Security.Cryptography;
using System.Text;

namespace Jobbliggaren.Infrastructure.Auth;

/// <summary>
/// The one home of the Redis-key fingerprint for a subject (an email address or a user id): the subject
/// normalised the way Identity normalises a lookup key, then SHA-256, lower-case hex. One-way — the raw
/// value is never written to Redis. Every Redis key derived from an address calls this function, and none
/// carries its own copy (ADR 0142 D1, security-auditor Major 3).
/// </summary>
internal static class SubjectFingerprint
{
    // The normalisation MUST be Identity's, not merely "a" normalisation (#1171, security-auditor
    // 2026-08-10). A key derived from an address is only meaningful if two spellings that reach the SAME
    // ACCOUNT land on the SAME KEY. Identity's lookup is UpperInvariantLookupNormalizer —
    // `Normalize().ToUpperInvariant()`, i.e. NFC then upper.
    //
    // U+017F LATIN SMALL LETTER LONG S (ſ) upper-cases to 'S' while lower-casing to itself, so a long-s
    // spelling passes the validator, resolves to the SAME Identity account as its ASCII spelling, and
    // under a lower-casing key got its OWN window: 2^k independent windows for an address with k such
    // letters. NFC closes a second axis, where a decomposed (NFD) spelling of any accented address did
    // the same.
    //
    // U+0131 DOTLESS I (ı) is NOT such a character and never was a bypass - .NET's invariant upper-casing
    // leaves it alone. It is named here because a probe run in Python said otherwise and that claim
    // reached a draft; the sweep in SubjectFingerprintTests measures the property in the runtime that
    // SHIPS rather than trusting either list.
    //
    // `ToUpperInvariant` rather than `ToLower` deliberately: it is what Identity does, and the Turkish-I
    // hazard runs the other way.
    internal static string Hex(string subject)
    {
        var normalized = subject.Trim().Normalize().ToUpperInvariant();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}
