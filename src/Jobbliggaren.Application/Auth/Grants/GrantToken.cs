using System.Buffers.Text;
using System.Security.Cryptography;

namespace Jobbliggaren.Application.Auth.Grants;

/// <summary>
/// A grant's bearer token: 128 bits from the CSPRNG, Base64Url. It shares <c>SessionId</c>'s shape — the value
/// is private, <see cref="Reveal"/> hands it out on purpose, and <see cref="ToString"/> prints a six-character
/// prefix, so an interpolated log line or a failed assertion never prints a live token in full.
/// </summary>
public readonly record struct GrantToken
{
    public const int ByteLength = 16;

    private readonly string _value = string.Empty;

    private GrantToken(string value) => _value = value;

    public string Reveal() => _value;

    public override string ToString() => _value is { Length: >= 6 } ? $"{_value[..6]}…" : "…";

    public static GrantToken Generate() =>
        new(Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ByteLength)));

    public static GrantToken FromRaw(string raw) => new(raw);
}
