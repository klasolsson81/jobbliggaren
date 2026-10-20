using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.Admin.Email;

/// <summary>
/// The rolling window the delivery figures cover (#1981 PR 2). A closed set: the server derives the window's start from
/// it, so the browser never chooses a time or a size. Its wire tokens are <see cref="EmailDeliveryPeriods"/>'s.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmailDeliveryPeriod>))]
public enum EmailDeliveryPeriod
{
    [JsonStringEnumMemberName(EmailDeliveryPeriods.Last24HoursToken)]
    Last24Hours,

    [JsonStringEnumMemberName(EmailDeliveryPeriods.Last3DaysToken)]
    Last3Days,

    [JsonStringEnumMemberName(EmailDeliveryPeriods.Last7DaysToken)]
    Last7Days,
}

/// <summary>The one vocabulary of <see cref="EmailDeliveryPeriod"/> on the wire, and each period's length.</summary>
public static class EmailDeliveryPeriods
{
    public const string Last24HoursToken = "h24";
    public const string Last3DaysToken = "d3";
    public const string Last7DaysToken = "d7";

    /// <summary>
    /// The period a query-string token names, by ordinal match, or null for anything else: absent, empty, another case,
    /// a member name or a number. The validator turns null into a 400, so an enum is never bound from the query.
    /// </summary>
    public static EmailDeliveryPeriod? FromToken(string? token) => token switch
    {
        Last24HoursToken => EmailDeliveryPeriod.Last24Hours,
        Last3DaysToken => EmailDeliveryPeriod.Last3Days,
        Last7DaysToken => EmailDeliveryPeriod.Last7Days,
        _ => null,
    };

    /// <summary>The window's length, back from the moment of the read.</summary>
    public static TimeSpan LengthOf(EmailDeliveryPeriod period) => period switch
    {
        EmailDeliveryPeriod.Last24Hours => TimeSpan.FromHours(24),
        EmailDeliveryPeriod.Last3Days => TimeSpan.FromHours(72),
        EmailDeliveryPeriod.Last7Days => TimeSpan.FromHours(168),
        _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Not a delivery period."),
    };
}

/// <summary>The bounds of the delivery reads.</summary>
public static class EmailDeliveryLimits
{
    /// <summary>
    /// The canary's window: a read key that cannot see mail reads zero for every window, so a zero over this long a
    /// window makes every delivery figure NotObservable. 32 days, not 31, so the longest provider month is inside it
    /// whichever time zone the provider counts in. Its only cost is a false NotObservable, the safe direction.
    /// </summary>
    public static readonly TimeSpan SentinelWindow = TimeSpan.FromDays(32);

    /// <summary>The most failures one read returns: one page, never paginated.</summary>
    public const int MaxRecentFailures = 10;
}
