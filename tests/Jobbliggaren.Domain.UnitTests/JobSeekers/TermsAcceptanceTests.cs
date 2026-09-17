using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobSeekers;

/// <summary>
/// #1736 (ADR 0142 D6) — the one total factory on <see cref="TermsAcceptance"/>. The constants'
/// LITERAL values are not pinned here; that is
/// <c>TermsAcceptanceVersionsMatchPublishedPolicyTests</c>' subject, which reads them against the
/// published copy. What this file pins is what the factory does with them and with the clock.
/// </summary>
public class TermsAcceptanceTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;

    [Fact]
    public void AcceptCurrent_WithAClock_StampsThatClocksNow()
    {
        var acceptance = TermsAcceptance.AcceptCurrent(Clock);

        acceptance.AcceptedAt.ShouldBe(Clock.UtcNow);
    }

    [Fact]
    public void AcceptCurrent_WithAClock_StampsBothPublishedVersions()
    {
        // Asserted against the two constants rather than their literal dates: a version bump then
        // does not have to be edited here as well, and because the constants differ from each other
        // a factory that passed them in the wrong order still fails.
        var acceptance = TermsAcceptance.AcceptCurrent(Clock);

        acceptance.TermsVersion.ShouldBe(TermsAcceptance.CurrentTermsVersion);
        acceptance.PrivacyPolicyVersion.ShouldBe(TermsAcceptance.CurrentPrivacyPolicyVersion);
    }

    [Fact]
    public void AcceptCurrent_TwiceOnTheSameClock_ProducesEqualStamps()
    {
        var first = TermsAcceptance.AcceptCurrent(Clock);
        var second = TermsAcceptance.AcceptCurrent(Clock);

        second.ShouldBe(first);
        second.GetHashCode().ShouldBe(first.GetHashCode());
    }

    [Fact]
    public void AcceptCurrent_OnTwoClocksReadingDifferentInstants_ProducesUnequalStamps()
    {
        // Without this arm the equality case above would hold just as well for a record whose value
        // equality ignored AcceptedAt entirely — the member carrying the whole Art. 5(2) fact.
        var first = TermsAcceptance.AcceptCurrent(Clock);

        var later = TermsAcceptance.AcceptCurrent(FakeDateTimeProvider.At(Clock.UtcNow.AddSeconds(1)));

        later.ShouldNotBe(first);
    }
}
