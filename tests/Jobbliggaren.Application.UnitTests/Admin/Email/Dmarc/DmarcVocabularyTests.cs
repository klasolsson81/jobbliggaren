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
        Enum.GetValues<DmarcRunOutcome>().Select(v => (int)v).ShouldBe([1, 2, 3, 4, 5, 6, 7, 8, 9]);
        Enum.GetValues<DmarcMessageOutcome>().Select(v => (int)v)
            .ShouldBe([1, 2, 3, 10, 11, 12, 13, 20, 21, 22, 23, 24, 25, 30, 31, 32, 33]);
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

    [Fact]
    public void AnUnknownMember_IsRefusedRatherThanMapped()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => DmarcOutcomes.OutcomeOf((DmarcSkipReason)99));
        Should.Throw<ArgumentOutOfRangeException>(() => DmarcOutcomes.OutcomeOf((DmarcRejection)99));
        Should.Throw<ArgumentOutOfRangeException>(() => DmarcOutcomes.RunOutcomeOf((DmarcSessionFault)99));
    }
}
