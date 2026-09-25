using System.Diagnostics;

namespace Jobbliggaren.Application.Auth.ExternalLogins;

/// <summary>
/// An identity provider's key (ADR 0142 D8): the route segment, the <c>AspNetUserLogins.login_provider</c> value and
/// the providers list's entry. A closed set, so no literal key is spelled anywhere else, and an unknown key never
/// parses. Which of these a host has REGISTERED is a separate question, answered by the providers that exist.
/// </summary>
public readonly record struct ExternalProviderKey
{
    public static readonly ExternalProviderKey Google = new("google");

    /// <summary>Every key this build knows, in the order the login page lists them.</summary>
    public static IReadOnlyList<ExternalProviderKey> Known { get; } = [Google];

    private ExternalProviderKey(string value) => Value = value;

    public string Value { get; }

    /// <summary>How a session earned through this provider is recorded (one home for the mapping).</summary>
    public LoginMethod LoginMethod => Value switch
    {
        "google" => LoginMethod.Google,
        _ => throw new UnreachableException($"No login method for provider key '{Value}'."),
    };

    public static bool TryParse(string? raw, out ExternalProviderKey key)
    {
        foreach (var known in Known)
        {
            if (string.Equals(known.Value, raw, StringComparison.Ordinal))
            {
                key = known;
                return true;
            }
        }

        key = default;
        return false;
    }

    public override string ToString() => Value;
}
