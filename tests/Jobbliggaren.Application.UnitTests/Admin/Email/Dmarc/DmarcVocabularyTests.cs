using Jobbliggaren.Application.Admin.Email.Dmarc;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Email.Dmarc;

// The tables store these numbers and a CHECK refuses 0, so the numbers are pinned and every mapping is total.
public sealed class DmarcVocabularyTests
{
    [Fact]
    public void TheStoredNumbers_AreThePinnedOnes()
    {
        Enum.GetValues<DmarcAlignment>().Select(v => (int)v).ShouldBe([1, 2, 3]);
        Enum.GetValues<DmarcRunOutcome>().Select(v => (int)v).ShouldBe([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
        Enum.GetValues<DmarcMessageOutcome>().Select(v => (int)v)
            .ShouldBe([1, 2, 3, 10, 11, 12, 13, 20, 21, 22, 23, 24, 25, 26, 30, 31, 32, 33]);
        Enum.GetValues<DmarcSkipReason>().Select(v => (int)v).ShouldBe([1, 2, 3, 4, 5, 6, 7]);
        Enum.GetValues<DmarcSessionFault>().Select(v => (int)v).ShouldBe([1, 2, 3, 4, 5, 6]);
    }

    [Fact]
    public void NoStoredMember_IsZero()
    {
        Enum.GetValues<DmarcAlignment>().ShouldNotContain(v => (int)v == 0);
        Enum.GetValues<DmarcRunOutcome>().ShouldNotContain(v => (int)v == 0);
        Enum.GetValues<DmarcMessageOutcome>().ShouldNotContain(v => (int)v == 0);
    }

    [Fact]
    public void EverySkipReason_MapsToItsOwnOutcome()
    {
        var outcomes = Enum.GetValues<DmarcSkipReason>().Select(DmarcOutcomes.OutcomeOf).ToList();

        outcomes.ShouldBeUnique();
        outcomes.ShouldAllBe(o => o.ToString().StartsWith("Skipped", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryRejection_MapsToARejectedOutcome_ThePolicyDomainApart()
    {
        foreach (var rejection in Enum.GetValues<DmarcRejection>())
        {
            DmarcOutcomes.OutcomeOf(rejection).ShouldBe(rejection == DmarcRejection.PolicyDomain
                ? DmarcMessageOutcome.RejectedPolicyDomain
                : DmarcMessageOutcome.RejectedContent);
        }
    }

    [Fact]
    public void EverySessionFault_EndsTheRunWithItsOwnOutcome()
    {
        var outcomes = Enum.GetValues<DmarcSessionFault>().Select(DmarcOutcomes.RunOutcomeOf).ToList();

        outcomes.ShouldBeUnique();
        outcomes.ShouldNotContain(DmarcRunOutcome.Succeeded);
    }

    private static readonly Dictionary<DmarcMessageOutcome, DmarcOutcomeClass> ExpectedClass = new()
    {
        [DmarcMessageOutcome.InProgress] = DmarcOutcomeClass.InProgress,
        [DmarcMessageOutcome.RetryPending] = DmarcOutcomeClass.RetryPending,
        [DmarcMessageOutcome.Abandoned] = DmarcOutcomeClass.Abandoned,
        [DmarcMessageOutcome.Stored] = DmarcOutcomeClass.Stored,
        [DmarcMessageOutcome.Duplicate] = DmarcOutcomeClass.Stored,
        [DmarcMessageOutcome.StoredConflicting] = DmarcOutcomeClass.Stored,
        [DmarcMessageOutcome.VolumeCapped] = DmarcOutcomeClass.VolumeCapped,
        [DmarcMessageOutcome.SkippedTooLarge] = DmarcOutcomeClass.Skipped,
        [DmarcMessageOutcome.SkippedNoReportPart] = DmarcOutcomeClass.Skipped,
        [DmarcMessageOutcome.SkippedMultipleReportParts] = DmarcOutcomeClass.Skipped,
        [DmarcMessageOutcome.SkippedForensic] = DmarcOutcomeClass.Skipped,
        [DmarcMessageOutcome.SkippedUnsupportedEncoding] = DmarcOutcomeClass.Skipped,
        [DmarcMessageOutcome.SkippedHeaderTooLarge] = DmarcOutcomeClass.Skipped,
        [DmarcMessageOutcome.SkippedNotInMailbox] = DmarcOutcomeClass.Skipped,
        [DmarcMessageOutcome.RejectedMalformed] = DmarcOutcomeClass.Rejected,
        [DmarcMessageOutcome.RejectedPolicyDomain] = DmarcOutcomeClass.Rejected,
        [DmarcMessageOutcome.RejectedContent] = DmarcOutcomeClass.Rejected,
        [DmarcMessageOutcome.RejectedByStore] = DmarcOutcomeClass.Rejected,
    };

    public static TheoryData<DmarcMessageOutcome, DmarcOutcomeClass> OutcomeClasses()
    {
        var rows = new TheoryData<DmarcMessageOutcome, DmarcOutcomeClass>();
        foreach (var (outcome, outcomeClass) in ExpectedClass)
            rows.Add(outcome, outcomeClass);
        return rows;
    }

    [Theory]
    [MemberData(nameof(OutcomeClasses))]
    public void EveryMessageOutcome_IsInExactlyOneClass(DmarcMessageOutcome outcome, DmarcOutcomeClass expected)
    {
        DmarcOutcomeClasses.ClassOf(outcome).ShouldBe(expected);
        DmarcOutcomeClasses.IsSkipped(outcome).ShouldBe(expected == DmarcOutcomeClass.Skipped);
        DmarcOutcomeClasses.IsRejected(outcome).ShouldBe(expected == DmarcOutcomeClass.Rejected);
    }

    [Fact]
    public void TheClassTable_NamesEveryMessageOutcomeOnce()
    {
        ExpectedClass.Keys.ShouldBe(Enum.GetValues<DmarcMessageOutcome>(), ignoreOrder: true);
    }

    [Fact]
    public void ExactlyARefusedAndAnUnansweredSignIn_BackOff()
    {
        foreach (var outcome in Enum.GetValues<DmarcRunOutcome>())
        {
            DmarcOutcomes.BacksOffSignIn(outcome).ShouldBe(
                outcome is DmarcRunOutcome.AuthRefused or DmarcRunOutcome.AuthUnconfirmed, outcome.ToString());
        }
    }

    [Fact]
    public void AnUnknownMember_IsRefusedRatherThanMapped()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DmarcOutcomes.OutcomeOf((DmarcSkipReason)99));
        Should.Throw<ArgumentOutOfRangeException>(() => DmarcOutcomes.OutcomeOf((DmarcRejection)99));
        Should.Throw<ArgumentOutOfRangeException>(() => DmarcOutcomes.RunOutcomeOf((DmarcSessionFault)99));
        Should.Throw<ArgumentOutOfRangeException>(() => DmarcOutcomes.BacksOffSignIn((DmarcRunOutcome)99));
        Should.Throw<ArgumentOutOfRangeException>(() => DmarcOutcomeClasses.ClassOf((DmarcMessageOutcome)99));
    }
}
