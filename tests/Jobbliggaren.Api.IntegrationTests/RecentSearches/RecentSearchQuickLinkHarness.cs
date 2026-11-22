using System.Data.Common;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobSeekers.Commands.SetJuniorQuickLink;
using Jobbliggaren.Application.RecentJobSearches.Commands.MarkRecentSearchQuickLink;
using Jobbliggaren.Application.RecentJobSearches.Commands.UnmarkRecentSearchQuickLink;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.RecentJobSearches;
using Jobbliggaren.Domain.SavedSearches;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.RecentJobSearches;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.RecentSearches;

/// <summary>
/// ADR 0188 (#2265) — one database of its own on the shared Postgres and every actor that writes what a quick link
/// rests on, each the production one: <see cref="JobSeeker.Register"/> for the seeker, the real
/// <see cref="RecentJobSearchCapturer"/> for the searches (what <c>CaptureRecentSearchCommandHandler</c> calls for a
/// search the user stores), the real <see cref="MarkRecentSearchQuickLinkCommandHandler"/> for a mark and the real
/// <see cref="SetJuniorQuickLinkCommandHandler"/> for the junior link, each handler followed by the save
/// <c>UnitOfWorkBehavior</c> makes after it. <see cref="Seams"/> picks the moment another request commits.
/// </summary>
internal sealed class RecentSearchQuickLinkHarness
{
    public static readonly DateTimeOffset Start = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    public MutableFakeDateTimeProvider Clock { get; } = new() { UtcNow = Start };

    public RecentSearchSeams Seams { get; } = new();

    public IFailedAccessLogger FailedAccess { get; } = Substitute.For<IFailedAccessLogger>();

    /// <summary>The signed-in user every action runs as unless one is named.</summary>
    public Guid UserId { get; } = Guid.NewGuid();

    private string _connectionString = string.Empty;
    private ServiceProvider _provider = default!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The fixture is handed in, never kept: the test class receives it from its collection.</summary>
    public async ValueTask InitializeAsync(SharedPostgresFixture postgres)
    {
        _connectionString = await postgres.CreateDatabaseAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(_connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(Seams)
            .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        _provider = services.BuildServiceProvider();

        await RegisterAsync(UserId);
    }

    public async ValueTask DisposeAsync(SharedPostgresFixture postgres)
    {
        await _provider.DisposeAsync();
        await postgres.DropDatabaseAsync(_connectionString);
    }

    /// <summary>Registration's write of the job seeker: <see cref="JobSeeker.Register"/>, then the save.</summary>
    public async Task RegisterAsync(Guid userId)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.JobSeekers.Add(JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(Clock), Clock).Value);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>A committed /jobb search for <paramref name="q"/>, at the clock's now, through the real capturer.</summary>
    public async Task<bool> CaptureAsync(string q, int currentCount = 0)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var criteria = SearchCriteria.Create(
            occupationGroup: null, occupationName: null, municipality: null, region: null, employmentType: null,
            worktimeExtent: null, employer: null, remote: false, q: q, sortBy: JobAdSortBy.PublishedAtDesc).Value;
        return await new RecentJobSearchCapturer(
                db, Clock, new DbExceptionInspector(), NullLogger<RecentJobSearchCapturer>.Instance)
            .CaptureAsync(UserId, criteria, currentCount, Ct);
    }

    /// <summary>
    /// <paramref name="count"/> committed searches, a minute apart; returns their ids oldest first. The clock ends a
    /// minute past the last one.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> CaptureSearchesAsync(int count, string prefix)
    {
        for (var i = 0; i < count; i++)
        {
            await CaptureAsync($"{prefix}{i:D2}");
            Clock.UtcNow = Clock.UtcNow.AddMinutes(1);
        }

        var rows = await RowsAsync();
        rows.Count.ShouldBe(count, "the premise: every search was stored");
        return [.. rows.Select(r => r.Id)];
    }

    /// <summary><c>PUT /api/v1/me/recent-searches/{id}/quick-link</c>: the handler, then the unit of work's save.</summary>
    public async Task<Result> MarkAsync(Guid recentSearchId, Guid? asUser = null)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var result = await new MarkRecentSearchQuickLinkCommandHandler(
                db, SignedIn(asUser ?? UserId), FailedAccess, Clock, new DbExceptionInspector())
            .Handle(new MarkRecentSearchQuickLinkCommand(recentSearchId), Ct);
        await db.SaveChangesAsync(Ct);
        return result;
    }

    /// <summary><c>DELETE /api/v1/me/recent-searches/{id}/quick-link</c>: the handler, then the unit of work's save.</summary>
    public async Task<Result> UnmarkAsync(Guid recentSearchId, Guid? asUser = null)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var result = await new UnmarkRecentSearchQuickLinkCommandHandler(
                db, SignedIn(asUser ?? UserId), FailedAccess, Clock)
            .Handle(new UnmarkRecentSearchQuickLinkCommand(recentSearchId), Ct);
        await db.SaveChangesAsync(Ct);
        return result;
    }

    /// <summary><c>PUT /api/v1/me/junior-quick-link</c>: the handler, then the unit of work's save.</summary>
    public async Task<Result<Guid>> SetJuniorLinkAsync(bool enabled)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var result = await new SetJuniorQuickLinkCommandHandler(db, SignedIn(UserId), Clock)
            .Handle(new SetJuniorQuickLinkCommand(enabled), Ct);
        await db.SaveChangesAsync(Ct);
        return result;
    }

    /// <summary>The user's stored searches, oldest first.</summary>
    public async Task<IReadOnlyList<StoredSearch>> RowsAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seekerId = await db.JobSeekers.AsNoTracking().Where(js => js.UserId == UserId).Select(js => js.Id)
            .SingleAsync(Ct);
        var rows = await db.RecentJobSearches.AsNoTracking().Where(r => r.JobSeekerId == seekerId).ToListAsync(Ct);
        return [.. rows
            .OrderBy(r => r.LastViewedAt)
            .Select(r => new StoredSearch(r.Id.Value, r.Q, r.LastViewedAt, r.LastSeenCount, r.QuickLinkSlot))];
    }

    /// <summary>One value from the database's own catalog.</summary>
    public async Task<string?> CatalogValueAsync(string sql, string name)
    {
        await using var scope = _provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.Database.SqlQueryRaw<string>(sql, name).ToListAsync(Ct);
        return rows.SingleOrDefault();
    }

    private static ICurrentUser SignedIn(Guid userId)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        user.IsAuthenticated.Returns(true);
        return user;
    }

    internal sealed record StoredSearch(
        Guid Id, string? Q, DateTimeOffset LastViewedAt, int LastSeenCount, int? QuickLinkSlot);
}

/// <summary>
/// Where another request meets the one under test, set per test. Each hook runs once, on the command that names its
/// moment, and is awaited there, so nothing blocks on a task. The other request runs on a connection of its own and
/// commits before the command under test goes on, which is what a second Api request does; the two share no
/// transaction.
/// </summary>
internal sealed class RecentSearchSeams : DbCommandInterceptor
{
    private Func<Task>? _afterTheCapTally;
    private Func<Task>? _beforeTheEvictionDelete;
    private Func<Task>? _afterTheHeldSlotsRead;
    private int _evictionDeletes;
    private readonly List<string> _uniqueViolations = [];

    /// <summary>How many eviction deletes the capturer sent.</summary>
    public int EvictionDeletes => Volatile.Read(ref _evictionDeletes);

    /// <summary>The constraint every unique violation the server raised named, in order.</summary>
    public IReadOnlyList<string> UniqueViolations
    {
        get { lock (_uniqueViolations) return [.. _uniqueViolations]; }
    }

    /// <summary>Runs once, as the capturer's count of the seeker's rows has been read.</summary>
    public void AfterTheCapTally(Func<Task> hook) => Volatile.Write(ref _afterTheCapTally, hook);

    /// <summary>Runs once, after the capturer picked the rows to evict and before its delete reaches the server.</summary>
    public void BeforeTheEvictionDelete(Func<Task> hook) => Volatile.Write(ref _beforeTheEvictionDelete, hook);

    /// <summary>Runs once, as the mark handler has read the slots the seeker's marked searches hold.</summary>
    public void AfterTheHeldSlotsRead(Func<Task> hook) => Volatile.Write(ref _afterTheHeldSlotsRead, hook);

    public override async ValueTask<InterceptionResult> DataReaderClosingAsync(
        DbCommand command, DataReaderClosingEventData eventData, InterceptionResult result)
    {
        if (IsTheCapTally(command) && Interlocked.Exchange(ref _afterTheCapTally, null) is { } tallyHook)
            await tallyHook();
        else if (IsTheHeldSlotsRead(command) && Interlocked.Exchange(ref _afterTheHeldSlotsRead, null) is { } slotsHook)
            await slotsHook();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (IsTheEvictionDelete(command))
        {
            Interlocked.Increment(ref _evictionDeletes);
            if (Interlocked.Exchange(ref _beforeTheEvictionDelete, null) is { } hook)
                await hook();
        }

        return result;
    }

    public override Task CommandFailedAsync(
        DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Exception is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation)
        {
            lock (_uniqueViolations) _uniqueViolations.Add(violation.ConstraintName ?? string.Empty);
        }

        return Task.CompletedTask;
    }

    // RecentJobSearchCapturer.EnforceCapAsync's count: every row of the seeker, no slot in the predicate.
    private static bool IsTheCapTally(DbCommand command) =>
        command.CommandText.Contains("count(*)", StringComparison.OrdinalIgnoreCase)
        && command.CommandText.Contains("FROM recent_job_searches", StringComparison.Ordinal)
        && !command.CommandText.Contains("quick_link_slot", StringComparison.Ordinal);

    // MarkRecentSearchQuickLinkCommandHandler's read of the held slots: the slots themselves, never a count of them.
    private static bool IsTheHeldSlotsRead(DbCommand command) =>
        command.CommandText.Contains("FROM recent_job_searches", StringComparison.Ordinal)
        && command.CommandText.Contains("quick_link_slot IS NOT NULL", StringComparison.Ordinal)
        && !command.CommandText.Contains("count(", StringComparison.OrdinalIgnoreCase);

    // The capturer's eviction, the only delete of a search the harness's actors send. Matched on the table alone, so the
    // moment holds whatever predicate the delete carries.
    private static bool IsTheEvictionDelete(DbCommand command) =>
        command.CommandText.Contains("DELETE FROM recent_job_searches", StringComparison.Ordinal);
}
