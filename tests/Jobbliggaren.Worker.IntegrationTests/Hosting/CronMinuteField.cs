using System.Globalization;
using System.Text.RegularExpressions;

namespace Jobbliggaren.Worker.IntegrationTests.Hosting;

/// <summary>
/// Reads the occurrences of a cron whose minute field alone varies, in the form <c>&lt;a&gt;-59/&lt;step&gt;</c>
/// or <c>*/&lt;step&gt;</c>, in UTC as Hangfire evaluates it. Cronos is not referenced, so this reads only
/// that form and throws on anything else; <see cref="TopMatchMailBoundTests"/> pins that the
/// matching cron keeps it.
/// </summary>
internal static partial class CronMinuteField
{
    [GeneratedRegex(@"^(\*|(?<start>[0-9]{1,2})-59)/(?<step>[0-9]{1,2})$")]
    internal static partial Regex MinuteStepForm();

    public static IReadOnlyList<int> Minutes(string cron)
    {
        var fields = cron.Split(' ');
        var form = fields.Length == 5 ? MinuteStepForm().Match(fields[0]) : Match.Empty;
        if (!form.Success || fields[1..].Any(field => field != "*"))
            throw new FormatException($"'{cron}' is not a minute-step cron with every other field '*'.");

        var start = form.Groups["start"].Success
            ? int.Parse(form.Groups["start"].Value, CultureInfo.InvariantCulture)
            : 0;
        var step = int.Parse(form.Groups["step"].Value, CultureInfo.InvariantCulture);
        if (start > 59 || step is < 1 or > 59)
            throw new FormatException($"'{cron}' has a start or step outside the hour.");

        return [.. Enumerable.Range(start, 60 - start).Where(minute => (minute - start) % step == 0)];
    }

    /// <summary>The gaps between consecutive occurrences, the one across the hour boundary included.</summary>
    public static IReadOnlyList<TimeSpan> Gaps(string cron)
    {
        var minutes = Minutes(cron);
        return
        [
            .. minutes.Zip(minutes.Skip(1), (earlier, later) => TimeSpan.FromMinutes(later - earlier)),
            TimeSpan.FromMinutes(minutes[0] + 60 - minutes[^1]),
        ];
    }

    /// <summary>The first occurrence strictly after <paramref name="instant"/>.</summary>
    public static DateTimeOffset NextAfter(string cron, DateTimeOffset instant)
    {
        var minutes = Minutes(cron);
        var utc = instant.ToUniversalTime();
        var candidate = new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
        while (candidate <= utc || !minutes.Contains(candidate.Minute))
            candidate = candidate.AddMinutes(1);
        return candidate;
    }
}
