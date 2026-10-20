using System.Buffers;
using System.Text;

namespace Jobbliggaren.Application.Admin.Email;

/// <summary>
/// A recipient address as the failures list shows it (#1981 PR 2): the local part's first letter or digit, a fixed
/// <see cref="Hidden"/>, and the whole domain, as in <c>z•••@example.com</c>. This is pseudonymisation, not
/// anonymisation: for a personal domain the masked form still identifies its holder.
/// <para>
/// The only way to make one is <see cref="Of"/>, which never throws and never returns the input: no <c>@</c>, an
/// empty local part, an empty domain, an input over <see cref="MaxAddressLength"/>, or a domain holding anything but
/// letters, digits, <c>-</c> and <c>.</c> (a control, format, bidi or whitespace character, or a lone surrogate) gives
/// <see cref="Hidden"/> alone. The mask is fixed, so the local part's length does not show. Scoped to this surface:
/// it lives in Application, not Domain, so no other surface inherits the form unreviewed.
/// </para>
/// </summary>
public sealed record MaskedEmailAddress
{
    /// <summary>The fixed mask, and the whole value when the address cannot be shown in part.</summary>
    public const string Hidden = "•••";

    /// <summary>The longest input that is masked at all.</summary>
    public const int MaxAddressLength = 320;

    private const int MaxDomainLength = 253;

    private MaskedEmailAddress(string value) => Value = value;

    /// <summary>An address of which nothing is shown.</summary>
    public static MaskedEmailAddress HiddenAddress { get; } = new(Hidden);

    public string Value { get; }

    public static MaskedEmailAddress Of(ReadOnlySpan<char> address) => new(Mask(address));

    public override string ToString() => Value;

    private static string Mask(ReadOnlySpan<char> address)
    {
        if (address.Length > MaxAddressLength)
        {
            return Hidden;
        }

        // The last @: a quoted local part may hold another, the domain never does.
        var at = address.LastIndexOf('@');
        if (at <= 0)
        {
            return Hidden;
        }

        var domain = address[(at + 1)..];
        if (!IsShownWhole(domain))
        {
            return Hidden;
        }

        var first = Rune.DecodeFromUtf16(address[..at], out var rune, out _) == OperationStatus.Done
                    && Rune.IsLetterOrDigit(rune)
            ? rune.ToString()
            : string.Empty;

        return string.Concat(first, Hidden, "@", domain);
    }

    private static bool IsShownWhole(ReadOnlySpan<char> domain)
    {
        if (domain.IsEmpty || domain.Length > MaxDomainLength)
        {
            return false;
        }

        // A lone surrogate enumerates as U+FFFD, which is neither a letter nor a digit.
        foreach (var rune in domain.EnumerateRunes())
        {
            if (!Rune.IsLetterOrDigit(rune) && rune.Value is not ('-' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
