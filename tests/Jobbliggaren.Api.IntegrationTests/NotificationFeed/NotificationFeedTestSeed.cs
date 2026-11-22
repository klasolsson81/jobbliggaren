using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.NotificationFeed;

/// <summary>
/// What the NotificationFeed integration classes share: the feed's wire shapes, the seeds (each through an aggregate,
/// in a scope of its own), the read-backs and the statement probes. The Branschbevakning seeds are also
/// <c>CriterionNewAdsEndpointTests</c>' own: one copy for both. The background-match consent is not here: each class
/// sets it on its own path, and <c>NotificationFeedOneNotificationPerAdTests</c> only through its endpoint.
/// </summary>
internal static class NotificationFeedTestSeed
{
    internal const string FeedPath = "/api/v1/me/notification-feed";

    public sealed record FeedItem(
        string Kind, Guid JobAdId, string Title, string CompanyName, DateTimeOffset OccurredAt, bool IsUnread, string? Grade);

    public sealed record FeedSource(string Kind, int? UnreadCount, DateTimeOffset? AcknowledgedThrough);

    public sealed record FeedBody(IReadOnlyList<FeedItem> Items, IReadOnlyList<FeedSource> Sources);

    public sealed record CountResponse(int Count);

    // Whole seconds in the past: the /seen setters clamp to now, and a seeded instant must survive timestamptz unchanged.
    public static readonly DateTimeOffset Base =
        DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeSeconds());

    // ── seeding ──────────────────────────────────────────────────────────────────────────────

    public static async Task<(HttpClient Client, Guid UserId)> RegisterUserAsync(
        WebApplicationFactory<Program> factory, string prefix, CancellationToken ct)
    {
        var client = factory.CreateClient();
        var email = $"{prefix}-{Guid.NewGuid()}@example.com";
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(factory, email, ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        user.ShouldNotBeNull();
        return (client, user.Id);
    }

    public static Task<JobAdId> SeedAdAsync(WebApplicationFactory<Program> factory, string title, CancellationToken ct) =>
        SeedAdAsync(factory, title, "Bolag AB", ct);

    public static Task<JobAdId> SeedAdAsync(
        WebApplicationFactory<Program> factory, string title, string company, CancellationToken ct) =>
        SeedAdAsync(factory, title, company, Base.AddHours(-1), ct);

    /// <summary>An Active ad ingested at <paramref name="ingestedAt"/>, before any hit or match row a scan stamps for it.</summary>
    public static async Task<JobAdId> SeedAdAsync(
        WebApplicationFactory<Program> factory, string title, string company, DateTimeOffset ingestedAt, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FakeDateTimeProvider(ingestedAt);
        var externalId = $"ext-{Guid.NewGuid():N}";
        var payload = $"{{\"id\":\"{externalId}\"}}";
        var jobAd = JobAd.Import(
            title: title,
            company: Company.Create(company).Value,
            description: "beskrivning",
            url: $"https://example.com/jobs/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload,
            facets: TestFacets.FromPayload(payload),
            publishedAt: clock.UtcNow,
            expiresAt: clock.UtcNow.AddDays(60),
            clock: clock, declaredContacts: [], applicationUrl: null, extractTerms: TestKeywordExtraction.None).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        return jobAd.Id;
    }

    public static async Task ArchiveAsync(WebApplicationFactory<Program> factory, JobAdId adId, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ad = await db.JobAds.SingleAsync(j => j.Id == adId, ct);
        ad.Archive(scope.ServiceProvider.GetRequiredService<IDateTimeProvider>()).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);
    }

    public static Task SeedMatchAsync(
        WebApplicationFactory<Program> factory, Guid userId, JobAdId adId, DateTimeOffset at, CancellationToken ct) =>
        SeedMatchAsync(factory, userId, adId, NotifiableMatchGrade.Strong, at, ct);

    public static async Task SeedMatchAsync(
        WebApplicationFactory<Program> factory, Guid userId, JobAdId adId, NotifiableMatchGrade grade, DateTimeOffset at,
        CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserJobAdMatches.Add(UserJobAdMatch.Create(userId, adId, grade, ["csharp"], new FakeDateTimeProvider(at)).Value);
        await db.SaveChangesAsync(ct);
    }

    public static Task<CompanyWatchId> SeedWatchAsync(
        WebApplicationFactory<Program> factory, Guid userId, CancellationToken ct) =>
        SeedWatchAsync(factory, userId, onlyMatched: false, ct);

    public static async Task<CompanyWatchId> SeedWatchAsync(
        WebApplicationFactory<Program> factory, Guid userId, bool onlyMatched, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var watch = CompanyWatch.Follow(userId, OrganizationNumber.Create(NewOrgNr()).Value, clock).Value;
        if (onlyMatched)
            watch.SetFilter(WatchFilterSpec.Create([], [], onlyMatched: true).Value).IsSuccess.ShouldBeTrue();
        db.CompanyWatches.Add(watch);
        await db.SaveChangesAsync(ct);
        return watch.Id;
    }

    public static async Task SeedHitAsync(
        WebApplicationFactory<Program> factory, Guid userId, JobAdId adId, CompanyWatchId watchId, DateTimeOffset at,
        CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.FollowedCompanyAdHits.Add(FollowedCompanyAdHit.Create(userId, adId, watchId, new FakeDateTimeProvider(at)).Value);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A click's effect on a followed ad's hits, through the aggregate, as the follow source writes it.</summary>
    public static async Task ReadHitsInBellAsync(
        WebApplicationFactory<Program> factory, Guid userId, JobAdId adId, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var hit in await db.FollowedCompanyAdHits.Where(h => h.UserId == userId && h.JobAdId == adId).ToListAsync(ct))
            hit.MarkReadInNotificationFeed();
        await db.SaveChangesAsync(ct);
    }

    private static string NewOrgNr() =>
        "55" + (Math.Abs(Guid.NewGuid().GetHashCode()) % 100000000)
            .ToString("D8", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>When every seeded Branschbevakning was created: a day before <see cref="Base"/>, so a hit stamped at
    /// <see cref="Base"/> or later is newer than the criterion's last change.</summary>
    public static readonly DateTimeOffset CriterionCreatedAt = Base.AddDays(-1);

    /// <summary>A Branschbevakning whose nightly scan has answered Scanned.</summary>
    public static Task<CompanyWatchCriterionId> SeedCriterionAsync(
        WebApplicationFactory<Program> factory, Guid userId, CancellationToken ct) =>
        SeedCriterionAsync(factory, userId, CompanyWatchCriterionScanOutcome.Scanned, ct);

    /// <summary>A Branschbevakning; <paramref name="outcome"/> <c>null</c> is one the scan has never reached.</summary>
    public static Task<CompanyWatchCriterionId> SeedCriterionAsync(
        WebApplicationFactory<Program> factory, Guid userId, CompanyWatchCriterionScanOutcome? outcome, CancellationToken ct) =>
        SeedCriterionAsync(factory, userId, outcome, ["63910"], ct);

    /// <summary>
    /// A Branschbevakning as <see cref="CompanyWatchCriterion.Create"/> makes it, created at
    /// <see cref="CriterionCreatedAt"/>; with an outcome, the nightly scan has recorded it half an hour later through
    /// <see cref="CompanyWatchCriterion.RecordScan"/>.
    /// </summary>
    public static async Task<CompanyWatchCriterionId> SeedCriterionAsync(
        WebApplicationFactory<Program> factory,
        Guid userId,
        CompanyWatchCriterionScanOutcome? outcome,
        IReadOnlyList<string> sniCodes,
        CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var criterion = CompanyWatchCriterion.Create(
            userId, CompanyWatchCriteriaSpec.Create(sniCodes, ["0180"]).Value, null,
            new FakeDateTimeProvider(CriterionCreatedAt)).Value;
        var scanned = CriterionCreatedAt.AddMinutes(30);
        if (outcome == CompanyWatchCriterionScanOutcome.NotMaterialised)
        {
            // The scan's read-age guard answers NotMaterialised for a criterion it has scanned before, once its
            // materialisation is stale (CompanyWatchBrowseQuery); alone it would leave ScannedThrough null.
            criterion.RecordScan(CompanyWatchCriterionScanOutcome.Scanned, scanned, new FakeDateTimeProvider(scanned));
            criterion.RecordScan(CompanyWatchCriterionScanOutcome.NotMaterialised, scanned.AddMinutes(30),
                new FakeDateTimeProvider(scanned.AddMinutes(30)));
        }
        else if (outcome is { } answered)
        {
            criterion.RecordScan(answered, scanned, new FakeDateTimeProvider(scanned));
        }

        db.CompanyWatchCriteria.Add(criterion);
        await db.SaveChangesAsync(ct);
        return criterion.Id;
    }

    /// <summary>
    /// A hit as the criterion scan writes it (<see cref="CompanyWatchCriterionAdHit.Create"/>, owner and criterion taken
    /// from the criterion itself), stamped at <paramref name="at"/>.
    /// </summary>
    public static async Task SeedCriterionHitAsync(
        WebApplicationFactory<Program> factory, CompanyWatchCriterionId criterionId, JobAdId adId, DateTimeOffset at,
        CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var criterion = await db.CompanyWatchCriteria.AsNoTracking().SingleAsync(c => c.Id == criterionId, ct);
        db.CompanyWatchCriterionAdHits.Add(
            CompanyWatchCriterionAdHit.Create(criterion, adId, new FakeDateTimeProvider(at)).Value);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>What the nightly scan records for a criterion it reached at <paramref name="at"/>.</summary>
    public static async Task RecordScanAsync(
        WebApplicationFactory<Program> factory, CompanyWatchCriterionId criterionId, CompanyWatchCriterionScanOutcome outcome,
        DateTimeOffset at, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var criterion = await db.CompanyWatchCriteria.SingleAsync(c => c.Id == criterionId, ct);
        criterion.RecordScan(outcome, at, new FakeDateTimeProvider(at));
        await db.SaveChangesAsync(ct);
    }

    // ── reading back ─────────────────────────────────────────────────────────────────────────

    public static async Task<UserJobAdMatch> MatchRowAsync(
        WebApplicationFactory<Program> factory, Guid userId, JobAdId adId, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.UserJobAdMatches.AsNoTracking().SingleAsync(m => m.UserId == userId && m.JobAdId == adId, ct);
    }

    public static async Task<List<FollowedCompanyAdHit>> HitRowsAsync(
        WebApplicationFactory<Program> factory, Guid userId, JobAdId adId, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.FollowedCompanyAdHits.AsNoTracking().Where(h => h.UserId == userId && h.JobAdId == adId).ToListAsync(ct);
    }

    public static async Task<List<CompanyWatchCriterionAdHit>> CriterionHitRowsAsync(
        WebApplicationFactory<Program> factory, Guid userId, JobAdId adId, CancellationToken ct)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.CompanyWatchCriterionAdHits.AsNoTracking()
            .Where(h => h.UserId == userId && h.JobAdId == adId)
            .ToListAsync(ct);
    }

    public static async Task<FeedBody> GetFeedAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.GetAsync(FeedPath, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var feed = await response.Content.ReadFromJsonAsync<FeedBody>(ct);
        feed.ShouldNotBeNull();
        return feed;
    }

    public static FeedSource Source(FeedBody feed, string kind) => feed.Sources.Single(s => s.Kind == kind);

    public static async Task<int> CountAsync(HttpClient client, string path, CancellationToken ct)
    {
        var response = await client.GetAsync(path, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<CountResponse>(ct))!.Count;
    }

    // ── the statement probes ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// A fresh <see cref="AppDbContext"/> on the fixture's database with only the given interceptors — the
    /// <c>FilterToMatchingQueryPlanTests</c> idiom. None of the tables read here carries a DEK-encrypted column, so the
    /// field-encryption interceptor pair is not needed.
    /// </summary>
    public static AppDbContext NewContext(IServiceScope scope, params IInterceptor[] interceptors)
    {
        var connectionString = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString();
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning));
        if (interceptors.Length > 0)
            builder = builder.AddInterceptors(interceptors);
        return new AppDbContext(builder.Options);
    }

    /// <summary>
    /// Runs <paramref name="action"/> right after the first <c>MAX</c> statement returns: a row committed through another
    /// context, or a change through the API.
    /// </summary>
    public sealed class AfterFirstMaxInterceptor(Func<Task> action) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired && command.CommandText.Contains("max(", StringComparison.OrdinalIgnoreCase))
            {
                Fired = true;
                await action();
            }

            return result;
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> right after the context's <paramref name="statement"/>-th statement returns (1-based),
    /// for a source whose token is not a <c>MAX</c>: the Branschbevakning source reads its newest hit in order.
    /// </summary>
    public sealed class AfterStatementInterceptor(int statement, Func<Task> action) : DbCommandInterceptor
    {
        private int _returned;

        public bool Fired { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired && ++_returned == statement)
            {
                Fired = true;
                await action();
            }

            return result;
        }
    }

    /// <summary>Counts the statements a context sends. EF runs every query here through a reader.</summary>
    public sealed class StatementCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>Keeps the text of every statement a context sends.</summary>
    public sealed class StatementText : DbCommandInterceptor
    {
        public List<string> Texts { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Texts.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    /// <summary>One statement a context sent without a reader: its text, and a copy of its parameters to replay it.</summary>
    public sealed record WriteStatement(string Text, IReadOnlyList<NpgsqlParameter> Parameters);

    /// <summary>
    /// Keeps every statement a context sends without a reader, which is how EF runs <c>ExecuteUpdate</c> and
    /// <c>ExecuteDelete</c>. The statements still run.
    /// </summary>
    public sealed class WriteStatementCapture : DbCommandInterceptor
    {
        public List<WriteStatement> Statements { get; } = [];

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Statements.Add(new WriteStatement(
                command.CommandText, [.. command.Parameters.OfType<NpgsqlParameter>().Select(p => p.Clone())]));
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
