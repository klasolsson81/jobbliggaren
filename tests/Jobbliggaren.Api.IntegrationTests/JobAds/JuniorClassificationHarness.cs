using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Admin.JuniorJobAds.Commands.DecideJuniorJobAd;
using Jobbliggaren.Application.Admin.JuniorJobAds.Queries.ListJuniorJobAds;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.ArchiveExternalJobAd;
using Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;
using Jobbliggaren.Application.JobAds.Jobs.ClassifyJuniorJobAds;
using Jobbliggaren.Application.JobAds.Jobs.ExpireJobAds;
using Jobbliggaren.Application.JobAds.Junior;
using Jobbliggaren.Application.JobAds.Queries.GetJuniorItJobAdCount;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJuniorItJobAds;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.JobAds.Junior;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Taxonomy;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #2264 (ADR 0187) — one database of its own on the shared Postgres, the real taxonomy snapshot behind the real
/// <see cref="JuniorFieldResolver"/>, a synthetic rule file read through the real loader, and every actor that writes
/// what the junior assessment reads, each the production one: <see cref="JobAd.Import"/> and
/// <see cref="JobAd.UpdateFromSource"/> for the ads, the real <see cref="ClassifyJuniorJobAdsJob"/> for the verdicts, the
/// real <see cref="DecideJuniorJobAdCommandHandler"/> for the decisions, <see cref="ArchiveExternalJobAdCommandHandler"/>,
/// <see cref="ExpireJobAdsJob"/>, <see cref="EraseRecruiterAdsCommandHandler"/> and the clock for an ad that stops being
/// listed, and the migration <c>AddJobAdJuniorAssessments</c> itself for an ad written before it. Each handler's save is
/// the one <c>UnitOfWorkBehavior</c> makes after it, whatever the result. The rule file mirrors
/// <c>JuniorItJobAdQueryReadTests</c>'s.
/// </summary>
internal sealed partial class JuniorClassificationHarness
{
    public static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    public const string DataItField = "apaJ_2ja_LuF";
    public const string DataIt = "DJh5_yyF_hEM";
    public const string OtherDataIt = "hmaC_cfi_UKg";
    public const string NotDataIt = "2kLc_pto_DpV";
    public const string Stockholm = "AvNB_uwa_6n6";
    public const string StockholmsLan = "CifL_Rzy_Mku";

    // Synthetic Platsbanken-shaped ids the synthetic reading names, outside the shipped reading's range.
    public const string CuratedInField = "90000001";
    public const string CuratedOutsideField = "90000002";

    /// <summary>A fictional recruiter named in an ad's text: the identifier the erasure command finds the ad by.</summary>
    public const string RecruiterName = "Magnus Fagerberg";

    /// <summary>No junior signal: the rules call it NotJunior under <c>no-signal</c>.</summary>
    public const string NeutralText = "Vi söker en utvecklare.";

    /// <summary>Under a junior title, the non-automatic <c>text-mentor</c> rule queues the ad as Unclear.</summary>
    public const string MentorText = "Du ska handleda nya kollegor.";

    public static string NamingTheRecruiter(string text) => $"{text} Kontakta {RecruiterName} för frågor.";

    public static string RuleFile(bool titleRuleIsAuto = true) => $$"""
        {
          "ruleSetVersion": "1",
          "occupationFieldConceptId": "{{DataItField}}",
          "enabled": true,
          "reading": { "ruleId": "reading-test", "junior": ["{{CuratedInField}}", "{{CuratedOutsideField}}"] },
          "patterns": {
            "candidate": "\\b(junior|trainee|praktikant)",
            "juniorTitle": "\\b(junior|trainee|praktikant)",
            "seniorTitle": "\\bsenior",
            "mentor": "handleda",
            "years": "\\b(?<from>\\d{1,2}) års erfarenhet"
          },
          "candidatePatterns": ["candidate"],
          "candidateWhenNoExperienceRequired": false,
          "years": { "patterns": ["years"], "numberWords": {}, "plausibleMin": 1, "plausibleMax": 15 },
          "rules": [
            { "id": "title-senior", "verdict": "NotJunior", "auto": true, "all": [ { "on": "title", "pattern": "seniorTitle" } ] },
            { "id": "text-mentor", "verdict": "NotJunior", "auto": false, "all": [ { "on": "text", "pattern": "mentor" } ] },
            { "id": "title-junior", "verdict": "Junior", "auto": {{(titleRuleIsAuto ? "true" : "false")}}, "all": [ { "on": "title", "pattern": "juniorTitle" } ] }
          ],
          "candidateFallbackRuleId": "candidate-only",
          "noSignalRuleId": "no-signal",
          "timeoutRuleId": "match-timeout"
        }
        """;

    public static JuniorRuleSet LoadRules(bool titleRuleIsAuto = true) =>
        JuniorRuleSetLoader.LoadFrom(new MemoryStream(Encoding.UTF8.GetBytes(RuleFile(titleRuleIsAuto))));

    /// <summary>The deployed rule file. Replacing it is a deploy of new rules.</summary>
    public JuniorRuleSet Rules { get; set; } = LoadRules();

    public MutableFakeDateTimeProvider Clock { get; } = new() { UtcNow = Now };

    public CommandCapture Commands { get; } = new();

    public SweepSeams Seams { get; } = new();

    private string _connectionString = string.Empty;
    private ServiceProvider _provider = default!;
    private TaxonomyReadModel _taxonomy = default!;
    private int _externalIds;

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
            .AddInterceptors(Commands, Seams)
            .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddRecruiterErasureMatchQuery();
        _provider = services.BuildServiceProvider();

        var scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Test");
        await new TaxonomySnapshotSeeder(scopeFactory, environment, NullLogger<TaxonomySnapshotSeeder>.Instance)
            .StartAsync(Ct);
        _taxonomy = new TaxonomyReadModel(scopeFactory);
    }

    public async ValueTask DisposeAsync(SharedPostgresFixture postgres)
    {
        await _provider.DisposeAsync();
        await postgres.DropDatabaseAsync(_connectionString);
    }

    public JuniorFieldResolver Catalog() => new([Rules], _taxonomy);

    /// <summary>
    /// One tick of the real sweep, as the Worker's two-minute schedule runs it.
    /// </summary>
    /// <param name="enabled">The kill-switch, <c>ClassifyJuniorJobAds:Enabled</c>.</param>
    /// <param name="beforeTheFirstWrite">Another process committing between the sweep's read of its candidates and its
    /// first write: it runs once, as the first candidate read's reader closes (<see cref="SweepSeams"/>). The Worker's
    /// sweep and the Api's commands share no transaction.</param>
    /// <param name="afterTheFirstWrite">Runs once, as the sweep's first per-ad scope ends, its save committed.</param>
    /// <param name="cancellationToken">The tick's token: Hangfire's, which its shutdown cancels.</param>
    public async Task<ClassifyJuniorJobAdsCounts> SweepAsync(
        int batchSize = 200,
        Func<Task>? beforeTheFirstWrite = null,
        Action? afterTheFirstWrite = null,
        bool enabled = true,
        ILogger<ClassifyJuniorJobAdsJob>? logger = null,
        CancellationToken? cancellationToken = null)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var scopes = _provider.GetRequiredService<IServiceScopeFactory>();
        Seams.BeforeTheFirstWrite(beforeTheFirstWrite);
        try
        {
            return await new ClassifyJuniorJobAdsJob(
                    afterTheFirstWrite is null ? scopes : new ScopesThatSignal(scopes, afterTheFirstWrite), db, Catalog(),
                    Clock,
                    Options.Create(new ClassifyJuniorJobAdsOptions { BatchSize = batchSize, Enabled = enabled }),
                    logger ?? NullLogger<ClassifyJuniorJobAdsJob>.Instance)
                .RunAsync(cancellationToken ?? Ct);
        }
        finally
        {
            Seams.BeforeTheFirstWrite(null);
        }
    }

    /// <summary>Hands out the inner factory's scopes and runs <paramref name="signal"/> once, as the first one ends.</summary>
    private sealed class ScopesThatSignal(IServiceScopeFactory inner, Action signal) : IServiceScopeFactory
    {
        private int _signalled;

        public IServiceScope CreateScope() => new Signalling(inner.CreateScope(), this);

        private void ScopeEnded()
        {
            if (Interlocked.Exchange(ref _signalled, 1) == 0)
                signal();
        }

        private sealed class Signalling(IServiceScope scope, ScopesThatSignal owner) : IServiceScope, IAsyncDisposable
        {
            public IServiceProvider ServiceProvider => scope.ServiceProvider;

            public void Dispose()
            {
                scope.Dispose();
                owner.ScopeEnded();
            }

            public async ValueTask DisposeAsync()
            {
                if (scope is IAsyncDisposable asyncScope)
                    await asyncScope.DisposeAsync();
                else
                    scope.Dispose();
                owner.ScopeEnded();
            }
        }
    }

    /// <summary><c>/junior</c>'s handler over the real port, first page, newest first.</summary>
    public async Task<PublicJuniorItJobAdsPage.Available> PublicListAsync()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var page = await new GetPublicJuniorItJobAdsQueryHandler(new JuniorItJobAdQuery(db, Catalog()), _taxonomy, Clock)
            .Handle(new GetPublicJuniorItJobAdsQuery(null, JobAdSortBy.PublishedAtDesc, 1), Ct);
        return page.ShouldBeOfType<PublicJuniorItJobAdsPage.Available>();
    }

    /// <summary>
    /// ADR 0188 (#2265) — the /jobb hero's count over the same real port, as <c>GET /api/v1/job-ads/junior-it-count</c>
    /// reads it: <see cref="GetJuniorItJobAdCountQueryHandler"/> at the harness's clock.
    /// </summary>
    public async Task<JuniorItJobAdCountDto> SignedInCountAsync()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await new GetJuniorItJobAdCountQueryHandler(new JuniorItJobAdQuery(db, Catalog()), Clock)
            .Handle(new GetJuniorItJobAdCountQuery(), Ct);
    }

    /// <summary>The admin command's handler, then <c>UnitOfWorkBehavior</c>'s save.</summary>
    public async Task<Result> DecideAsync(Guid id, JuniorDecision? decision)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var result = await new DecideJuniorJobAdCommandHandler(db, Catalog(), Clock)
            .Handle(new DecideJuniorJobAdCommand(id, decision), Ct);
        await db.SaveChangesAsync(Ct);
        return result;
    }

    /// <summary>The admin queue's handler.</summary>
    public async Task<JuniorJobAdQueueDto> ListAsync(JuniorQueueView view, int page = 1)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await new ListJuniorJobAdsQueryHandler(db, Catalog(), _taxonomy, Clock)
            .Handle(new ListJuniorJobAdsQuery(view, page), Ct);
    }

    /// <param name="municipality">The kommun the source carries; <c>null</c> is an ad without one
    /// (<c>PlatsbankenJobSourceFacetMappingTests.RefetchByExternalIdAsync_YieldsEmptyFacets_WhenTheSourceCarriesNone</c>).</param>
    /// <param name="region">The län the source carries, independent of the kommun (#839).</param>
    public JobAd Imported(
        string title,
        string? group = DataIt,
        string description = NeutralText,
        DateTimeOffset? publishedAt = null,
        DateTimeOffset? expiresAt = null,
        string? externalId = null,
        string? municipality = Stockholm,
        string? region = StockholmsLan)
    {
        externalId ??= $"8{Interlocked.Increment(ref _externalIds):D7}";
        return JobAd.Import(
            title: title,
            company: Company.Create("Exempelbolaget AB").Value,
            description: description,
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: $"{{\"id\":\"{externalId}\"}}",
            facets: TestFacets.From(occupationGroup: group, municipality: municipality, region: region),
            declaredContacts: [],
            publishedAt: publishedAt ?? Now.AddDays(-1),
            expiresAt: expiresAt,
            clock: Clock,
            applicationUrl: null, extractTerms: TestKeywordExtraction.None).Value;
    }

    /// <summary>The stream's import of a new ad: <see cref="JobAd.Import"/>, then the save.</summary>
    public async Task<Guid> ImportAsync(
        string title,
        string? group = DataIt,
        string description = NeutralText,
        DateTimeOffset? publishedAt = null,
        DateTimeOffset? expiresAt = null,
        string? externalId = null,
        string? municipality = Stockholm,
        string? region = StockholmsLan)
    {
        var ad = Imported(title, group, description, publishedAt, expiresAt, externalId, municipality, region);
        await AddAsync(ad);
        return ad.Id.Value;
    }

    public async Task AddAsync(JobAd ad)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.JobAds.Add(ad);
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>What <c>POST /api/v1/job-ads</c> stores: no import provenance and no facets.</summary>
    public async Task<Guid> CreateManuallyAsync(string title)
    {
        var ad = JobAd.Create(
            title, Company.Create("Exempelbolaget AB").Value, "Text som någon skrev själv.",
            "https://example.com/jobb/1", JobSource.Platsbanken, Now.AddHours(-1), null, Clock).Value;
        await AddAsync(ad);
        return ad.Id.Value;
    }

    /// <summary>
    /// The stream's re-import of a changed ad: <see cref="JobAd.UpdateFromSource"/> with the source's values, every one
    /// not named kept as the row holds it. <paramref name="experienceRequired"/> null is the source not speaking, which
    /// keeps the stored mark (PRESERVE, <c>JobAd.SetSourcePayload</c>).
    /// </summary>
    public async Task RewriteAsync(
        Guid id,
        string? title = null,
        string? description = null,
        string? group = null,
        DateTimeOffset? expiresAt = null,
        bool? experienceRequired = null)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ad = await db.JobAds.Where(j => j.Id == new JobAdId(id)).SingleAsync(Ct);
        var facets = new JobAdFacets(
            occupationNameConceptId: null,
            occupationGroupConceptId: group ?? ad.OccupationGroupConceptId,
            municipalityConceptId: ad.MunicipalityConceptId,
            regionConceptId: ad.RegionConceptId,
            employmentTypeConceptId: null,
            worktimeExtentConceptId: null,
            organizationNumber: null,
            remote: null,
            experienceRequired: experienceRequired);
        ad.UpdateFromSource(
                title ?? ad.Title, description ?? ad.Description, ad.Url, ad.RawPayload, facets, [],
                ad.PublishedAt, expiresAt ?? ad.ExpiresAt, TestKeywordExtraction.None, applicationUrl: null)
            .IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>The stream's removal path: the command handler, then the save the unit-of-work behaviour makes.</summary>
    public async Task ArchiveThroughTheStreamAsync(Guid id)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var externalId = await db.JobAds.AsNoTracking().Where(j => j.Id == new JobAdId(id))
            .Select(j => j.External!.ExternalId).SingleAsync(Ct);
        var result = await new ArchiveExternalJobAdCommandHandler(
                db, Clock, NullLogger<ArchiveExternalJobAdCommandHandler>.Instance)
            .Handle(new ArchiveExternalJobAdCommand(JobSource.Platsbanken, externalId), Ct);
        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(ArchiveOutcome.Archived);
        await db.SaveChangesAsync(Ct);
    }

    public async Task RunExpireJobAdsJobAsync()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await new ExpireJobAdsJob(db, Clock, Substitute.For<ISystemEventAuditor>(), NullLogger<ExpireJobAdsJob>.Instance)
            .RunAsync(Ct);
    }

    /// <summary>
    /// The Art. 17 erasure as an operator runs it: a dry run, the ads it returns confirmed by id, then the handler's
    /// save.
    /// </summary>
    public async Task<EraseRecruiterAdsResponse> EraseAsync(string identifier)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = new EraseRecruiterAdsCommandHandler(
            db, scope.ServiceProvider.GetRequiredService<IRecruiterErasureMatchQuery>(), Clock,
            NullLogger<EraseRecruiterAdsCommandHandler>.Instance);

        var probe = await handler.Handle(new EraseRecruiterAdsCommand(Guid.NewGuid(), identifier, DryRun: true, null), Ct);
        probe.IsSuccess.ShouldBeTrue();
        var result = await handler.Handle(
            new EraseRecruiterAdsCommand(
                Guid.NewGuid(), identifier, DryRun: false, [.. probe.Value.Matches.Select(m => m.JobAdId)]),
            Ct);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Code : string.Empty);
        await db.SaveChangesAsync(Ct);
        return result.Value;
    }

    /// <summary>
    /// The state the migration <c>AddJobAdJuniorAssessments</c> left every ad already stored when it was applied: the
    /// real migration is rolled back and applied again over the stored rows. Its <c>Up</c> adds both columns nullable
    /// with no data step, so a row that was there reads <c>NULL</c> in both until its next source write.
    /// </summary>
    public async Task ApplyTheJuniorMigrationOverTheStoredAdsAsync()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var migrations = db.Database.GetMigrations().ToList();
        var junior = migrations.FindIndex(m => m.EndsWith("_AddJobAdJuniorAssessments", StringComparison.Ordinal));
        junior.ShouldBeGreaterThan(0);

        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(migrations[junior - 1], Ct);
        (await db.Database.GetAppliedMigrationsAsync(Ct)).ShouldNotContain(migrations[junior]);
        await migrator.MigrateAsync(null, Ct);
        (await db.Database.GetPendingMigrationsAsync(Ct)).ShouldBeEmpty();
    }

    public async Task<JobAdJuniorAssessment?> AssessmentAsync(Guid id)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.JuniorAssessments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == new JobAdId(id), Ct);
    }

    public async Task<IReadOnlyList<JobAdJuniorAssessment>> AssessmentsAsync()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.JuniorAssessments.AsNoTracking().ToListAsync(Ct);
    }

    /// <summary>The two junior inputs <c>job_ads</c> carries for the ad: its fingerprint and its experience mark.</summary>
    public async Task<(string? Fingerprint, bool? ExperienceRequired, JobAdStatus Status)> JuniorInputsAsync(Guid id)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.JobAds.AsNoTracking().Where(j => j.Id == new JobAdId(id))
            .Select(j => new { j.JuniorInputFingerprint, j.ExperienceRequired, j.Status })
            .SingleAsync(Ct);
        return (row.JuniorInputFingerprint, row.ExperienceRequired, row.Status);
    }

    /// <summary>The text of every command the harness's contexts send, in order.</summary>
    internal sealed partial class CommandCapture : DbCommandInterceptor
    {
        private readonly List<string> _commands = [];

        public IReadOnlyList<string> Commands
        {
            get { lock (_commands) return [.. _commands]; }
        }

        /// <summary>Every table an INSERT, UPDATE or DELETE named, in the order first written.</summary>
        public IReadOnlyList<string> WrittenTables =>
            [.. Commands.SelectMany(c => Write().Matches(c).Select(m => m.Groups["table"].Value)).Distinct(StringComparer.Ordinal)];

        public void Clear()
        {
            lock (_commands) _commands.Clear();
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        private void Record(DbCommand command)
        {
            lock (_commands) _commands.Add(command.CommandText);
        }

        [GeneratedRegex("""(?:INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+"?(?<table>[a-z_]+)"?""", RegexOptions.CultureInvariant)]
        private static partial Regex Write();
    }

    /// <summary>
    /// Where the sweep meets another actor, set per tick. Every hook runs on the sweep's own commands and is awaited
    /// there, so nothing blocks on a task.
    /// </summary>
    internal sealed class SweepSeams : DbCommandInterceptor
    {
        private Func<Task>? _beforeTheFirstWrite;
        private Guid? _sessionEndedFor;
        private readonly List<string> _failureMessages = [];
        private int _endedSessions;

        /// <summary>How many sessions the server ended under an INSERT of the targeted ad's assessment.</summary>
        public int EndedSessions => Volatile.Read(ref _endedSessions);

        /// <summary>The message of every exception the ended sessions raised, for the log's hygiene assertions.</summary>
        public IReadOnlyList<string> FailureMessages
        {
            get { lock (_failureMessages) return [.. _failureMessages]; }
        }

        public void BeforeTheFirstWrite(Func<Task>? hook) => Volatile.Write(ref _beforeTheFirstWrite, hook);

        /// <summary>
        /// Every INSERT of <paramref name="jobAdId"/>'s assessment meets the database server ending its session first
        /// (<c>pg_terminate_backend</c> on its own backend: SQLSTATE 57P01 admin_shutdown, what a Postgres restart or
        /// failover does to the sessions it holds). The server produces the failure; the seam picks the moment.
        /// </summary>
        public void EndTheSessionOfEveryInsertFor(Guid jobAdId) => _sessionEndedFor = jobAdId;

        public void LiftTheFailure() => _sessionEndedFor = null;

        public override async ValueTask<InterceptionResult> DataReaderClosingAsync(
            DbCommand command, DataReaderClosingEventData eventData, InterceptionResult result)
        {
            if (IsTheCandidateRead(command) && Interlocked.Exchange(ref _beforeTheFirstWrite, null) is { } hook)
                await hook();
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            await EndTheSessionIfTargetedAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await EndTheSessionIfTargetedAsync(command, cancellationToken);
            return result;
        }

        public override Task CommandFailedAsync(
            DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            if (IsAssessmentInsertOf(command, _sessionEndedFor))
                RecordFailure(eventData.Exception);
            return Task.CompletedTask;
        }

        // The sweep's selection: the only read that orders job_ads by an anti-join on the assessments' fingerprints.
        private static bool IsTheCandidateRead(DbCommand command) =>
            command.CommandText.Contains("FROM job_ads", StringComparison.Ordinal)
            && command.CommandText.Contains("job_ad_junior_assessments", StringComparison.Ordinal)
            && command.CommandText.Contains("junior_input_fingerprint", StringComparison.Ordinal)
            && command.CommandText.Contains("ORDER BY", StringComparison.Ordinal);

        private static bool IsAssessmentInsertOf(DbCommand command, Guid? jobAdId) =>
            jobAdId is { } id
            && command.CommandText.Contains("INSERT INTO job_ad_junior_assessments", StringComparison.Ordinal)
            && command.Parameters.Cast<DbParameter>().Any(p => p.Value is Guid value && value == id);

        private async Task EndTheSessionIfTargetedAsync(DbCommand command, CancellationToken cancellationToken)
        {
            if (!IsAssessmentInsertOf(command, _sessionEndedFor))
                return;

            Interlocked.Increment(ref _endedSessions);
            try
            {
                await using var end = command.Connection!.CreateCommand();
                end.Transaction = command.Transaction;
                end.CommandText = "SELECT pg_terminate_backend(pg_backend_pid())";
                await end.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (DbException ex)
            {
                RecordFailure(ex);
                throw;
            }
        }

        private void RecordFailure(Exception exception)
        {
            lock (_failureMessages) _failureMessages.Add(exception.Message);
        }
    }
}
