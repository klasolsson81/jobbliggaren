using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.JobAds;
using Shouldly;
using static Jobbliggaren.Api.IntegrationTests.JobAds.JuniorClassificationHarness;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #2264 (ADR 0187) — the erasure and the junior sweep interleaved. The sweep reads its candidates in one statement and
/// writes each ad's assessment later, in a scope of its own; the erasure is an Api command in another process. An
/// erasure that commits in between finds no assessment to delete (or deletes the one the sweep is about to replace),
/// and the sweep then writes an assessment, stamped with the fingerprint of the erased text, onto the tombstone.
/// <c>ErasureCascadeRegistry</c> records <c>job_ad_junior_assessments.classified_fingerprint</c> as Erased because
/// "the erasure command deletes the whole assessment row with the ad".
/// </summary>
/// <remarks>
/// Both actors are the production ones (<see cref="JuniorClassificationHarness"/>); the seam is only the order, set by
/// <see cref="JuniorClassificationHarness.SweepAsync"/>'s <c>beforeTheFirstWrite</c>: the erasure runs to its commit
/// after the sweep's read and before its first per-ad scope, as two processes can order them.
/// </remarks>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class JuniorAssessmentErasureRaceTests(SharedPostgresFixture postgres) : IAsyncLifetime
{
    private readonly JuniorClassificationHarness _h = new();

    public ValueTask InitializeAsync() => _h.InitializeAsync(postgres);

    public ValueTask DisposeAsync() => _h.DisposeAsync(postgres);

    [Fact]
    public async Task AnErasureBetweenTheSweepsReadAndItsFirstWrite_ShouldLeaveNoAssessmentOnTheTombstone()
    {
        var erased = await _h.ImportAsync("Junior utvecklare", description: NamingTheRecruiter(NeutralText));

        await _h.SweepAsync(beforeTheFirstWrite: async () =>
            (await _h.EraseAsync(RecruiterName)).Erased.JobAds.ShouldBe(1));

        (await _h.JuniorInputsAsync(erased)).Status.ShouldBe(JobAdStatus.Erased);
        (await _h.AssessmentAsync(erased)).ShouldBeNull(
            "an assessment written after the erasure committed survives on the tombstone, stamped with a hash of the "
            + "erased text, and no later actor deletes it: the sweep never selects an erased ad again");
    }

    [Fact]
    public async Task AnErasureBetweenAReclassificationsReadAndItsWrite_ShouldLeaveNoAssessmentOnTheTombstone()
    {
        var erased = await _h.ImportAsync("Junior utvecklare", description: NamingTheRecruiter(NeutralText));
        (await _h.SweepAsync()).Total.ShouldBe(1);
        // A new rule file selects the ad again.
        _h.Rules = LoadRules(titleRuleIsAuto: false);
        _h.Clock.UtcNow = Now.AddMinutes(2);

        await _h.SweepAsync(beforeTheFirstWrite: async () =>
        {
            (await _h.EraseAsync(RecruiterName)).Erased.JobAds.ShouldBe(1);
            (await _h.AssessmentAsync(erased)).ShouldBeNull("the erasure deleted the assessment it found");
        });

        (await _h.JuniorInputsAsync(erased)).Status.ShouldBe(JobAdStatus.Erased);
        (await _h.AssessmentAsync(erased)).ShouldBeNull(
            "the re-classification finds no row and inserts a new one onto the tombstone");
    }
}
