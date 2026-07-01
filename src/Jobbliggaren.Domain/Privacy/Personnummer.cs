using System.Diagnostics.CodeAnalysis;

namespace Jobbliggaren.Domain.Privacy;

/// <summary>
/// Value object representing a parsed-and-validated Swedish personnummer or
/// samordningsnummer. PII-by-construction: the raw significant digits are held
/// PRIVATELY (required for the Luhn proof and value equality) and are NEVER
/// exposed publicly. The only public textual surface is <see cref="Masked"/>,
/// a deliberately redacted form that contains none of the real digits.
///
/// ADR 0074 Invariant 1: a personnummer must NEVER reach logs or any unflagged
/// surface. The guard only ever FLAGS a personnummer for removal — there is
/// deliberately NO factory that adds, generates, suggests, or echoes one
/// (IMY posture; CLAUDE.md §5; BUILD §13). The single entry point is
/// <see cref="TryParse"/>, which parses an EXISTING candidate token; it can
/// never invent a number.
/// </summary>
public sealed record Personnummer
{
    // Raw 10 significant digits (YYMMDD + 3 birth digits + 1 check digit).
    // PRIVATE — needed for the Luhn proof and for correct value equality; never
    // exposed publicly. Not emitted by ToString (overridden below) and not
    // serialized (no public getter), so it cannot leak to logs.
    private readonly string _significantDigits;

    private Personnummer(string significantDigits, PersonnummerKind kind, string masked)
    {
        _significantDigits = significantDigits;
        Kind = kind;
        Masked = masked;
    }

    /// <summary>Personnummer or samordningsnummer.</summary>
    public PersonnummerKind Kind { get; }

    /// <summary>
    /// Deliberate redacted form for display/diagnostics. Every digit is replaced
    /// by '*'; only the separator (if any) and the overall length are preserved.
    /// Contains NONE of the real significant digits — safe to surface and to log.
    /// </summary>
    public string Masked { get; }

    /// <summary>
    /// Primary (and only) entry point. Attempts to parse and validate a single
    /// candidate token as a Swedish personnummer or samordningsnummer.
    /// Accepts the 10-digit form <c>YYMMDD[-+]XXXX</c> / <c>YYMMDDXXXX</c> and the
    /// 12-digit form <c>YYYYMMDD[-+]XXXX</c> / <c>YYYYMMDDXXXX</c>. Validation =
    /// lenient date sanity (month 1–12, day 1–31; samordningsnummer = day+60,
    /// raw day 61–91) AND the Luhn (mod-10) checksum over the 10 significant
    /// digits. The century prefix (12-digit form) does not participate in Luhn.
    /// </summary>
    /// <param name="candidate">The candidate token (no surrounding delimiters).</param>
    /// <param name="result">The parsed value object on success; otherwise null.</param>
    /// <returns><c>true</c> if <paramref name="candidate"/> is a valid personnummer or samordningsnummer.</returns>
    public static bool TryParse(ReadOnlySpan<char> candidate, [MaybeNullWhen(false)] out Personnummer result)
    {
        result = null!;

        candidate = candidate.Trim();
        if (candidate.IsEmpty)
            return false;

        // Extract the significant digits, tolerating at most one '-'/'+' separator.
        // Any other character (letter, second separator, 13th digit) is rejected.
        Span<char> digits = stackalloc char[12];
        var digitCount = 0;
        var separatorCount = 0;

        foreach (var c in candidate)
        {
            if (char.IsAsciiDigit(c))
            {
                if (digitCount == 12)
                    return false; // more than 12 digits — not a personnummer
                digits[digitCount++] = c;
            }
            else if (c is '-' or '+')
            {
                if (++separatorCount > 1)
                    return false;
            }
            else
            {
                return false; // stray non-digit, non-separator character
            }
        }

        // The 10 significant digits: the whole 10-digit form, or the 12-digit form
        // with its 2-digit century prefix dropped. Declared-and-initialized in one
        // statement so the span inherits the stackalloc's narrow escape scope.
        if (digitCount is not (10 or 12))
            return false;

        ReadOnlySpan<char> significant = digitCount == 12 ? digits[2..12] : digits[..10];

        var month = ((significant[2] - '0') * 10) + (significant[3] - '0');
        var rawDay = ((significant[4] - '0') * 10) + (significant[5] - '0');

        // Samordningsnummer encodes the birth day as day+60 (raw 61–91).
        PersonnummerKind kind;
        int calendarDay;
        if (rawDay is >= 61 and <= 91)
        {
            kind = PersonnummerKind.Samordningsnummer;
            calendarDay = rawDay - 60;
        }
        else
        {
            kind = PersonnummerKind.Personnummer;
            calendarDay = rawDay;
        }

        // Lenient date sanity: the century is unknown from the number alone, so
        // strict leap-year validation is impossible. For a PII guard a missed
        // personnummer (false negative) is a leak and strictly worse than a rare
        // over-flag, so we bound month/day rather than over-reject.
        if (month is < 1 or > 12)
            return false;
        if (calendarDay is < 1 or > 31)
            return false;

        if (!PassesLuhn(significant))
            return false;

        result = new Personnummer(new string(significant), kind, MaskSpan(candidate));
        return true;
    }

    /// <summary>
    /// Returns the redacted <see cref="Masked"/> form — NEVER the raw digits.
    /// This override replaces the record's synthesized ToString so that accidental
    /// string interpolation / logging of a Personnummer cannot leak digits
    /// (ADR 0074 Invariant 1; CLAUDE.md §5).
    /// </summary>
    public override string ToString() => Masked;

    // Luhn (mod-10) over the 10 significant digits. Digits at even indices
    // (0-based: positions 1,3,5,7,9 one-based, counted from the left) are doubled;
    // a doubled value > 9 has 9 subtracted. The number is valid when the sum is
    // a multiple of 10.
    private static bool PassesLuhn(ReadOnlySpan<char> tenDigits)
    {
        var sum = 0;
        for (var i = 0; i < 10; i++)
        {
            var d = tenDigits[i] - '0';
            if ((i & 1) == 0)
            {
                d *= 2;
                if (d > 9)
                    d -= 9;
            }

            sum += d;
        }

        return sum % 10 == 0;
    }

    /// <summary>
    /// Masks a personnummer-shaped text span: every ASCII digit → '*', every other
    /// character (a '-'/'+' separator or a bridging whitespace / zero-width gap) is
    /// kept and the overall length preserved, so a masked span maps 1:1 back onto the
    /// original text it was found in — no offset translation. Exposes NONE of the real
    /// digits (ADR 0074 Invariant 1; CLAUDE.md §5). Shared by the gap-aware scan
    /// (<see cref="PersonnummerScanner.ScanWithGaps"/>) whose matched span may carry a
    /// gap; the ordinary contiguous token is unaffected (identical output as before).
    /// </summary>
    internal static string MaskSpan(ReadOnlySpan<char> span)
    {
        if (span.IsEmpty)
            return string.Empty;

        // The ordinary personnummer token is short and masks on the stack. A matched
        // span may in principle be inflated by many bridging zero-width characters
        // (\p{Cf}); fall back to the heap past a small threshold so masking a
        // user-controlled span can never overflow the stack.
        const int stackThreshold = 32;
        if (span.Length <= stackThreshold)
        {
            Span<char> buffer = stackalloc char[stackThreshold];
            return MaskInto(span, buffer[..span.Length]);
        }

        return MaskInto(span, new char[span.Length]);
    }

    private static string MaskInto(ReadOnlySpan<char> span, Span<char> buffer)
    {
        for (var i = 0; i < span.Length; i++)
            buffer[i] = char.IsAsciiDigit(span[i]) ? '*' : span[i];

        return new string(buffer);
    }
}
