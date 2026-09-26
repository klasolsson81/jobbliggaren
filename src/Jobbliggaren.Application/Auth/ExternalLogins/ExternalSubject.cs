namespace Jobbliggaren.Application.Auth.ExternalLogins;

/// <summary>
/// A provider's identifier for a person (OIDC <c>sub</c>): stable, never reused, and an Art. 4(1) identifier, so it
/// is never logged. <see cref="ToString"/> prints no part of it. The bound is OIDC Core 1.0 §2's: at most 255 ASCII
/// characters; this type also refuses a space or a control character, which no provider documents.
/// </summary>
public readonly record struct ExternalSubject
{
    public const int MaximumLength = 255;

    private readonly string _value;

    private ExternalSubject(string value) => _value = value;

    public string Reveal() => _value;

    public override string ToString() => "ExternalSubject(redacted)";

    public static ExternalSubject? TryCreate(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length > MaximumLength)
            return null;

        foreach (var ch in raw)
        {
            if (ch < '!' || ch > '~')
                return null;
        }

        return new ExternalSubject(raw);
    }
}
