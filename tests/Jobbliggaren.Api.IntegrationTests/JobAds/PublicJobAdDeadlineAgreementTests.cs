using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Jobs.ExpireJobAds;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAd;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// The public read and the nightly archiver agree on the deadline instant (ADR 0160). The public
/// predicate is evaluated at read time and <c>ExpireJobAdsJob</c> archives in bulk; they share no
/// code, so this pins that they draw the line at the same place, on real Postgres. An own database
/// per test: the job archives every expired Active row it can see.
/// </summary>
/// <remarks>
/// Instants are whole seconds and the step is one microsecond, the resolution of <c>timestamptz</c>:
/// a sub-microsecond step would be rounded away by the query parameter, so the job and the
/// in-memory predicate would be comparing different instants.
/// </remarks>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class PublicJobAdDeadlineAgreementTests(SharedPostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Deadline = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

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

    private async Task<Guid> ImportWithDeadlineAsync()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new MutableFakeDateTimeProvider { UtcNow = Deadline.AddDays(-10) };
        var externalId = TestIds.ExternalId();
        var payload = $"{{\"id\":\"{externalId}\"}}";

        var jobAd = JobAd.Import(
            title: "Backend-utvecklare",
            company: Company.Create("Exempelbolaget AB").Value,
            description: "Vi söker en backend-utvecklare.",
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload,
            facets: TestFacets.None,
            declaredContacts: [],
            publishedAt: clock.UtcNow,
            expiresAt: Deadline,
            clock: clock,
            extractTerms: TestKeywordExtraction.None).Value;

        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(Ct);
        return jobAd.Id.Value;
    }

    private async Task<bool> PublicReadSucceedsAsync(Guid id, DateTimeOffset now)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var result = await new GetPublicJobAdQueryHandler(db, Substitute.For<ITaxonomyReadModel>(), new MutableFakeDateTimeProvider { UtcNow = now })
            .Handle(new GetPublicJobAdQuery(id), Ct);
        if (result.IsFailure)
            result.Error.ShouldBe(JobAdErrors.Gone);
        return result.IsSuccess;
    }

    private async Task<JobAdStatus> RunArchiverAsync(Guid id, DateTimeOffset now)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await new ExpireJobAdsJob(
                db, new MutableFakeDateTimeProvider { UtcNow = now }, Substitute.For<ISystemEventAuditor>(),
                NullLogger<ExpireJobAdsJob>.Instance)
            .RunAsync(Ct);
        return await db.JobAds.AsNoTracking().Where(j => j.Id == new JobAdId(id)).Select(j => j.Status).SingleAsync(Ct);
    }

    [Fact]
    public async Task At_the_deadline_instant_the_ad_is_public_and_the_archiver_leaves_it_Active()
    {
        var id = await ImportWithDeadlineAsync();

        (await PublicReadSucceedsAsync(id, Deadline)).ShouldBeTrue();
        (await RunArchiverAsync(id, Deadline)).ShouldBe(JobAdStatus.Active);
    }

    [Fact]
    public async Task One_microsecond_after_the_deadline_the_ad_is_gone_and_the_archiver_archives_it()
    {
        var id = await ImportWithDeadlineAsync();
        var justAfter = Deadline.AddMicroseconds(1);

        (await PublicReadSucceedsAsync(id, justAfter)).ShouldBeFalse();
        (await RunArchiverAsync(id, justAfter)).ShouldBe(JobAdStatus.Archived);
    }
}
