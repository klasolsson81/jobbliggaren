using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.RecentSearches;

/// <summary>
/// ADR 0188 (#2265, security-auditor's #2265 plan-read Major 1) — the store bounds the marked searches on its own: the
/// partial unique index admits each slot once per job seeker, so no race marks more searches than there are slots.
/// <see cref="QuickLinkCap"/> is asked per request, which a second request can overtake; the race below is that second
/// request, the real <c>MarkRecentSearchQuickLinkCommandHandler</c> committing between the first one's read of the held
/// slots and its save. Its own database per test, because the exact set of slots is the assertion.
/// </summary>
/// <remarks>
/// The CHECK's bound is pinned from the catalog rather than by writing a slot outside it: no path in <c>src/</c>
/// produces one, because <c>RecentJobSearch.MarkAsQuickLink</c> refuses it
/// (<c>RecentJobSearchTests.MarkAsQuickLink_ASlotOutsideTheCap_Throws</c>) and the handler only ever passes
/// <see cref="QuickLinkCap.FreeSlot"/>'s answer.
/// </remarks>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class RecentSearchQuickLinkStoreBoundTests(SharedPostgresFixture postgres) : IAsyncLifetime
{
    private const string SlotIndex = "ux_recent_job_searches_seeker_quick_link_slot";
    private const string SlotCheck = "ck_recent_job_searches_quick_link_slot";

    private readonly RecentSearchQuickLinkHarness _h = new();

    public ValueTask InitializeAsync() => _h.InitializeAsync(postgres);

    public ValueTask DisposeAsync() => _h.DisposeAsync(postgres);

    [Fact]
    public async Task Mark_ShouldBeASlotTakenConflict_WhenARacingMarkTookTheFreeSlotFirst_AndTheStoreHoldsThreeMarks()
    {
        (await _h.SetJuniorLinkAsync(enabled: false)).IsSuccess.ShouldBeTrue();
        var searches = await _h.CaptureSearchesAsync(4, "sok");
        (await _h.MarkAsync(searches[0])).IsSuccess.ShouldBeTrue();
        (await _h.MarkAsync(searches[1])).IsSuccess.ShouldBeTrue();
        Result? racing = null;
        _h.Seams.AfterTheHeldSlotsRead(async () => racing = await _h.MarkAsync(searches[3]));

        var result = await _h.MarkAsync(searches[2]);

        racing.ShouldNotBeNull().IsSuccess.ShouldBeTrue(
            "the premise: a second mark read the same two held slots, took the free one, and committed first");
        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        result.Error.Code.ShouldBe("QuickLink.SlotTaken");
        _h.Seams.UniqueViolations.ShouldBe([SlotIndex], "the slot index refused the write, and nothing else did");
        (await _h.RowsAsync()).Select(r => r.QuickLinkSlot).ShouldBe(new int?[] { 1, 2, null, 3 });
    }

    [Fact]
    public async Task Mark_ShouldTakeTheFreedSlot_WhenTheRetryFollowsTheConflict()
    {
        (await _h.SetJuniorLinkAsync(enabled: false)).IsSuccess.ShouldBeTrue();
        var searches = await _h.CaptureSearchesAsync(3, "sok");
        (await _h.MarkAsync(searches[0])).IsSuccess.ShouldBeTrue();
        _h.Seams.AfterTheHeldSlotsRead(async () => (await _h.MarkAsync(searches[2])).IsSuccess.ShouldBeTrue());
        (await _h.MarkAsync(searches[1])).Error.Code.ShouldBe("QuickLink.SlotTaken");

        // The 409 asks the user to try again; the retry reads the slots afresh.
        var retry = await _h.MarkAsync(searches[1]);

        retry.IsSuccess.ShouldBeTrue();
        (await _h.RowsAsync()).Select(r => r.QuickLinkSlot).ShouldBe(new int?[] { 1, 3, 2 });
    }

    [Fact]
    public async Task TheStore_ShouldAdmitSlotsOneToMaxTotal_AndEachSlotOncePerSeeker()
    {
        var check = await _h.CatalogValueAsync(
            """SELECT pg_get_constraintdef(oid) AS "Value" FROM pg_constraint WHERE conname = {0}""", SlotCheck);
        var index = await _h.CatalogValueAsync(
            """SELECT indexdef AS "Value" FROM pg_indexes WHERE schemaname = 'public' AND indexname = {0}""", SlotIndex);

        check.ShouldBe($"CHECK (((quick_link_slot >= 1) AND (quick_link_slot <= {QuickLinkCap.MaxTotal})))");
        index.ShouldBe(
            $"CREATE UNIQUE INDEX {SlotIndex} ON public.recent_job_searches USING btree (job_seeker_id, quick_link_slot) "
            + "WHERE (quick_link_slot IS NOT NULL)");
    }
}
