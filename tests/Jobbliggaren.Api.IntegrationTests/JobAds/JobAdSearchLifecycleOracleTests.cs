using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #864 follow-up (B4) — THE LIFECYCLE WITNESS AT THE ANON SPOT GATE. Pins the single
/// <c>Status == JobAdStatus.Active</c> predicate in <see cref="JobAdSearchComposition.ApplyFilter"/>
/// (ADR 0039 Beslut 1 SPOT; ADR 0032-amendment 2026-05-23) as an ALLOW-LIST on the row where an
/// allow-list and a deny-list actually disagree.
/// <para>
/// <b>Why an ERASED row is the witness.</b> #864 recorded the mutation <c>== Active</c> →
/// <c>!= Archived</c> as an expected SURVIVOR: the two predicates were extensionally identical on
/// every reachable row while the only third status (<c>Expired</c>) had no writer, and fabricating
/// one was the #843 fiction the issue itself forbade. #886 retired <c>Expired</c> and left
/// <c>Erased</c> (#842, a REAL Art. 17 transition) as the reachable row where they differ:
/// <c>Erased != Archived</c> is TRUE, so the deny-list mutant would return a GDPR tombstone —
/// empty title, company "[raderad]" — in the public /jobb list. This class kills that mutant.
/// </para>
/// <para>
/// <b>Attribution.</b> <c>JobAd.Erase()</c> empties the text carriers and nulls org.nr, but the six
/// <c>*_concept_id</c> facet columns are ordinary written columns and SURVIVE (the tombstone keeps
/// its taxonomy codes — they disclose nothing about the recruiter). The erased seed below carries
/// the same unique occupation-group as the live ads, and the test READS IT BACK from a fresh
/// context: the erased row provably still matches the facet criteria, so its exclusion is
/// attributable to the status gate ALONE — not to a facet mismatch, not to a broken seed.
/// </para>
/// <para>
/// <b>Real Postgres, never EF-InMemory:</b> the gate composes a value-converted record equality
/// (<c>HasConversion</c> to varchar) with facet <c>IN(...)</c> over CLR-mapped columns; only the
/// real engine proves that translation (memory <c>ef_strongly_typed_vo_contains</c>). One witness
/// here covers every ApplyFilter carrier by construction — ListJobAds, RunSavedSearch,
/// ListRecentSearches CountAsync, facet-counts and the three per-user paths share the one
/// predicate line (the facet-count carrier is additionally pinned by its own erased sibling in
/// <see cref="JobAdFacetCountsTests"/>).
/// </para>
/// </summary>
[Collection("Api")]
public class JobAdSearchLifecycleOracleTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static JobAdSearchQuery CreateSut(IServiceScope scope) =>
        new(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            Substitute.For<IOccupationSynonymExpander>());

    // Seeds an imported ad tagged with the run's unique occupation-group (isolation key in the
    // shared [Collection("Api")] Postgres). Lifecycle transitions run through the REAL domain
    // methods — never a fabricated column stamp (#843 / #864 AC 4).
    private async Task<JobAdId> SeedAsync(
        string title, string occupationGroup, CancellationToken ct,
        bool archived = false, bool erased = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var externalId = $"lifecycle-{Guid.NewGuid():N}";
        var rawPayload =
            $"{{\"id\":\"{externalId}\","
            + $"\"occupation_group\":{{\"concept_id\":\"{occupationGroup}\"}}}}";

        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create("Test Company AB").Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: clock.UtcNow.AddDays(-1),
            expiresAt: clock.UtcNow.AddDays(30),
            clock: clock, declaredContacts: []).Value;

        if (archived)
            jobAd.Archive(clock).IsSuccess.ShouldBeTrue("Archive-seeden får inte tyst misslyckas");

        if (erased)
            jobAd.Erase(clock).IsSuccess.ShouldBeTrue("Erase-seeden får inte tyst misslyckas");

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    private static JobAdFilterCriteria CriteriaFor(string occupationGroup) => new(
        OccupationGroup: [occupationGroup],
        Municipality: [],
        Region: [],
        EmploymentType: [],
        WorktimeExtent: [],
        Employer: [],
        Remote: false,
        Q: null);

    // =================================================================
    // THE WITNESS — SearchAsync returns EXACTLY the Active ads; the erased tombstone is the row
    // that separates the allow-list from a deny-list.
    //
    // ASYMMETRIC SEED (2 Active + 1 Archived + 1 Erased) — every mutant state separates on BOTH
    // membership and count: correct → {a1,a2}/2 · gate deleted → 4 · deny-list (!= Archived) →
    // 3 with the TOMBSTONE in the list · inverted (== Archived) → 1 and the wrong member. The
    // Archived row is the counterfactual axis the deny-list mutant still excludes — without it,
    // this test could not tell a deny-list from the correct gate going red for seed reasons.
    // =================================================================

    [Fact]
    public async Task SearchAsync_ReturnsOnlyActiveAds_TheErasedTombstoneIsExcludedByTheAllowList()
    {
        var ct = TestContext.Current.CancellationToken;
        var grp = $"grp{Guid.NewGuid():N}"[..16];

        var active1 = await SeedAsync("Aktiv roll 1", grp, ct);
        var active2 = await SeedAsync("Aktiv roll 2", grp, ct);
        var archived = await SeedAsync("Arkiverad roll", grp, ct, archived: true);
        var erased = await SeedAsync("Raderad roll", grp, ct, erased: true);

        // SEED READ-BACK through a FRESH context (fail-loud, #886 M2-class): the erased row IS an
        // Erased-status row AND still carries the run's facet — the two facts that make its
        // absence below attributable to the status gate alone. A silently-broken Erase() (or a
        // facet that did not survive it) degrades this test to "active ads are returned"; these
        // asserts make that degradation loud instead of green.
        using (var verifyScope = _factory.Services.CreateScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var erasedRow = await verifyDb.JobAds.AsNoTracking()
                .SingleAsync(j => j.Id == erased, ct);
            erasedRow.Status.ShouldBe(JobAdStatus.Erased);
            erasedRow.OccupationGroupConceptId.ShouldBe(grp,
                "facetten ska ÖVERLEVA Erase() — annars lämnar tombstonen frågan via facett-" +
                "filtret och vittnar inte om status-grinden");
            var archivedRow = await verifyDb.JobAds.AsNoTracking()
                .SingleAsync(j => j.Id == archived, ct);
            archivedRow.Status.ShouldBe(JobAdStatus.Archived);
        }

        using var scope = _factory.Services.CreateScope();
        var sut = CreateSut(scope);

        var page = await sut.SearchAsync(
            new JobAdSearchCriteria(CriteriaFor(grp), JobAdSortBy.PublishedAtDesc, Page: 1, PageSize: 100), ct);

        // EXACT membership — not ShouldNotContain alone: an inverted gate returns the WRONG member
        // and a deleted gate returns extra members; both fail set-equality, neither can hide in a
        // weaker assertion.
        page.Items.Select(i => i.Id).ToHashSet().ShouldBe(
            new HashSet<Guid> { active1.Value, active2.Value }, ignoreOrder: true,
            "slutanvändar-sökvägen ska returnera EXAKT de aktiva annonserna (ADR 0032-amendment: " +
            "allow-list == Active) — en deny-list (!= Archived) hade returnerat Art. 17-tombstonen " +
            "(tom titel, företag '[raderad]') i den publika /jobb-listan");

        page.TotalCount.ShouldBe(2,
            "TotalCount räknas genom SAMMA ApplyFilter-SPOT (separat count-query) — 2, inte 3 " +
            "(deny-list/raderad grind) och inte 1 (inverterad grind)");
    }
}
