using System.Runtime.CompilerServices;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Infrastructure.JobSources.Platsbanken;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.JobSources;

/// <summary>
/// Regressionslås för ADR 0066 timezone-bugg (senior-cto-advisor-beslut
/// 2026-06-06). JobTech-datum (<c>publication_date</c>/<c>last_publication_date</c>,
/// <see cref="DateTimeOffset"/>?) deserialiseras av System.Text.Json med LOKAL
/// maskin-offset (+02:00 i Sverige). De skrevs tidigare orörda till
/// <c>job_ads.published_at</c>/<c>expires_at</c> (Postgres <c>timestamptz</c>),
/// vilket Npgsql avvisar — kravet är Offset=0/UTC. Buggen var osynlig på Fargate
/// (UTC-värd) men failade lokalt.
///
/// <para>
/// Fixen i <see cref="PlatsbankenJobSource"/> är <c>.ToUniversalTime()</c> på
/// <c>publishedAt</c>/<c>expiresAt</c> (TryConvertToImportItem) och hela
/// <c>occurredAt</c>-null-coalesce-kedjan (StreamChangesAsync).
/// </para>
///
/// <para>
/// Assertet <c>Offset == TimeSpan.Zero</c> ÄR regressionslåset — det är
/// ekvivalent med Npgsql timestamptz-kravet. Ingen riktig Postgres behövs:
/// om någon tar bort <c>.ToUniversalTime()</c> kvarstår +02:00-offseten och
/// dessa tester blir röda, exakt som Npgsql skulle ha avvisat värdet i prod.
/// </para>
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
    // Input med +02:00 (svensk sommartid) — exakt den offset System.Text.Json
    // tilldelar på en svensk dev-maskin. 12:00 +02:00 == 10:00 UTC.
    private static readonly DateTimeOffset PublishedLocal =
        new(2026, 6, 6, 12, 0, 0, TimeSpan.FromHours(2));

    private static readonly DateTimeOffset ExpiresLocal =
        new(2026, 7, 6, 12, 0, 0, TimeSpan.FromHours(2));

    // clock.UtcNow är redan UTC — fakad till valfri UTC-tid.
    private static readonly DateTimeOffset FakeNow =
        new(2026, 6, 6, 0, 0, 0, TimeSpan.Zero);

    private static JobTechHit ValidHit(
        string id = "ext-utc-1",
        bool removed = false) => new()
        {
            Id = id,
            Headline = "Backend-utvecklare",
            Description = new JobTechDescription { Text = "Beskrivning av tjänsten." },
            Employer = new JobTechEmployer { Name = "Klarna" },
            WebpageUrl = "https://arbetsformedlingen.se/platsbanken/annonser/" + id,
            PublicationDate = PublishedLocal,
            LastPublicationDate = ExpiresLocal,
            Removed = removed,
        };

    private static PlatsbankenJobSource CreateSut(
        IJobTechStreamClient? streamClient = null,
        IJobTechSearchClient? searchClient = null) =>
        new(
            streamClient ?? new FakeStreamClient(),
            searchClient ?? new FakeSearchClient(),
            new FakeDateTimeProvider(FakeNow),
            NullLogger<PlatsbankenJobSource>.Instance);

    [Fact]
    public async Task RefetchByExternalIdAsync_ShouldNormalizeDatesToUtc_WhenSourceReturnsLocalOffset()
    {
        var searchClient = new FakeSearchClient(ValidHit("ext-utc-1"));
        var sut = CreateSut(searchClient: searchClient);

        var item = await sut.RefetchByExternalIdAsync(
            "ext-utc-1", TestContext.Current.CancellationToken);

        item.ShouldNotBeNull();

        // Regressionslås: Npgsql timestamptz kräver Offset=0.
        item.PublishedAt.Offset.ShouldBe(TimeSpan.Zero);
        item.ExpiresAt.ShouldNotBeNull();
        item.ExpiresAt.Value.Offset.ShouldBe(TimeSpan.Zero);

        // Instanten bevaras — UTC-normalisering ändrar offset, inte tidpunkt.
        // 12:00 +02:00 == 10:00 UTC.
        item.PublishedAt.UtcDateTime.ShouldBe(PublishedLocal.UtcDateTime);
        item.PublishedAt.UtcDateTime.ShouldBe(
            new DateTime(2026, 6, 6, 10, 0, 0, DateTimeKind.Utc));
        item.ExpiresAt.Value.UtcDateTime.ShouldBe(ExpiresLocal.UtcDateTime);
    }

    [Fact]
    public async Task StreamChangesAsync_ShouldNormalizeUpsertItemAndOccurredAtToUtc_WhenSourceReturnsLocalOffset()
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
        upsert.Item.ExpiresAt.ShouldNotBeNull();
        upsert.Item.ExpiresAt.Value.Offset.ShouldBe(TimeSpan.Zero);

        // occurredAt-grenen (null-coalesce-kedjan i StreamChangesAsync).
        // last_publication_date är satt → den vinner i kedjan.
        upsert.OccurredAt.Offset.ShouldBe(TimeSpan.Zero);
        upsert.OccurredAt.UtcDateTime.ShouldBe(ExpiresLocal.UtcDateTime);
    }

    [Fact]
    public async Task StreamChangesAsync_ShouldNormalizeRemovalOccurredAtToUtc_WhenRemovalHitHasLocalOffset()
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

        // occurredAt-grenen för removal — last_publication_date (+02:00) → UTC.
        removal.OccurredAt.Offset.ShouldBe(TimeSpan.Zero);
        removal.OccurredAt.UtcDateTime.ShouldBe(ExpiresLocal.UtcDateTime);
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
