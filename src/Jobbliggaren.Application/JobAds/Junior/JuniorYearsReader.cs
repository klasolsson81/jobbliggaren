using System.Globalization;
using System.Text.RegularExpressions;

namespace Jobbliggaren.Application.JobAds.Junior;

/// <summary>
/// #2264 — reads a stated years-of-experience requirement ("minst tre års erfarenhet", "2–4 years of experience").
/// Each pattern captures the lower bound in a group named <c>from</c> and may capture an upper bound in <c>to</c>;
/// a bound is digits or a number word from the data file. A match outside the plausible range, or with an upper
/// bound below its lower, is ignored. When several requirements are stated, the highest lower bound counts.
/// </summary>
public sealed class JuniorYearsReader
{
    public const string FromGroup = "from";
    public const string ToGroup = "to";

    private readonly IReadOnlyList<Regex> _patterns;
    private readonly Dictionary<string, int> _numberWords;
    private readonly int _plausibleMin;
    private readonly int _plausibleMax;

    public JuniorYearsReader(
        IReadOnlyList<Regex> patterns,
        IReadOnlyDictionary<string, int> numberWords,
        int plausibleMin,
        int plausibleMax)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(numberWords);
        ArgumentOutOfRangeException.ThrowIfNegative(plausibleMin);
        ArgumentOutOfRangeException.ThrowIfLessThan(plausibleMax, plausibleMin);

        _patterns = patterns;
        _numberWords = new Dictionary<string, int>(numberWords, StringComparer.OrdinalIgnoreCase);
        _plausibleMin = plausibleMin;
        _plausibleMax = plausibleMax;
    }

    /// <summary>The highest stated lower bound, or <see langword="null"/> when the text states none.</summary>
    public int? ReadRequirement(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int? requirement = null;
        foreach (var pattern in _patterns)
        {
            foreach (Match match in pattern.Matches(text))
            {
                if (!TryReadBound(match.Groups[FromGroup], out var from))
                    continue;

                var to = from;
                var upper = match.Groups[ToGroup];
                if (upper.Success && !TryReadBound(upper, out to))
                    continue;

                if (from < _plausibleMin || from > _plausibleMax || to < from)
                    continue;

                requirement = requirement is { } current ? Math.Max(current, from) : from;
            }
        }

        return requirement;
    }

    private bool TryReadBound(Group group, out int value)
    {
        value = 0;
        if (!group.Success)
            return false;

        var token = group.Value.Trim();
        if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out value))
            return true;

        return _numberWords.TryGetValue(token, out value);
    }
}
