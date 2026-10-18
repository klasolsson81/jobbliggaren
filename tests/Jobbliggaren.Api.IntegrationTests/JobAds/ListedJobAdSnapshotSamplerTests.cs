using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Jobs.ExpireJobAds;
using Jobbliggaren.Application.JobAds.Listing;
using Jobbliggaren.Application.JobAds.Publication;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// The listed-ad sampler on real Postgres (ADR 0174): <see cref="JobAdPublication.IsListed"/> translates, and what it
/// admits is exactly what <see cref="JobAdPublication.Classify"/> calls Listed over the same rows, including the NULL
/// deadline, the NULL provenance and the deadline instant, where SQL's three-valued logic and C# can part. Its own
/// database per test, because the exact set is the assertion.
/// </summary>
/// <remarks>
/// Instants are whole seconds and the step is one microsecond, the resolution of <c>timestamptz</c>. Every row is
/// produced the way production produces it: imported through <c>JobAd.Import</c>, created as
/// <c>POST /api/v1/job-ads</c> stores it, archived and erased through the aggregate, and the never-imported row past
/// its deadline archived by the real <c>ExpireJobAdsJob</c>.
/// </remarks>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class ListedJobAdSnapshotSamplerTests(SharedPostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly MutableFakeDateTimeProvider _clock = new() { UtcNow = Now };
    private string _connectionString = string.Empty;
    private ServiceProvider _provider = default!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(_connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        _provider = services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await postgres.DropDatabaseAsync(_connectionString);
    }

    private ListedJobAdSnapshotSampler Sampler(ILogger<ListedJobAdSnapshotSampler>? logger = null, int? ceiling = null) =>
        new(_provider.GetRequiredService<IServiceScopeFactory>(), _clock,
            logger ?? NullLogger<ListedJobAdSnapshotSampler>.Instance, ceiling ?? ListedJobAdSnapshotLimits.Ceiling);

    [Fact]
    public async Task Sample_PublishesExactlyTheAdsClassifyCallsListed_InTheirParts_InIdOrder()
    {
        var archivedPastDeadline = await AddAsync(NeverImported(Now.AddMicroseconds(-1)));
        await RunArchiverAsync(Now);
        var noDeadline = await AddAsync(Imported(null));
        var atTheInstant = await AddAsync(Imported(Now));
        var afterTheInstant = await AddAsync(Imported(Now.AddMicroseconds(1)));
        var pastDeadlineNotYetArchived = await AddAsync(Imported(Now.AddMicroseconds(-1)));
        var archived = await AddAsync(Imported(null), ad => ad.Archive(_clock).IsSuccess.ShouldBeTrue());
        var erased = await AddAsync(Imported(null), ad => ad.Erase(_clock).IsSuccess.ShouldBeTrue());
        var neverImported = await AddAsync(NeverImported(null));
        var neverImportedErased = await AddAsync(NeverImported(null), ad => ad.Erase(_clock).IsSuccess.ShouldBeTrue());
        var sampler = Sampler();

        await sampler.SampleAsync(Ct);

        var snapshot = sampler.Current.ShouldNotBeNull();
        snapshot.AsOf.ShouldBe(Now);
        var listed = AllIds(snapshot);
        listed.ShouldBe([noDeadline, atTheInstant, afterTheInstant], ignoreOrder: true);
        listed.ShouldNotContain(archivedPastDeadline);
        listed.ShouldNotContain(pastDeadlineNotYetArchived);
        listed.ShouldNotContain(archived);
        listed.ShouldNotContain(erased);
        listed.ShouldNotContain(neverImported);
        listed.ShouldNotContain(neverImportedErased);
        listed.ShouldBe(await ClassifiedListedAsync(Now), ignoreOrder: true,
            "the SQL form and Classify must agree over the same rows");
        ShouldBeInTheirPartsInIdOrder(snapshot);
    }

    [Fact]
    public async Task Sample_OneMicrosecondPastTheDeadline_TheAdIsNoLongerListed()
    {
        var atTheInstant = await AddAsync(Imported(Now));
        var sampler = Sampler();
        _clock.UtcNow = Now.AddMicroseconds(1);

        await sampler.SampleAsync(Ct);

        AllIds(sampler.Current.ShouldNotBeNull()).ShouldNotContain(atTheInstant);
        (await ClassifiedListedAsync(Now.AddMicroseconds(1))).ShouldNotContain(atTheInstant);
    }

    [Fact]
    public async Task Sample_NothingListed_PublishesAMeasuredZero()
    {
        var sampler = Sampler();

        await sampler.SampleAsync(Ct);

        var snapshot = sampler.Current.ShouldNotBeNull("an empty read is a measurement, not an unknown");
        for (var part = 1; part <= ListedJobAdSnapshotLimits.PartCount; part++)
            snapshot.IdsIn(part).ShouldBeEmpty();
    }

    [Fact]
    public async Task Sample_PastTheCeiling_PublishesNothing_AndLogsTheCeilingAndNoId()
    {
        var logger = new RecordingLogger<ListedJobAdSnapshotSampler>();
        var sampler = Sampler(logger, ceiling: 2);
        await AddAsync(Imported(null));
        await AddAsync(Imported(null));
        await sampler.SampleAsync(Ct);
        var before = sampler.Current.ShouldNotBeNull();
        var third = await AddAsync(Imported(null));

        await sampler.SampleAsync(Ct);

        sampler.Current.ShouldBeSameAs(before, "a read past the ceiling replaces nothing");
        var line = logger.Records.ShouldHaveSingleItem();
        (line.Level, line.EventId.Id).ShouldBe((LogLevel.Error, 6234));
        line.Message.ShouldContain("2");
        line.Message.ShouldNotContain(third.ToString());
    }

    [Fact]
    public async Task Sample_AReadThatFails_LeavesTheSnapshotAndItsStampAsTheyWere()
    {
        await AddAsync(Imported(null));
        var sampler = Sampler();
        await sampler.SampleAsync(Ct);
        var before = sampler.Current.ShouldNotBeNull();
        await postgres.DropDatabaseAsync(_connectionString, Ct);
        _clock.UtcNow = Now.AddMinutes(5);

        await Should.ThrowAsync<Exception>(() => sampler.SampleAsync(Ct));

        sampler.Current.ShouldBeSameAs(before);
        sampler.Current!.AsOf.ShouldBe(Now, "a failed read must not re-stamp the old list as fresh");
    }

    [Fact]
    public async Task Sample_AnOlderReadNeverReplacesANewerOne()
    {
        await AddAsync(Imported(null));
        var sampler = Sampler();
        await sampler.SampleAsync(Ct);
        var newer = sampler.Current.ShouldNotBeNull();
        _clock.UtcNow = Now.AddMinutes(-5);

        await sampler.SampleAsync(Ct);

        sampler.Current.ShouldBeSameAs(newer);
    }

    private static List<Guid> AllIds(ListedJobAdSnapshot snapshot) =>
        Enumerable.Range(1, ListedJobAdSnapshotLimits.PartCount).SelectMany(snapshot.IdsIn).ToList();

    private static void ShouldBeInTheirPartsInIdOrder(ListedJobAdSnapshot snapshot)
    {
        for (var part = 1; part <= ListedJobAdSnapshotLimits.PartCount; part++)
        {
            var ids = snapshot.IdsIn(part);
            foreach (var id in ids)
                ListedJobAdPartition.PartOf(id, ListedJobAdSnapshotLimits.PartCount).ShouldBe(part);
            ids.Select(id => id.ToString("D")).ShouldBe(
                ids.Select(id => id.ToString("D")).Order(StringComparer.Ordinal), "the canonical, lower-case id order");
        }
    }

    private async Task<List<Guid>> ClassifiedListedAsync(DateTimeOffset now)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var facts = await db.JobAds.AsNoTracking()
            .Select(j => new { j.Id, j.Status, j.ExpiresAt, ImportedFrom = j.External != null ? j.External.Source : null })
            .ToListAsync(Ct);
        return facts
            .Where(f => JobAdPublication.Classify(f.Status, f.ExpiresAt, f.ImportedFrom, now) == JobAdPublicationState.Listed)
            .Select(f => f.Id.Value)
            .ToList();
    }

    private async Task<Guid> AddAsync(JobAd ad, Action<JobAd>? lifecycle = null)
    {
        lifecycle?.Invoke(ad);
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.JobAds.Add(ad);
        await db.SaveChangesAsync(Ct);
        return ad.Id.Value;
    }

    private async Task RunArchiverAsync(DateTimeOffset now)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await new ExpireJobAdsJob(
                db, new MutableFakeDateTimeProvider { UtcNow = now }, Substitute.For<ISystemEventAuditor>(),
                NullLogger<ExpireJobAdsJob>.Instance)
            .RunAsync(Ct);
    }

    private JobAd Imported(DateTimeOffset? expiresAt)
    {
        var externalId = TestIds.ExternalId();
        return JobAd.Import(
            title: "Backend-utvecklare",
            company: Company.Create("Exempelbolaget AB").Value,
            description: "Vi söker en backend-utvecklare.",
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: $"{{\"id\":\"{externalId}\"}}",
            facets: TestFacets.None,
            declaredContacts: [],
            publishedAt: Now.AddDays(-10),
            expiresAt: expiresAt,
            clock: _clock,
            extractTerms: TestKeywordExtraction.None).Value;
    }

    private JobAd NeverImported(DateTimeOffset? expiresAt) =>
        JobAd.Create(
            title: "Backend-utvecklare",
            company: Company.Create("Exempelbolaget AB").Value,
            description: "Vi söker en backend-utvecklare.",
            url: "https://example.com/annons",
            source: JobSource.Platsbanken,
            publishedAt: Now.AddDays(-10),
            expiresAt: expiresAt,
            clock: _clock).Value;
}
