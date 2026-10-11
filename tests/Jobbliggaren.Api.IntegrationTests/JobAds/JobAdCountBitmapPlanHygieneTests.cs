using System.Data.Common;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// #744 — the bitmap-plan count hygiene (<c>SET LOCAL enable_seqscan = off</c> inside a transaction, TD-94 /
/// ADR 0062) must TRACK the free-text q-predicate: applied when q is present (the sole TOAST-detoast source
/// over the wide STORED search_vector), skipped when it is not. Before #744 it ran unconditionally on the
/// shared port (2-3 needless roundtrips per no-q browse) and was MISSING on the per-user graded/status count
/// when q WAS present (re-exposing the seqscan #821/TD-94 fixed).
/// <para>
/// The instrument is a <see cref="RecordingCommandInterceptor"/> over a real Testcontainers connection: it
/// records each command's text AND its <see cref="DbTransaction"/>, so the counterfactual (no-q emits the
/// COUNT with ZERO preceding SET LOCAL and NO ambient transaction; q emits exactly one SET LOCAL sharing the
/// COUNT's transaction) is proven structurally — absence proves the gate only alongside its positive twin,
/// and the GUC is a no-op unless it and the COUNT ride the same pinned connection. That the emitted q-search
/// SHAPE is actually served by the GIN bitmap under the GUC is the separate concern of
/// <see cref="JobAdPlannerUsabilityOracleTests"/>; this file guards WHEN the hygiene wraps the count.
/// </para>
/// </summary>
[Collection("Api")]
public sealed class JobAdCountBitmapPlanHygieneTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private const string SeqScanGuc = "enable_seqscan";
    private const string NestLoopGuc = "enable_nestloop";

    private static readonly ResolvedSearchText NoText = new(null, [], TaxonomyLabelMatchDto.Empty);

    private static readonly ResolvedSearchText PositiveText =
        new(new TextTerm("lärare", "%lärare%", [], TextTermRole.Positive), [], TaxonomyLabelMatchDto.Empty);

    private static readonly ResolvedSearchText ExcludingText =
        new(null, [new TextTerm("deltid", "%deltid%", [], TextTermRole.Excluded)], TaxonomyLabelMatchDto.Empty);

    [Fact]
    public async Task BitmapPlanCount_AppliesSetLocalInTheCountsTransaction_OnlyWhenHygieneRequested()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var recorder = new RecordingCommandInterceptor();
        await using var db = NewRecordingContext(scope, recorder);

        // Hygiene OFF (the no-q gate): the COUNT runs bare — no SET LOCAL, and no ambient transaction
        // (the count command carries a null DbTransaction because BitmapPlanCount opened none).
        recorder.Clear();
        _ = await BitmapPlanCount.CountWithBitmapPlanAsync(
            db, NoText, c => db.JobAds.CountAsync(c), ct);

        recorder.Records.ShouldNotContain(
            r => r.CommandText.Contains(SeqScanGuc, StringComparison.OrdinalIgnoreCase),
            "hygiene OFF must NOT emit SET LOCAL enable_seqscan — the whole transaction is skipped.");
        CountRecord(recorder).Transaction.ShouldBeNull(
            "hygiene OFF must run the COUNT with no ambient transaction (no BEGIN was opened).");

        // Hygiene ON (the q gate): exactly one SET LOCAL, and it MUST share the COUNT's transaction —
        // else the session-local GUC is a no-op outside its block and the whole hygiene is theatre.
        recorder.Clear();
        _ = await BitmapPlanCount.CountWithBitmapPlanAsync(
            db, PositiveText, c => db.JobAds.CountAsync(c), ct);

        var setLocal = recorder.Records
            .Where(r => r.CommandText.Contains(SeqScanGuc, StringComparison.OrdinalIgnoreCase))
            .ShouldHaveSingleItem();
        setLocal.Transaction.ShouldNotBeNull();
        CountRecord(recorder).Transaction.ShouldBe(setLocal.Transaction,
            "the SET LOCAL and the COUNT must run in the SAME transaction/connection.");
        setLocal.CommandText.ShouldNotContain(NestLoopGuc, Case.Insensitive,
            "a count without minus-words keeps nested loops available.");

        // #2143 — minus-words: nested loops are prohibited too, in the same SET LOCAL and transaction.
        recorder.Clear();
        _ = await BitmapPlanCount.CountWithBitmapPlanAsync(
            db, ExcludingText, c => db.JobAds.CountAsync(c), ct);

        var excludingSetLocal = recorder.Records
            .Where(r => r.CommandText.Contains(SeqScanGuc, StringComparison.OrdinalIgnoreCase))
            .ShouldHaveSingleItem();
        excludingSetLocal.CommandText.ShouldContain(NestLoopGuc, Case.Insensitive,
            "a count with minus-words must prohibit the nested loop anti join that detoasts per probe.");
        CountRecord(recorder).Transaction.ShouldBe(excludingSetLocal.Transaction,
            "the SET LOCAL and the COUNT must run in the SAME transaction/connection.");
    }

    [Fact]
    public async Task CountAsync_GatesTheHygieneOnFreeTextQuery()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var expander = scope.ServiceProvider.GetRequiredService<IOccupationSynonymExpander>();
        var recorder = new RecordingCommandInterceptor();
        await using var db = NewRecordingContext(scope, recorder);

        // The REAL shared-port query over the recording context — proves the wiring (HasFreeTextPredicate)
        // threads the gate, not just that the helper honours a bool.
        var query = new JobAdSearchQuery(db, SearchTextResolvers.For(scope, expander));

        // No free-text q → gate closed → bare count.
        recorder.Clear();
        _ = await query.CountAsync(EmptyFilter(q: null), ct);
        recorder.Records.ShouldNotContain(
            r => r.CommandText.Contains(SeqScanGuc, StringComparison.OrdinalIgnoreCase),
            "a no-q count must skip the bitmap-plan hygiene transaction (the detoast risk is q-only).");

        // Free-text q present → gate open → hygiene applied (this is the TD-94 detoast path).
        recorder.Clear();
        _ = await query.CountAsync(EmptyFilter(q: "lärare"), ct);
        recorder.Records.ShouldContain(
            r => r.CommandText.Contains(SeqScanGuc, StringComparison.OrdinalIgnoreCase),
            "a q-count must apply SET LOCAL enable_seqscan = off (the GIN-bitmap coax).");

        // #2143 — a q of only minus-words has no positive residual but still reads search_vector, inside
        // the NOT EXISTS, so the gate must open for it too.
        recorder.Clear();
        _ = await query.CountAsync(EmptyFilter(q: "-deltid"), ct);
        recorder.Records.ShouldContain(
            r => r.CommandText.Contains(SeqScanGuc, StringComparison.OrdinalIgnoreCase)
                 && r.CommandText.Contains(NestLoopGuc, StringComparison.OrdinalIgnoreCase),
            "a count of only minus-words must apply the hygiene too: its exclusion reads search_vector.");
    }

    [Fact]
    public async Task SearchByStatusAsync_GatesTheHygieneOnFreeTextQuery()
    {
        // The status-only per-user count is the site the issue's own enumeration MISSED (dotnet-architect
        // caught it). Guard its wiring directly: a future refactor must not silently re-strand it ungated.
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var expander = scope.ServiceProvider.GetRequiredService<IOccupationSynonymExpander>();
        var recorder = new RecordingCommandInterceptor();
        await using var db = NewRecordingContext(scope, recorder);

        // searchQuery is unused by SearchByStatusAsync; pass a real one over the same recording context.
        var perUser = new PerUserJobAdSearchQuery(db, SearchTextResolvers.For(scope, expander), new JobAdSearchQuery(db, SearchTextResolvers.For(scope, expander)));
        var seeker = new JobSeekerId(Guid.NewGuid());
        var savedOnly = new JobAdStatusFilter(SavedOnly: true, AppliedOnly: false, HideApplied: false);

        recorder.Clear();
        _ = await perUser.SearchByStatusAsync(
            EmptyFilter(q: null), seeker, savedOnly, JobAdSortBy.PublishedAtDesc, page: 1, pageSize: 20, ct);
        recorder.Records.ShouldNotContain(
            r => r.CommandText.Contains(SeqScanGuc, StringComparison.OrdinalIgnoreCase),
            "the status-only count must skip hygiene with no free-text q.");

        recorder.Clear();
        _ = await perUser.SearchByStatusAsync(
            EmptyFilter(q: "lärare"), seeker, savedOnly, JobAdSortBy.PublishedAtDesc, page: 1, pageSize: 20, ct);
        recorder.Records.ShouldContain(
            r => r.CommandText.Contains(SeqScanGuc, StringComparison.OrdinalIgnoreCase),
            "the status-only count must apply hygiene when q is present (the issue-missed site).");

        recorder.Clear();
        _ = await perUser.SearchByStatusAsync(
            EmptyFilter(q: "-deltid"), seeker, savedOnly, JobAdSortBy.PublishedAtDesc, page: 1, pageSize: 20, ct);
        recorder.Records
            .Count(r => r.CommandText.Contains(SeqScanGuc, StringComparison.OrdinalIgnoreCase)
                        && r.CommandText.Contains(NestLoopGuc, StringComparison.OrdinalIgnoreCase))
            .ShouldBe(2, "the status-only count of only minus-words and its item read each read search_vector "
                         + "inside the NOT EXISTS.");
    }

    // #2143 (ADR 0167 D6) — the item read joins the count under the GUC pair only with minus-words.
    [Fact]
    public async Task SearchAsync_ReadsItsItemsUnderTheGucs_OnlyWithMinusWords()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var expander = scope.ServiceProvider.GetRequiredService<IOccupationSynonymExpander>();
        var recorder = new RecordingCommandInterceptor();
        await using var db = NewRecordingContext(scope, recorder);
        var query = new JobAdSearchQuery(db, SearchTextResolvers.For(scope, expander));

        recorder.Clear();
        _ = await query.SearchAsync(FirstPage("lärare"), ct);
        recorder.Records.Count(r => r.CommandText.Contains(SeqScanGuc, StringComparison.OrdinalIgnoreCase))
            .ShouldBe(1, "without minus-words only the count runs under the GUC.");
        recorder.Records[^1].Transaction.ShouldBeNull("without minus-words the item read runs bare.");

        recorder.Clear();
        _ = await query.SearchAsync(FirstPage("lärare -deltid"), ct);
        var setLocals = recorder.Records
            .Where(r => r.CommandText.Contains(NestLoopGuc, StringComparison.OrdinalIgnoreCase))
            .ToList();
        setLocals.Count.ShouldBe(2, "the count and the item read each prohibit the nested loop.");
        recorder.Records[^1].Transaction.ShouldNotBeNull();
        recorder.Records[^1].Transaction.ShouldBe(setLocals[1].Transaction,
            "the SET LOCAL and the item read must run in the SAME transaction/connection.");
    }

    [Fact]
    public async Task SearchPerUserAsync_ReadsItsItemsUnderTheGucs_WithMinusWords()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var expander = scope.ServiceProvider.GetRequiredService<IOccupationSynonymExpander>();
        var recorder = new RecordingCommandInterceptor();
        await using var db = NewRecordingContext(scope, recorder);
        var perUser = new PerUserJobAdSearchQuery(
            db, SearchTextResolvers.For(scope, expander), new JobAdSearchQuery(db, SearchTextResolvers.For(scope, expander)));
        var profile = new FullCandidateMatchProfile(
            new CandidateMatchProfile(
                Title: string.Empty,
                SsykGroupConceptIds: ["grp_hygiene"],
                PreferredRegionConceptIds: [],
                PreferredEmploymentTypeConceptIds: [],
                PreferredMunicipalityConceptIds: []),
            []);

        recorder.Clear();
        _ = await perUser.SearchPerUserAsync(
            EmptyFilter(q: "-deltid"), profile, grades: [], JobAdSortBy.PublishedAtDesc, orderByMatchRank: true,
            JobAdStatusFilter.None, new JobSeekerId(Guid.NewGuid()), page: 1, pageSize: 20, ct);

        var setLocals = recorder.Records
            .Where(r => r.CommandText.Contains(NestLoopGuc, StringComparison.OrdinalIgnoreCase))
            .ToList();
        setLocals.Count.ShouldBe(2, "the count and the item read each prohibit the nested loop.");
        recorder.Records[^1].Transaction.ShouldBe(setLocals[1].Transaction);
    }

    private static JobAdSearchCriteria FirstPage(string q) =>
        new(EmptyFilter(q), JobAdSortBy.PublishedAtDesc, Page: 1, PageSize: 20);

    private static JobAdFilterCriteria EmptyFilter(string? q) =>
        new([], [], [], [], [], [], Remote: false, Q: q);

    // A fresh AppDbContext against the SAME Testcontainers connection, carrying only the recording
    // interceptor (not the field-encryption pair — these counts touch no encrypted columns), so the SQL it
    // emits is observable without touching the shared [Collection("Api")] host wiring.
    private static AppDbContext NewRecordingContext(IServiceScope scope, RecordingCommandInterceptor recorder)
    {
        var connectionString = scope.ServiceProvider
            .GetRequiredService<AppDbContext>().Database.GetConnectionString();

        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
            .AddInterceptors(recorder)
            .Options);
    }

    private static CommandRecord CountRecord(RecordingCommandInterceptor recorder) =>
        recorder.Records.Single(r => r.CommandText.Contains("count(", StringComparison.OrdinalIgnoreCase));

    private sealed record CommandRecord(string CommandText, DbTransaction? Transaction);

    private sealed class RecordingCommandInterceptor : DbCommandInterceptor
    {
        // EF executes a context's commands sequentially, and each test uses one context on one thread, so a
        // plain list needs no synchronization here.
        private readonly List<CommandRecord> _records = [];

        public IReadOnlyList<CommandRecord> Records => _records;

        public void Clear() => _records.Clear();

        private void Record(DbCommand command) =>
            _records.Add(new CommandRecord(command.CommandText, command.Transaction));

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Record(command);
            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Record(command);
            return base.ScalarExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Record(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
