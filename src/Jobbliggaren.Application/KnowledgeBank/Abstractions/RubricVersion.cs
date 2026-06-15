using System.Globalization;

namespace Jobbliggaren.Application.KnowledgeBank.Abstractions;

/// <summary>
/// Semantic version of the CV-quality rubric (F4-7, BUILD §8.1/§8.6, research §2.8):
/// <c>rubric@major.minor.patch</c> — major = a criterion is added/removed, minor =
/// a threshold changes, patch = wording. Stored with every assessment downstream
/// (F4-9) and used to reason about N-1 compatibility (older versions runnable in
/// parallel — research §2.8).
/// <para>
/// A <c>readonly record struct</c> (CLAUDE.md §3.3 value object; senior-cto-advisor
/// DQ3=A) — value equality is the triple. <see cref="Parse(string)"/> FAILS LOUD on a
/// malformed version (a silently-coerced <c>0.0.0</c> would mis-route the loader's
/// compatibility decision); ordering is major → minor → patch.
/// </para>
/// </summary>
public readonly record struct RubricVersion(int Major, int Minor, int Patch)
    : IComparable<RubricVersion>
{
    /// <summary>Parses <c>"major.minor.patch"</c>; throws <see cref="FormatException"/>
    /// on anything else (wrong component count, non-numeric, negative, null).</summary>
    public static RubricVersion Parse(string value) =>
        TryParse(value, out var version)
            ? version
            : throw new FormatException(
                $"Ogiltig rubric-version: '{value}'. Förväntat format major.minor.patch (t.ex. 1.0.0).");

    /// <summary>Non-throwing parse; <see langword="false"/> on malformed/null input.</summary>
    public static bool TryParse(string? value, out RubricVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        // NumberStyles.None rejects sign (so negatives fail) and whitespace.
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        version = new RubricVersion(major, minor, patch);
        return true;
    }

    /// <summary>Orders by major, then minor, then patch.</summary>
    public int CompareTo(RubricVersion other)
    {
        var byMajor = Major.CompareTo(other.Major);
        if (byMajor != 0)
        {
            return byMajor;
        }

        var byMinor = Minor.CompareTo(other.Minor);
        return byMinor != 0 ? byMinor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public static bool operator <(RubricVersion left, RubricVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(RubricVersion left, RubricVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(RubricVersion left, RubricVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(RubricVersion left, RubricVersion right) => left.CompareTo(right) >= 0;
}
