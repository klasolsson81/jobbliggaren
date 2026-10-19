using Jobbliggaren.Application.Admin.Email;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email;

/// <summary>
/// The delivery ports' results refuse shapes the provider cannot mean (#1981 PR 2): negative counts, more sent and
/// failed than in total, an over-long or unordered failure list, and "not configured" posing as a failure.
/// </summary>
public class EmailDeliveryReadsTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 19, 19, 0, 0, TimeSpan.Zero);

    private static EmailFailure FailureAt(DateTimeOffset createdAt) =>
        new(createdAt, MaskedEmailAddress.Of("zelda@example.com"), EmailFailureReason.HardBounce);

    // ---- statistics ----

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(1, -1, 0)]
    [InlineData(1, 0, -1)]
    public void ObservedStatistics_ShouldRefuseANegativeCount(long total, long sent, long failed) =>
        Should.Throw<ArgumentOutOfRangeException>(() => new EmailDeliveryStatisticsRead.Observed(total, sent, failed, At));

    [Fact]
    public void ObservedStatistics_ShouldRefuseMoreSentAndFailedThanInTotal() =>
        Should.Throw<ArgumentException>(() => new EmailDeliveryStatisticsRead.Observed(3, 2, 2, At));

    [Fact]
    public void ObservedStatistics_ShouldAcceptZeros_AndSentPlusFailedUpToTheTotal()
    {
        new EmailDeliveryStatisticsRead.Observed(0, 0, 0, At).TotalCount.ShouldBe(0);
        var observed = new EmailDeliveryStatisticsRead.Observed(5, 3, 2, At);
        observed.SentCount.ShouldBe(3);
        observed.FailedCount.ShouldBe(2);
        observed.ObservedAt.ShouldBe(At);
    }

    [Fact]
    public void FailedStatistics_ShouldRefuseNotConfiguredAndAReasonOutsideTheEnum()
    {
        Should.Throw<ArgumentException>(() => new EmailDeliveryStatisticsRead.Failed(EmailProviderReadReason.NotConfigured));
        Should.Throw<ArgumentException>(() => new EmailDeliveryStatisticsRead.Failed((EmailProviderReadReason)99));
    }

    // ---- failures ----

    [Fact]
    public void ObservedFailures_ShouldRefuseMoreThanTheLimit()
    {
        var eleven = Enumerable.Range(0, EmailDeliveryLimits.MaxRecentFailures + 1)
            .Select(i => FailureAt(At.AddMinutes(-i)))
            .ToList();

        Should.Throw<ArgumentException>(() => new EmailRecentFailuresRead.Observed(eleven, At));
    }

    [Fact]
    public void ObservedFailures_ShouldRefuseAnOlderFailureBeforeANewerOne() =>
        Should.Throw<ArgumentException>(() =>
            new EmailRecentFailuresRead.Observed([FailureAt(At.AddMinutes(-5)), FailureAt(At)], At));

    [Fact]
    public void ObservedFailures_ShouldRefuseANullFailure() =>
        Should.Throw<ArgumentException>(() => new EmailRecentFailuresRead.Observed([FailureAt(At), null!], At));

    [Fact]
    public void ObservedFailures_ShouldAcceptTheLimit_NewestFirst_WithTies()
    {
        var ten = Enumerable.Range(0, EmailDeliveryLimits.MaxRecentFailures)
            .Select(i => FailureAt(At.AddMinutes(-(i / 2))))
            .ToList();

        new EmailRecentFailuresRead.Observed(ten, At).Failures.Count.ShouldBe(10);
    }

    [Fact]
    public void ObservedFailures_ShouldHoldACopy_SoTheCallersListCannotChangeIt()
    {
        var list = new List<EmailFailure> { FailureAt(At) };
        var observed = new EmailRecentFailuresRead.Observed(list, At);

        list.Add(FailureAt(At.AddMinutes(-1)));

        observed.Failures.Count.ShouldBe(1);
        observed.Failures.ShouldBeOfType<EmailFailure[]>();
    }

    [Fact]
    public void FailedFailures_ShouldRefuseNotConfiguredAndAReasonOutsideTheEnum()
    {
        Should.Throw<ArgumentException>(() => new EmailRecentFailuresRead.Failed(EmailProviderReadReason.NotConfigured));
        Should.Throw<ArgumentException>(() => new EmailRecentFailuresRead.Failed((EmailProviderReadReason)99));
    }

    [Fact]
    public void AFailure_ShouldRefuseANullRecipientAndAnUndefinedReason()
    {
        Should.Throw<ArgumentNullException>(() => new EmailFailure(At, null!, EmailFailureReason.Other));
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new EmailFailure(At, MaskedEmailAddress.HiddenAddress, (EmailFailureReason)99));
    }

    // ---- the reason precedence ----

    [Fact]
    public void MostSevere_ShouldBeOther_WhenThereIsNoCandidate() =>
        EmailFailureReasons.MostSevere([]).ShouldBe(EmailFailureReason.Other);

    [Theory]
    [InlineData(new[] { EmailFailureReason.Other, EmailFailureReason.Spam }, EmailFailureReason.Spam)]
    [InlineData(new[] { EmailFailureReason.SoftBounce, EmailFailureReason.HardBounce }, EmailFailureReason.HardBounce)]
    [InlineData(new[] { EmailFailureReason.HardBounce, EmailFailureReason.MailboxNotFound }, EmailFailureReason.MailboxNotFound)]
    [InlineData(new[] { EmailFailureReason.Blocklisted, EmailFailureReason.MailboxFull }, EmailFailureReason.MailboxFull)]
    [InlineData(new[] { EmailFailureReason.Spam, EmailFailureReason.SoftBounce }, EmailFailureReason.SoftBounce)]
    [InlineData(new[] { EmailFailureReason.Blocklisted, EmailFailureReason.Spam }, EmailFailureReason.Spam)]
    [InlineData(new[] { EmailFailureReason.Other, EmailFailureReason.Blocklisted }, EmailFailureReason.Blocklisted)]
    public void MostSevere_ShouldPickByThePrecedence_WhateverTheOrder(EmailFailureReason[] candidates, EmailFailureReason expected)
    {
        EmailFailureReasons.MostSevere(candidates).ShouldBe(expected);
        EmailFailureReasons.MostSevere([.. candidates.Reverse()]).ShouldBe(expected);
    }

    [Fact]
    public void MostSevere_ShouldFollowThePrecedenceAcrossEveryReason()
    {
        EmailFailureReason[] precedence =
        [
            EmailFailureReason.MailboxNotFound,
            EmailFailureReason.HardBounce,
            EmailFailureReason.MailboxFull,
            EmailFailureReason.SoftBounce,
            EmailFailureReason.Spam,
            EmailFailureReason.Blocklisted,
            EmailFailureReason.Other,
        ];
        Enum.GetValues<EmailFailureReason>().ShouldBe(precedence, ignoreOrder: true);

        for (var i = 0; i < precedence.Length; i++)
        {
            EmailFailureReasons.MostSevere([.. precedence[i..].Reverse()]).ShouldBe(precedence[i]);
        }
    }

    [Fact]
    public void MostSevere_ShouldRefuseAnUndefinedReason() =>
        Should.Throw<ArgumentOutOfRangeException>(() => EmailFailureReasons.MostSevere([(EmailFailureReason)99]));

    // ---- the DTOs ----

    [Fact]
    public void TheDtos_ShouldRefuseNotConfiguredAsAFailureReason()
    {
        Should.Throw<ArgumentException>(() =>
            EmailDeliveryTotalsDto.ReadFailed(EmailDeliveryPeriod.Last7Days, EmailProviderReadReason.NotConfigured));
        Should.Throw<ArgumentException>(() =>
            EmailRecentFailuresDto.ReadFailed(EmailDeliveryPeriod.Last7Days, EmailProviderReadReason.NotConfigured));
    }

    [Fact]
    public void AFailureDto_ShouldCarryTheMaskedValue_AndNothingElseOfTheAddress()
    {
        var dto = new EmailFailureDto(At, MaskedEmailAddress.Of("zelda@example.com"), EmailFailureReason.MailboxFull);

        dto.Recipient.ShouldBe("z•••@example.com");
        dto.CreatedAt.ShouldBe(At);
        dto.Reason.ShouldBe(EmailFailureReason.MailboxFull);
    }

    [Fact]
    public void TheFailuresDto_ShouldAlwaysCarryTheLimit() =>
        EmailRecentFailuresDto.NotObserved(EmailDeliveryPeriod.Last24Hours).Limit.ShouldBe(EmailDeliveryLimits.MaxRecentFailures);
}
