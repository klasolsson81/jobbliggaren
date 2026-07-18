using System.Runtime.CompilerServices;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Infrastructure.JobSources.Platsbanken;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.JobSources;

/// <summary>
/// #551 — the remote/distans HARVEST (CTO D1, dotnet-architect + code-reviewer Major). The downstream
/// scorer/oracle tests seed remote via <c>TestFacets.From(remote:true)</c> straight through
/// <c>JobAd.Import</c> and bypass the ACL entirely; this file is the ONLY coverage of the half that fills
/// the column — <c>PlatsbankenJobSource.TryFetchRemoteIdSetAsync</c> + the set-membership mapping in
/// <c>MapFacets</c>. The fail-safe (a failed OR empty harvest must never flip the corpus to false) is the
/// most dangerous single behaviour in the PR — a regression here would silently un-remote every ad — so it
/// is pinned here (MEMORY: the fix for an untested guarantee is itself a guarantee).
/// </summary>
public class PlatsbankenRemoteHarvestTests
{
    private static readonly DateTimeOffset FakeNow = new(2026, 7, 18, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Published = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private static JobTechHit ValidHit(string id) => new()
    {
        Id = id,
        Headline = "Backend-utvecklare",
        Description = new JobTechDescription { Text = "Beskrivning av tjänsten." },
        Employer = new JobTechEmployer { Name = "Test Company AB" },
        WebpageUrl = "https://arbetsformedlingen.se/platsbanken/annonser/" + id,
        PublicationDate = Published,
    };

    private static PlatsbankenJobSource CreateSut(
        IJobTechStreamClient streamClient, IJobTechSearchClient searchClient) =>
        new(streamClient, searchClient, new FakeDateTimeProvider(FakeNow),
            NullLogger<PlatsbankenJobSource>.Instance);

    private static async Task<List<JobAdImportItem>> SnapshotAsync(
        PlatsbankenJobSource sut, CancellationToken ct)
    {
        var items = new List<JobAdImportItem>();
        await foreach (var item in sut.FetchSnapshotAsync(new SnapshotOutcomeRecorder(), ct))
        {
            items.Add(item);
        }

        return items;
    }

    [Fact]
    public async Task FetchSnapshot_SetsRemoteFromHarvestMembership_TrueInSet_FalseOutside()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = CreateSut(
            new FakeStreamClient(ValidHit("in-set"), ValidHit("out-of-set")),
            new HarvestSearchClient(remoteIds: ["in-set"]));

        var items = await SnapshotAsync(sut, ct);

        items.Single(i => i.ExternalId == "in-set").Facets.Remote
            .ShouldBe(true, "an id AF classifies as remote maps to Remote=true");
        items.Single(i => i.ExternalId == "out-of-set").Facets.Remote
            .ShouldBe(false, "an id NOT in the harvest set maps to Remote=false (a successful harvest is authoritative)");
    }

    [Fact]
    public async Task FetchSnapshot_HarvestThrows_LeavesRemoteNull_NeverFlipsCorpusToFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        var sut = CreateSut(
            new FakeStreamClient(ValidHit("id-1")),
            new HarvestSearchClient(throwOnSearch: true));

        var items = await SnapshotAsync(sut, ct);

        items.Single().Facets.Remote.ShouldBeNull(
            "a FAILED harvest returns null → MapFacets emits Remote=null (preserve) → SetSourcePayload keeps " +
            "the ad's current value. The corpus is NEVER flipped to false (the #552 hole stays closed).");
    }

    [Fact]
    public async Task FetchSnapshot_HarvestSucceedsButEmpty_LeavesRemoteNull_TreatsZeroAsAnomaly()
    {
        var ct = TestContext.Current.CancellationToken;
        // A 200 with zero hits — the one edge the exception fail-safe does not cover (architect Note 1).
        var sut = CreateSut(
            new FakeStreamClient(ValidHit("id-1")),
            new HarvestSearchClient(remoteIds: []));

        var items = await SnapshotAsync(sut, ct);

        items.Single().Facets.Remote.ShouldBeNull(
            "a successful-but-empty harvest (AF's ~660-ad corpus suddenly 0 = an anomaly) is treated as a " +
            "failed harvest → null (preserve), never an empty set that would flip every ad to false.");
    }

    [Fact]
    public async Task RemoteHarvest_PaginatesBeyondPageSize_FindsAnIdOnTheSecondPage()
    {
        var ct = TestContext.Current.CancellationToken;
        // 150 remote ids → two pages (PageSize 100). The membership id lives on page 2 (index 120), so it is
        // only found if the harvest actually paginates.
        var remoteIds = Enumerable.Range(0, 150).Select(i => $"remote-{i:D3}").ToArray();
        var pageTwoId = remoteIds[120];
        var sut = CreateSut(
            new FakeStreamClient(ValidHit(pageTwoId), ValidHit("not-remote")),
            new HarvestSearchClient(remoteIds: remoteIds));

        var items = await SnapshotAsync(sut, ct);

        items.Single(i => i.ExternalId == pageTwoId).Facets.Remote
            .ShouldBe(true, "an id on the second harvest page must still be found → the harvest paginates");
        items.Single(i => i.ExternalId == "not-remote").Facets.Remote.ShouldBe(false);
    }

    [Fact]
    public async Task StreamChanges_DoesNotHarvest_LeavesRemoteNull_SnapshotOwnsRemote()
    {
        var ct = TestContext.Current.CancellationToken;
        // Snapshot-only ownership (D1): the 10-min stream never consults the harvest set, so a streamed ad
        // reads Remote=null (preserve) → the next nightly snapshot reconciles it (≤24h eventual consistency).
        var sut = CreateSut(
            new FakeStreamClient(ValidHit("streamed")),
            new HarvestSearchClient(remoteIds: ["streamed"])); // even if the set WOULD contain it

        var changes = new List<JobAdChange>();
        await foreach (var change in sut.StreamChangesAsync(FakeNow, ct))
        {
            changes.Add(change);
        }

        var upsert = changes.ShouldHaveSingleItem().ShouldBeOfType<JobAdUpsert>();
        upsert.Item.Facets.Remote.ShouldBeNull(
            "the stream path passes no harvest set → Remote=null (preserve); the snapshot owns remote");
    }

    // A harvest-capable search client: paginates an injected remote-id list exactly like the real
    // JobSearch client (Skip/Take over the ids, Total = full count), or throws to exercise the fail-safe.
    private sealed class HarvestSearchClient(
        IReadOnlyList<string>? remoteIds = null, bool throwOnSearch = false) : IJobTechSearchClient
    {
        public Task<JobTechHit?> GetAdByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<JobTechHit?>(null);

        public Task<JobTechSearchListResponse> SearchRemoteAsync(
            int offset, int limit, CancellationToken cancellationToken = default)
        {
            if (throwOnSearch)
            {
                throw new HttpRequestException("harvest fetch failed (simulated)");
            }

            var all = remoteIds ?? [];
            var hits = all.Skip(offset).Take(limit).Select(id => new JobTechHit { Id = id }).ToList();
            return Task.FromResult(new JobTechSearchListResponse
            {
                Total = new JobTechSearchTotal { Value = all.Count },
                Hits = hits,
            });
        }
    }

    private sealed class FakeStreamClient(params JobTechHit[] hits) : IJobTechStreamClient
    {
        public IAsyncEnumerable<JobTechHit> FetchSnapshotAsync(CancellationToken cancellationToken) =>
            Yield(hits, cancellationToken);

        public IAsyncEnumerable<JobTechHit> StreamChangesAsync(
            DateTimeOffset since, CancellationToken cancellationToken) =>
            Yield(hits, cancellationToken);

        private static async IAsyncEnumerable<JobTechHit> Yield(
            JobTechHit[] items, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }

            await Task.CompletedTask;
        }
    }
}
