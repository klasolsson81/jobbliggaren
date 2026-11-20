using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Jobs.ClassifyJuniorJobAds;
using Jobbliggaren.Domain.JobAds;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.JobAds.JuniorClassificationHarness;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #2264 (ADR 0187) — an Art. 17 erasure takes the erased ad's junior assessment with it, and the ad's fingerprint (a
/// hash of the erased text): <c>EraseRecruiterAdsCommand</c> through its handler, dry run first and the ads it returned
/// confirmed by id, as <c>RecruiterErasureIngestTests</c> drives it. Its own database per test, because the exact set of
/// assessments left is the assertion.
/// </summary>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class JuniorAssessmentErasureTests(SharedPostgresFixture postgres) : IAsyncLifetime
{
    private readonly JuniorClassificationHarness _h = new();

    public ValueTask InitializeAsync() => _h.InitializeAsync(postgres);

    public ValueTask DisposeAsync() => _h.DisposeAsync(postgres);

    [Fact]
    public async Task Erase_ShouldDeleteTheErasedAdsAssessment_AndNullItsFingerprint_AndLeaveEveryOtherAd()
    {
        var erased = await _h.ImportAsync("Junior utvecklare", description: NamingTheRecruiter(NeutralText));
        var kept = await _h.ImportAsync("Junior utvecklare");
        (await _h.SweepAsync()).Total.ShouldBe(2);
        // A decision about the ad, so the row carries the admin's half too.
        (await _h.DecideAsync(erased, JuniorDecision.NotJunior)).IsSuccess.ShouldBeTrue();
        (await _h.JuniorInputsAsync(erased)).Fingerprint.ShouldNotBeNull();
        var keptFingerprint = (await _h.JuniorInputsAsync(kept)).Fingerprint;

        _h.Commands.Clear();
        var response = await _h.EraseAsync(RecruiterName);

        response.Erased.JobAds.ShouldBe(1);
        _h.Commands.WrittenTables.ShouldContain("job_ad_junior_assessments");
        (await _h.AssessmentAsync(erased)).ShouldBeNull();
        var tombstone = await _h.JuniorInputsAsync(erased);
        tombstone.Status.ShouldBe(JobAdStatus.Erased);
        tombstone.Fingerprint.ShouldBeNull();

        (await _h.AssessmentsAsync()).Select(a => a.Id.Value).ShouldBe([kept]);
        (await _h.AssessmentAsync(kept)).ShouldNotBeNull().ClassifiedFingerprint.ShouldBe(keptFingerprint);
        (await _h.JuniorInputsAsync(kept)).Fingerprint.ShouldBe(keptFingerprint);
    }

    [Fact]
    public async Task TheSweep_ShouldNeverAssessAnErasedAdAgain()
    {
        var erased = await _h.ImportAsync("Junior utvecklare", description: NamingTheRecruiter(MentorText));
        (await _h.SweepAsync()).Total.ShouldBe(1);
        await _h.EraseAsync(RecruiterName);

        // A new rule file would select every listed ad in the field again; the tombstone is not listed.
        _h.Rules = LoadRules(titleRuleIsAuto: false);
        _h.Clock.UtcNow = Now.AddMinutes(2);

        (await _h.SweepAsync()).ShouldBe(ClassifyJuniorJobAdsCounts.None);
        (await _h.AssessmentAsync(erased)).ShouldBeNull();
    }
}
