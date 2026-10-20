using Jobbliggaren.Application.Admin.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email;

/// <summary>
/// The canary's truth tables (#1981 PR 2; dotnet-architect V2, security-auditor 5c), over the full product of inputs:
/// a zero or an empty list is shown as such only beside a sentinel of at least one, and an empty list only beside the
/// same period's statistics observed with no failure. Each input's failure carries its own reason, so a row also pins
/// which read's failure wins.
/// </summary>
public class EmailDeliveryEvaluatorTests
{
    private const EmailDeliveryPeriod Period = EmailDeliveryPeriod.Last3Days;
    private const EmailProviderReadReason FailuresReason = EmailProviderReadReason.Timeout;
    private const EmailProviderReadReason StatisticsReason = EmailProviderReadReason.Forbidden;
    private const EmailProviderReadReason SentinelReason = EmailProviderReadReason.RateLimited;
    private static readonly DateTimeOffset ReadAt = new(2026, 10, 19, 19, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ListAt = new(2026, 10, 19, 19, 0, 5, TimeSpan.Zero);

    /// <summary>A statistics read: NC, RKM, Failed, or Observed with the given total and failed counts.</summary>
    public enum Stat
    {
        NotConfigured,
        ReadKeyMissing,
        Failed,
        ObservedNoFailures,
        ObservedSomeFailures,
        ObservedZeroTotal,
        ObservedOneTotal,
    }

    public enum ListShape
    {
        NotConfigured,
        ReadKeyMissing,
        Failed,
        NonEmpty,
        Empty,
    }

    private static EmailDeliveryStatisticsRead Statistics(Stat stat, EmailProviderReadReason reason) => stat switch
    {
        Stat.NotConfigured => new EmailDeliveryStatisticsRead.NotConfigured(),
        Stat.ReadKeyMissing => new EmailDeliveryStatisticsRead.ReadKeyMissing(),
        Stat.Failed => new EmailDeliveryStatisticsRead.Failed(reason),
        Stat.ObservedNoFailures => new EmailDeliveryStatisticsRead.Observed(40, 40, 0, ReadAt),
        Stat.ObservedSomeFailures => new EmailDeliveryStatisticsRead.Observed(40, 37, 3, ReadAt),
        Stat.ObservedZeroTotal => new EmailDeliveryStatisticsRead.Observed(0, 0, 0, ReadAt),
        Stat.ObservedOneTotal => new EmailDeliveryStatisticsRead.Observed(1, 1, 0, ReadAt),
        _ => throw new ArgumentOutOfRangeException(nameof(stat)),
    };

    private static EmailRecentFailuresRead Failures(ListShape list) => list switch
    {
        ListShape.NotConfigured => new EmailRecentFailuresRead.NotConfigured(),
        ListShape.ReadKeyMissing => new EmailRecentFailuresRead.ReadKeyMissing(),
        ListShape.Failed => new EmailRecentFailuresRead.Failed(FailuresReason),
        ListShape.NonEmpty => new EmailRecentFailuresRead.Observed(
            [new EmailFailure(ListAt.AddMinutes(-3), MaskedEmailAddress.Of("zelda@example.com"), EmailFailureReason.MailboxFull)],
            ListAt),
        ListShape.Empty => new EmailRecentFailuresRead.Observed([], ListAt),
        _ => throw new ArgumentOutOfRangeException(nameof(list)),
    };

    // The sentinel's six shapes: the zero total is the canary's "blind key" case, OneTotal is the least it trusts, and
    // NoFailures stands for "more than one".
    private static readonly Stat[] SentinelShapes =
        [Stat.NotConfigured, Stat.ReadKeyMissing, Stat.Failed, Stat.ObservedZeroTotal, Stat.ObservedOneTotal, Stat.ObservedNoFailures];

    // The same period's statistics: its failed count is what matters beside an empty list.
    private static readonly Stat[] StatisticsShapes =
        [Stat.NotConfigured, Stat.ReadKeyMissing, Stat.Failed, Stat.ObservedNoFailures, Stat.ObservedSomeFailures];

    // The read the totals show: an observed one with failures, so a shown row carries non-zero figures.
    private static readonly Stat[] TotalsReadShapes =
        [Stat.NotConfigured, Stat.ReadKeyMissing, Stat.Failed, Stat.ObservedSomeFailures];

    // ---- totals: read × sentinel, with the pairs no registration produces below ----

    [Theory]
    [InlineData(Stat.NotConfigured, Stat.NotConfigured, EmailDeliveryStatus.NotObserved, EmailProviderReadReason.NotConfigured)]
    [InlineData(Stat.NotConfigured, Stat.ReadKeyMissing, EmailDeliveryStatus.NotObserved, EmailProviderReadReason.NotConfigured)]
    [InlineData(Stat.NotConfigured, Stat.Failed, EmailDeliveryStatus.NotObserved, EmailProviderReadReason.NotConfigured)]
    [InlineData(Stat.NotConfigured, Stat.ObservedZeroTotal, EmailDeliveryStatus.NotObserved, EmailProviderReadReason.NotConfigured)]
    [InlineData(Stat.NotConfigured, Stat.ObservedOneTotal, EmailDeliveryStatus.NotObserved, EmailProviderReadReason.NotConfigured)]
    [InlineData(Stat.NotConfigured, Stat.ObservedNoFailures, EmailDeliveryStatus.NotObserved, EmailProviderReadReason.NotConfigured)]
    [InlineData(Stat.ReadKeyMissing, Stat.NotConfigured, EmailDeliveryStatus.ReadKeyMissing, null)]
    [InlineData(Stat.ReadKeyMissing, Stat.ReadKeyMissing, EmailDeliveryStatus.ReadKeyMissing, null)]
    [InlineData(Stat.ReadKeyMissing, Stat.Failed, EmailDeliveryStatus.ReadKeyMissing, null)]
    [InlineData(Stat.ReadKeyMissing, Stat.ObservedZeroTotal, EmailDeliveryStatus.ReadKeyMissing, null)]
    [InlineData(Stat.ReadKeyMissing, Stat.ObservedOneTotal, EmailDeliveryStatus.ReadKeyMissing, null)]
    [InlineData(Stat.ReadKeyMissing, Stat.ObservedNoFailures, EmailDeliveryStatus.ReadKeyMissing, null)]
    [InlineData(Stat.Failed, Stat.NotConfigured, EmailDeliveryStatus.Failed, StatisticsReason)]
    [InlineData(Stat.Failed, Stat.ReadKeyMissing, EmailDeliveryStatus.Failed, StatisticsReason)]
    [InlineData(Stat.Failed, Stat.Failed, EmailDeliveryStatus.Failed, StatisticsReason)]
    [InlineData(Stat.Failed, Stat.ObservedZeroTotal, EmailDeliveryStatus.Failed, StatisticsReason)]
    [InlineData(Stat.Failed, Stat.ObservedOneTotal, EmailDeliveryStatus.Failed, StatisticsReason)]
    [InlineData(Stat.Failed, Stat.ObservedNoFailures, EmailDeliveryStatus.Failed, StatisticsReason)]
    [InlineData(Stat.ObservedSomeFailures, Stat.Failed, EmailDeliveryStatus.Failed, SentinelReason)]
    [InlineData(Stat.ObservedSomeFailures, Stat.ObservedZeroTotal, EmailDeliveryStatus.NotObservable, null)]
    [InlineData(Stat.ObservedSomeFailures, Stat.ObservedOneTotal, EmailDeliveryStatus.Observed, null)]
    [InlineData(Stat.ObservedSomeFailures, Stat.ObservedNoFailures, EmailDeliveryStatus.Observed, null)]
    public void EvaluateTotals_ShouldFollowTheTable(Stat read, Stat sentinel, EmailDeliveryStatus status, EmailProviderReadReason? reason)
    {
        var dto = EmailDeliveryEvaluator.EvaluateTotals(
            Period, Statistics(read, StatisticsReason), Statistics(sentinel, SentinelReason));

        dto.Period.ShouldBe(Period);
        dto.Status.ShouldBe(status);
        dto.Reason.ShouldBe(reason);
        if (status == EmailDeliveryStatus.Observed)
        {
            dto.Sent.ShouldBe(37);
            dto.Failed.ShouldBe(3);
            dto.ObservedAt.ShouldBe(ReadAt);
        }
        else
        {
            dto.Sent.ShouldBeNull();
            dto.Failed.ShouldBeNull();
            dto.ObservedAt.ShouldBeNull();
        }
    }

    [Fact]
    public void EvaluateTotals_ShouldShowAnObservedZero_OnlyBesideASentinelOfAtLeastOne()
    {
        var zero = new EmailDeliveryStatisticsRead.Observed(0, 0, 0, ReadAt);

        var dto = EmailDeliveryEvaluator.EvaluateTotals(Period, zero, Statistics(Stat.ObservedNoFailures, SentinelReason));

        dto.Status.ShouldBe(EmailDeliveryStatus.Observed);
        dto.Sent.ShouldBe(0);
        dto.Failed.ShouldBe(0);
        EmailDeliveryEvaluator.EvaluateTotals(Period, zero, zero).Status.ShouldBe(EmailDeliveryStatus.NotObservable);
    }

    // ---- failures: a list that is not an observed empty one decides alone, beside every statistics × sentinel pair ----

    public static TheoryData<ListShape, Stat, Stat> DecidingLists()
    {
        var data = new TheoryData<ListShape, Stat, Stat>();
        foreach (var list in new[] { ListShape.NotConfigured, ListShape.ReadKeyMissing, ListShape.Failed, ListShape.NonEmpty })
        {
            foreach (var statistics in StatisticsShapes)
            {
                foreach (var sentinel in SentinelShapes)
                {
                    data.Add(list, statistics, sentinel);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DecidingLists))]
    public void EvaluateFailures_ShouldLetAnythingButAnEmptyListDecideAlone(ListShape list, Stat statistics, Stat sentinel)
    {
        var dto = EmailDeliveryEvaluator.EvaluateFailures(
            Period, Failures(list), Statistics(statistics, StatisticsReason), Statistics(sentinel, SentinelReason));

        dto.Period.ShouldBe(Period);
        dto.Limit.ShouldBe(10);
        switch (list)
        {
            case ListShape.NotConfigured:
                dto.Status.ShouldBe(EmailDeliveryStatus.NotObserved);
                dto.Reason.ShouldBe(EmailProviderReadReason.NotConfigured);
                dto.Failures.ShouldBeNull();
                break;
            case ListShape.ReadKeyMissing:
                dto.Status.ShouldBe(EmailDeliveryStatus.ReadKeyMissing);
                dto.Reason.ShouldBeNull();
                dto.Failures.ShouldBeNull();
                break;
            case ListShape.Failed:
                dto.Status.ShouldBe(EmailDeliveryStatus.Failed);
                dto.Reason.ShouldBe(FailuresReason);
                dto.Failures.ShouldBeNull();
                break;
            default:
                dto.Status.ShouldBe(EmailDeliveryStatus.Observed);
                dto.Reason.ShouldBeNull();
                dto.ObservedAt.ShouldBe(ListAt);
                var failure = dto.Failures.ShouldNotBeNull().ShouldHaveSingleItem();
                failure.Recipient.ShouldBe("z•••@example.com");
                failure.Reason.ShouldBe(EmailFailureReason.MailboxFull);
                failure.CreatedAt.ShouldBe(ListAt.AddMinutes(-3));
                break;
        }
    }

    // ---- failures: an observed empty list beside a statistics × sentinel pair (first match wins) ----

    [Theory]
    // 1. A failed sentinel wins.
    [InlineData(Stat.NotConfigured, Stat.Failed, EmailDeliveryStatus.Failed, SentinelReason)]
    [InlineData(Stat.ReadKeyMissing, Stat.Failed, EmailDeliveryStatus.Failed, SentinelReason)]
    [InlineData(Stat.Failed, Stat.Failed, EmailDeliveryStatus.Failed, SentinelReason)]
    [InlineData(Stat.ObservedNoFailures, Stat.Failed, EmailDeliveryStatus.Failed, SentinelReason)]
    [InlineData(Stat.ObservedSomeFailures, Stat.Failed, EmailDeliveryStatus.Failed, SentinelReason)]
    // 2. Then failed statistics.
    [InlineData(Stat.Failed, Stat.NotConfigured, EmailDeliveryStatus.Failed, StatisticsReason)]
    [InlineData(Stat.Failed, Stat.ReadKeyMissing, EmailDeliveryStatus.Failed, StatisticsReason)]
    [InlineData(Stat.Failed, Stat.ObservedZeroTotal, EmailDeliveryStatus.Failed, StatisticsReason)]
    [InlineData(Stat.Failed, Stat.ObservedOneTotal, EmailDeliveryStatus.Failed, StatisticsReason)]
    [InlineData(Stat.Failed, Stat.ObservedNoFailures, EmailDeliveryStatus.Failed, StatisticsReason)]
    // 3. Then a sentinel of zero.
    [InlineData(Stat.ObservedNoFailures, Stat.ObservedZeroTotal, EmailDeliveryStatus.NotObservable, null)]
    [InlineData(Stat.ObservedSomeFailures, Stat.ObservedZeroTotal, EmailDeliveryStatus.NotObservable, null)]
    // 4. Then statistics that count a failure the list does not hold.
    [InlineData(Stat.ObservedSomeFailures, Stat.ObservedOneTotal, EmailDeliveryStatus.NotObservable, null)]
    [InlineData(Stat.ObservedSomeFailures, Stat.ObservedNoFailures, EmailDeliveryStatus.NotObservable, null)]
    // 5. The rows an empty list is trusted on: a sentinel of at least one, and no counted failure.
    [InlineData(Stat.ObservedNoFailures, Stat.ObservedOneTotal, EmailDeliveryStatus.Observed, null)]
    [InlineData(Stat.ObservedNoFailures, Stat.ObservedNoFailures, EmailDeliveryStatus.Observed, null)]
    // 6. A gate not configured or without a key beside an observed list is a pair no registration produces. Above, such
    // a pair appears only where a Failed read decides first; the rest are below.
    public void EvaluateFailures_ShouldTrustAnEmptyList_OnlyBesideAPassedCanaryAndNoCountedFailure(
        Stat statistics, Stat sentinel, EmailDeliveryStatus status, EmailProviderReadReason? reason)
    {
        var dto = EmailDeliveryEvaluator.EvaluateFailures(
            Period, Failures(ListShape.Empty), Statistics(statistics, StatisticsReason), Statistics(sentinel, SentinelReason));

        dto.Status.ShouldBe(status);
        dto.Reason.ShouldBe(reason);
        if (status == EmailDeliveryStatus.Observed)
        {
            dto.Failures.ShouldNotBeNull().ShouldBeEmpty();
            dto.ObservedAt.ShouldBe(ListAt);
        }
        else
        {
            dto.Failures.ShouldBeNull();
            dto.ObservedAt.ShouldBeNull();
        }
    }

    // ---- pairs no registration produces ----

    /// <summary>
    /// Declared unreachable (AGENTS.md §5 <c>Tests:</c>): an observed read beside a NotConfigured or ReadKeyMissing one.
    /// <c>AddEmailUsageSource</c> registers all three read ports from one branch: the memoized Scaleway adapters, or one
    /// <c>ReadKeyMissingEmailProviderReads</c> instance, or one <c>NotConfiguredEmailProviderReads</c> instance, and
    /// those two classes are the only code that constructs either read. <c>EmailUsageSourceRegistrationTests</c> pins
    /// the branch, in <c>WhereNothingIsRead_OneInstanceAnswersEveryReadPort</c> and
    /// <c>UnderScaleway_APresentReadKey_RegistersEachReadOnce_AsAMemoizedSingleton_OverItsAdapter</c>. The rows assert
    /// only that the read side degrades safely if that invariant breaks: no Observed status and no figure.
    /// </summary>
    [Theory]
    [InlineData(Stat.ObservedSomeFailures, Stat.NotConfigured)]
    [InlineData(Stat.ObservedSomeFailures, Stat.ReadKeyMissing)]
    public void EvaluateTotals_ShouldShowNoFigures_ForAPairNoRegistrationProduces(Stat read, Stat sentinel)
    {
        var dto = EmailDeliveryEvaluator.EvaluateTotals(
            Period, Statistics(read, StatisticsReason), Statistics(sentinel, SentinelReason));

        dto.Status.ShouldNotBe(EmailDeliveryStatus.Observed);
        dto.Sent.ShouldBeNull();
        dto.Failed.ShouldBeNull();
        dto.ObservedAt.ShouldBeNull();
    }

    /// <summary>
    /// Declared unreachable for the reason <see cref="EvaluateTotals_ShouldShowNoFigures_ForAPairNoRegistrationProduces"/>
    /// gives: an observed list comes only from the Scaleway branch, whose statistics are never NotConfigured or
    /// ReadKeyMissing. The rows assert only that the read side degrades safely: no Observed status and no list.
    /// </summary>
    [Theory]
    [InlineData(Stat.NotConfigured, Stat.NotConfigured)]
    [InlineData(Stat.NotConfigured, Stat.ReadKeyMissing)]
    [InlineData(Stat.NotConfigured, Stat.ObservedZeroTotal)]
    [InlineData(Stat.NotConfigured, Stat.ObservedOneTotal)]
    [InlineData(Stat.NotConfigured, Stat.ObservedNoFailures)]
    [InlineData(Stat.ReadKeyMissing, Stat.NotConfigured)]
    [InlineData(Stat.ReadKeyMissing, Stat.ReadKeyMissing)]
    [InlineData(Stat.ReadKeyMissing, Stat.ObservedZeroTotal)]
    [InlineData(Stat.ReadKeyMissing, Stat.ObservedOneTotal)]
    [InlineData(Stat.ReadKeyMissing, Stat.ObservedNoFailures)]
    [InlineData(Stat.ObservedNoFailures, Stat.NotConfigured)]
    [InlineData(Stat.ObservedNoFailures, Stat.ReadKeyMissing)]
    [InlineData(Stat.ObservedSomeFailures, Stat.NotConfigured)]
    [InlineData(Stat.ObservedSomeFailures, Stat.ReadKeyMissing)]
    public void EvaluateFailures_ShouldShowNoList_ForAPairNoRegistrationProduces(Stat statistics, Stat sentinel)
    {
        var dto = EmailDeliveryEvaluator.EvaluateFailures(
            Period, Failures(ListShape.Empty), Statistics(statistics, StatisticsReason), Statistics(sentinel, SentinelReason));

        dto.Status.ShouldNotBe(EmailDeliveryStatus.Observed);
        dto.Failures.ShouldBeNull();
        dto.ObservedAt.ShouldBeNull();
    }

    // ---- the tables' coverage ----

    [Fact]
    public void TheTotalsTable_ShouldCoverEveryReadAndSentinelPair() =>
        ShouldCoverEachPairOnce(
            Product(TotalsReadShapes, SentinelShapes),
            nameof(EvaluateTotals_ShouldFollowTheTable),
            nameof(EvaluateTotals_ShouldShowNoFigures_ForAPairNoRegistrationProduces));

    [Fact]
    public void TheEmptyListTable_ShouldCoverEveryStatisticsAndSentinelPair() =>
        ShouldCoverEachPairOnce(
            Product(StatisticsShapes, SentinelShapes),
            nameof(EvaluateFailures_ShouldTrustAnEmptyList_OnlyBesideAPassedCanaryAndNoCountedFailure),
            nameof(EvaluateFailures_ShouldShowNoList_ForAPairNoRegistrationProduces));

    /// <summary>The rows of <paramref name="theories"/> together hold every pair of <paramref name="product"/> exactly once.</summary>
    private static void ShouldCoverEachPairOnce(List<(Stat, Stat)> product, params string[] theories)
    {
        var rows = theories
            .SelectMany(theory => typeof(EmailDeliveryEvaluatorTests).GetMethod(theory)!
                .GetCustomAttributes(typeof(InlineDataAttribute), false)
                .Cast<InlineDataAttribute>())
            .Select(row => ((Stat)row.Data[0]!, (Stat)row.Data[1]!))
            .ToList();

        rows.Count.ShouldBe(product.Count);
        rows.ShouldBe(product, ignoreOrder: true);
    }

    private static List<(Stat, Stat)> Product(Stat[] first, Stat[] second) =>
        [.. first.SelectMany(a => second.Select(b => (a, b)))];
}
