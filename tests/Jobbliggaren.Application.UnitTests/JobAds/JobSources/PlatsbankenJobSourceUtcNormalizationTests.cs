using System.Runtime.CompilerServices;
using System.Text.Json;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Infrastructure.JobSources.Platsbanken;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.JobSources;

/// <summary>
/// ACL-level regression for JobTech's dates (ADR 0032 Amendment 2026-10-08). The hits are built
/// from wire JSON through the real converter, so an offsetless Swedish time must leave
/// <see cref="PlatsbankenJobSource"/> as the instant it names, at <c>Offset == 0</c> — the shape
/// Npgsql <c>timestamptz</c> requires. The expected instants are hard-coded UTC, so these hold on
/// any host zone.
///
/// <para>
/// OBS: <see cref="IJobTechStreamClient"/>/<see cref="IJobTechSearchClient"/> är
/// <c>internal</c> i Infrastructure. NSubstitute (Castle DynamicProxy) kan inte
/// proxy:a dem eftersom Infrastructure saknar
/// <c>[InternalsVisibleTo("DynamicProxyGenAssembly2")]</c>. Därför hand-skrivna
/// fakes nedan istället för <c>Substitute.For&lt;&gt;</c> — interfacen är synliga
/// för detta testprojekt via befintlig <c>InternalsVisibleTo</c>.
/// </para>
/// </summary>
public class PlatsbankenJobSourceUtcNormalizationTests
{
    // Wire values: 12:00 Swedish summer time (CEST, +02:00) is 10:00 UTC.
    private static readonly DateTime PublishedUtc = new(2026, 6, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ExpiresUtc = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);

    private static readonly DateTimeOffset FakeNow =
        new(2026, 6, 6, 0, 0, 0, TimeSpan.Zero);

    private static JobTechHit ValidHit(
        string id = "ext-utc-1",
        bool removed = false) =>
        JsonSerializer.Deserialize<JobTechHit>($$"""
            {
              "id": "{{id}}",
              "headline": "Backend-utvecklare",
              "description": { "text": "Beskrivning av tjänsten." },
              "employer": { "name": "Klarna" },
              "webpage_url": "https://arbetsformedlingen.se/platsbanken/annonser/{{id}}",
              "publication_date": "2026-06-06T12:00:00",
              "last_publication_date": "2026-07-06T12:00:00",
              "removed": {{(removed ? "true" : "false")}}
            }
            """)!;

    private static PlatsbankenJobSource CreateSut(
        IJobTechStreamClient? streamClient = null,
        IJobTechSearchClient? searchClient = null) =>
        new(
            streamClient ?? new FakeStreamClient(),
            searchClient ?? new FakeSearchClient(),
            new FakeDateTimeProvider(FakeNow),
            NullLogger<PlatsbankenJobSource>.Instance);

    [Fact]
    public async Task RefetchByExternalIdAsync_ReturnsTheSwedishWireTimes_AsUtcInstants()
    {
        var searchClient = new FakeSearchClient(ValidHit("ext-utc-1"));
        var sut = CreateSut(searchClient: searchClient);

        var item = await sut.RefetchByExternalIdAsync(
            "ext-utc-1", TestContext.Current.CancellationToken);

        item.ShouldNotBeNull();
        item.PublishedAt.Offset.ShouldBe(TimeSpan.Zero);
        item.PublishedAt.UtcDateTime.ShouldBe(PublishedUtc);
        item.ExpiresAt.ShouldNotBeNull();
        item.ExpiresAt.Value.Offset.ShouldBe(TimeSpan.Zero);
        item.ExpiresAt.Value.UtcDateTime.ShouldBe(ExpiresUtc);
    }

    [Fact]
    public async Task StreamChangesAsync_ReturnsUpsertItemAndOccurredAt_AsUtcInstants()
    {
        var streamClient = new FakeStreamClient(ValidHit("ext-upsert", removed: false));
        var sut = CreateSut(streamClient: streamClient);

        var changes = new List<JobAdChange>();
        await foreach (var change in sut.StreamChangesAsync(
            FakeNow, TestContext.Current.CancellationToken))
        {
            changes.Add(change);
        }

        var upsert = changes.ShouldHaveSingleItem().ShouldBeOfType<JobAdUpsert>();

        // Item-grenen (TryConvertToImportItem).
        upsert.Item.PublishedAt.Offset.ShouldBe(TimeSpan.Zero);
        upsert.Item.PublishedAt.UtcDateTime.ShouldBe(PublishedUtc);
        upsert.Item.ExpiresAt.ShouldNotBeNull();
        upsert.Item.ExpiresAt.Value.Offset.ShouldBe(TimeSpan.Zero);
        upsert.Item.ExpiresAt.Value.UtcDateTime.ShouldBe(ExpiresUtc);

        // occurredAt-grenen (null-coalesce-kedjan i StreamChangesAsync).
        // last_publication_date är satt → den vinner i kedjan.
        upsert.OccurredAt.Offset.ShouldBe(TimeSpan.Zero);
        upsert.OccurredAt.UtcDateTime.ShouldBe(ExpiresUtc);
    }

    [Fact]
    public async Task StreamChangesAsync_ReturnsRemovalOccurredAt_AsUtcInstant()
    {
        var streamClient = new FakeStreamClient(ValidHit("ext-removed", removed: true));
        var sut = CreateSut(streamClient: streamClient);

        var changes = new List<JobAdChange>();
        await foreach (var change in sut.StreamChangesAsync(
            FakeNow, TestContext.Current.CancellationToken))
        {
            changes.Add(change);
        }

        var removal = changes.ShouldHaveSingleItem().ShouldBeOfType<JobAdRemoval>();

        removal.OccurredAt.Offset.ShouldBe(TimeSpan.Zero);
        removal.OccurredAt.UtcDateTime.ShouldBe(ExpiresUtc);
    }

    // Hand-skrivna fakes — internal-interfacen kan inte NSubstitute-proxy:as
    // (saknar DynamicProxyGenAssembly2-grant i Infrastructure). De är synliga
    // för detta testprojekt via InternalsVisibleTo.

    private sealed class FakeSearchClient(JobTechHit? hit = null) : IJobTechSearchClient
    {
        public Task<JobTechHit?> GetAdByIdAsync(
            string id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(hit);

        // #551 — no remote ads harvested (empty set). This test does not exercise the remote facet.
        public Task<JobTechSearchListResponse> SearchRemoteAsync(
            int offset, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult(new JobTechSearchListResponse
            {
                Total = new JobTechSearchTotal { Value = 0 },
                Hits = [],
            });
    }

    private sealed class FakeStreamClient(params JobTechHit[] hits) : IJobTechStreamClient
    {
        public IAsyncEnumerable<JobTechHit> FetchSnapshotAsync(
            CancellationToken cancellationToken) =>
            Yield(hits, cancellationToken);

        public IAsyncEnumerable<JobTechHit> StreamChangesAsync(
            DateTimeOffset since,
            CancellationToken cancellationToken) =>
            Yield(hits, cancellationToken);

        private static async IAsyncEnumerable<JobTechHit> Yield(
            JobTechHit[] items,
            [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }
        }
    }
}
