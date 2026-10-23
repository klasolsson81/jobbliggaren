using System.Text.Json.Serialization;
using Jobbliggaren.Application.Common.Abstractions;

namespace Jobbliggaren.Application.Admin.Email;

/// <summary>Whether the per-kind region has counts to show (#1981 PR 3).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmailSendCountsStatus>))]
public enum EmailSendCountsStatus
{
    Observed,

    /// <summary>This deployment hands no mail to a provider, so nothing is counted.</summary>
    NotObserved,

    /// <summary>
    /// Our count is zero for the whole period while the provider's own count for it is at least one: the zero cannot be
    /// true, so no figure is shown. Produced by <see cref="EmailSendCountsEvaluator"/> alone.
    /// </summary>
    NotObservable,
}

/// <summary>Whether the counts cover every date of the period, or only the part after counting began.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmailSendCountsCoverage>))]
public enum EmailSendCountsCoverage
{
    Complete,

    /// <summary>Counting began inside the period; the figures are exact from <see cref="EmailSendCountsDto.CoveredFrom"/>.</summary>
    Partial,
}

/// <summary>
/// Our own counts per mail kind for one period as the browser receives them (#1981 PR 3, ADR 0180). A closed union:
/// figures only when observed, every kind in a fixed order, and a latest failure exactly when there was a failure. A
/// test pins the serialised key set.
/// </summary>
public sealed record EmailSendCountsDto
{
    private EmailSendCountsDto(
        EmailDeliveryPeriod period,
        EmailSendCountsStatus status,
        DateOnly? from,
        EmailSendCountsCoverage? coverage,
        DateTimeOffset? coveredFrom,
        IReadOnlyList<EmailKindCountDto>? kinds)
    {
        Period = period;
        Status = status;
        From = from;
        Coverage = coverage;
        CoveredFrom = coveredFrom;
        Kinds = kinds;
    }

    public EmailDeliveryPeriod Period { get; }

    public EmailSendCountsStatus Status { get; }

    /// <summary>The first Swedish civil date the period touches; the counts run through today. Absent when not observed.</summary>
    public DateOnly? From { get; }

    /// <summary>Present exactly when observed.</summary>
    public EmailSendCountsCoverage? Coverage { get; }

    /// <summary>When counting began, present exactly when the coverage is partial.</summary>
    public DateTimeOffset? CoveredFrom { get; }

    /// <summary>Every kind, in <see cref="EmailSendCountsEvaluator.KindOrder"/>, present exactly when observed.</summary>
    public IReadOnlyList<EmailKindCountDto>? Kinds { get; }

    public static EmailSendCountsDto Observed(
        EmailDeliveryPeriod period,
        DateOnly from,
        DateTimeOffset? coveredFrom,
        IReadOnlyList<EmailKindCountDto> kinds)
    {
        if (!kinds.Select(k => k.Kind).SequenceEqual(EmailSendCountsEvaluator.KindOrder))
        {
            throw new ArgumentException("Every kind is listed once, in the fixed order.", nameof(kinds));
        }

        return new(
            period,
            EmailSendCountsStatus.Observed,
            from,
            coveredFrom is null ? EmailSendCountsCoverage.Complete : EmailSendCountsCoverage.Partial,
            coveredFrom,
            kinds);
    }

    public static EmailSendCountsDto NotObserved(EmailDeliveryPeriod period) =>
        new(period, EmailSendCountsStatus.NotObserved, null, null, null, null);

    public static EmailSendCountsDto NotObservable(EmailDeliveryPeriod period, DateOnly from) =>
        new(period, EmailSendCountsStatus.NotObservable, from, null, null, null);
}

/// <summary>One kind's attempts in the period: those the provider accepted, those that failed, and the latest failure.</summary>
public sealed record EmailKindCountDto
{
    public EmailKindCountDto(EmailKind kind, long accepted, long failed, EmailLatestFailureDto? latestFailure)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(accepted);
        ArgumentOutOfRangeException.ThrowIfNegative(failed);
        if ((failed == 0) != (latestFailure is null))
        {
            throw new ArgumentException("A latest failure is present exactly when there was a failure.", nameof(latestFailure));
        }

        Kind = kind;
        Accepted = accepted;
        Failed = failed;
        LatestFailure = latestFailure;
    }

    public EmailKind Kind { get; }

    public long Accepted { get; }

    public long Failed { get; }

    public EmailLatestFailureDto? LatestFailure { get; }
}

/// <summary>
/// The latest date with a failure, and every failure class seen that date in <see cref="EmailSendOutcomes.FailureClasses"/>
/// order. A daily count does not know which failure came last within the date, so none is singled out.
/// </summary>
public sealed record EmailLatestFailureDto
{
    public EmailLatestFailureDto(DateOnly day, IReadOnlyList<EmailSendOutcome> outcomes)
    {
        if (outcomes.Count == 0 || outcomes.Distinct().Count() != outcomes.Count
            || outcomes.Any(o => o == EmailSendOutcome.Accepted || !Enum.IsDefined(o)))
        {
            throw new ArgumentException("At least one failure class, each once.", nameof(outcomes));
        }

        Day = day;
        Outcomes = outcomes;
    }

    public DateOnly Day { get; }

    public IReadOnlyList<EmailSendOutcome> Outcomes { get; }
}
