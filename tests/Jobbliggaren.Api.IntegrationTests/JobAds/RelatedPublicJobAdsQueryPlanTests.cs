using System.Data.Common;
using System.Diagnostics;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;
using Jobbliggaren.Application.JobAds.Queries.GetRelatedPublicJobAds;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// The related read's plan (ADR 0182) over a <c>job_ads</c> table of the box's shape. One pin and one observation.
/// </summary>
/// <remarks>
/// <para>
/// <b>The pin:</b> the candidates statement reaches <c>ix_job_ads_occupation_group_concept_id</c>. The index is partial
/// (<c>WHERE occupation_group_concept_id IS NOT NULL</c>), so it is usable only while the statement's group predicate
/// proves that: a group test written so that a null-safe <c>OR … IS NULL</c> arm survives would silently turn every
/// related read into a scan, with every semantic test still green. The statement is the handler's own, captured as it
/// is sent, never a copy.
/// </para>
/// <para>
/// <b>The observation</b> is observe-only (ADR 0045): <c>EXPLAIN (ANALYZE, BUFFERS)</c>, the statement's text for both
/// anchor forms, and the handler's latency against class (a), p95 300 ms, are written to test output for ADR 0182's
/// Measurements. What it asserts is that the seeded state is the one measured.
/// </para>
/// <para>
/// <b>The seed</b> follows the box's shape, read 2026-10-23 17:31Z, counts only (CLAUDE.md §9.2 (a)), grouped by status
/// and occupation group and never by employer: 44 718 Active, all listed, and 107 739 Archived; no Erased row; 396
/// groups and no row without one; the largest group 6 925 rows, 2 161 of them listed, over 281 kommuner, 19 of the listed
/// without a kommun. Employer names are synthetic. The rows are bulk-inserted in the shape the ingest writes, because
/// 150 000 rows through EF would measure the seed, not the read; the plan rests on the statistics, which production's
/// own writers produce, and never on these rows' identity.
/// </para>
/// </remarks>
[Collection("JobAdBrowsePlan")]
[Trait("Category", "SmokeTest")]
public class RelatedPublicJobAdsQueryPlanTests(JobAdBrowsePlanFixture fixture, ITestOutputHelper output)
{
    private const string IndexName = "ix_job_ads_occupation_group_concept_id";
    private const string BigGroup = "grp_big";
    private const int BigGroupListed = 2_161;
    private const int BigGroupListedWithoutKommun = 19;
    private const int BigGroupArchived = 6_925 - BigGroupListed;
    private const int BigGroupKommuner = 281;
    private const int OtherGroups = 395;
    private const int OtherListed = 44_718 - BigGroupListed;
    private const int OtherArchived = 107_739 - BigGroupArchived;
    private const int Lans = 21;
    private const int Iterations = 40;

    private static readonly Guid PlacedAnchor = Guid.Parse("5b6e2c0a-0000-4000-8000-000000000001");
    private static readonly Guid PlacelessAnchor = Guid.Parse("5b6e2c0a-0000-4000-8000-000000000002");
    private static readonly Guid PlacelessSmallGroupAnchor = Guid.Parse("5b6e2c0a-0000-4000-8000-000000000003");

    [Fact]
    public async Task TheCandidatesStatement_ReachesTheOccupationGroupIndex()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await SeedAsync(db, ct);

        var (items, candidates) = await RelatedAsync(db, PlacedAnchor, ct);
        items.Count.ShouldBe(RelatedPublicJobAdLimits.MaxRows);
        candidates.Sql.ShouldContain("CASE", Case.Sensitive, "an anchor with a kommun and a län orders by the tier rank");

        var plan = string.Join(Environment.NewLine, await ExplainAsync(db, "EXPLAIN ", candidates, ct));

        // The index NAME, not "using <name>": a Bitmap Index Scan prints "on <name>".
        plan.ShouldContain(IndexName, Case.Insensitive,
            $"The related candidates no longer reach {IndexName}: the index is gone, or the group predicate changed "
            + "shape so PostgreSQL can no longer prove it implies the index's. Every related read now scans job_ads."
            + Environment.NewLine + "Plan:" + Environment.NewLine + plan);
    }

    [Fact]
    public async Task TheRelatedRead_AgainstAdr0045ClassA_ObserveOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var seeding = Stopwatch.StartNew();
        await SeedAsync(db, ct);
        output.WriteLine(
            $"Seeded {BigGroupListed + BigGroupArchived + OtherListed + OtherArchived + 3:N0} ads in "
            + $"{seeding.Elapsed.TotalSeconds:F0} s; the anchors' group {BigGroupListed:N0} listed + {BigGroupArchived:N0} archived.");

        foreach (var (name, anchor) in new[]
                 {
                     ("kommun and län set", PlacedAnchor),
                     ("no kommun, no län", PlacelessAnchor),
                     ("no kommun, no län, in a small group", PlacelessSmallGroupAnchor),
                 })
        {
            var (items, candidates) = await RelatedAsync(db, anchor, ct);
            items.Count.ShouldBe(RelatedPublicJobAdLimits.MaxRows);
            items.Select(r => r.CompanyName).Distinct(StringComparer.Ordinal).Count().ShouldBe(RelatedPublicJobAdLimits.MaxRows);

            output.WriteLine($"--- Anchor with {name}: the candidates statement as sent");
            output.WriteLine(candidates.Sql);
            output.WriteLine($"--- Anchor with {name}: EXPLAIN (ANALYZE, BUFFERS)");
            foreach (var line in await ExplainAsync(db, "EXPLAIN (ANALYZE, BUFFERS) ", candidates, ct))
                output.WriteLine(line);

            var (p50, p95) = await MeasureAsync(() => HandleAsync(db, anchor, ct).AsTask(), ct);
            output.WriteLine(
                $"Anchor with {name}: handler, {Iterations} warmed iterations, nearest-rank p50 {p50:F1} ms, p95 {p95:F1} ms "
                + "(ADR 0045 class (a): p95 300 ms).");
        }
    }

    private static async Task<(IReadOnlyList<PublicJobAdSummaryDto> Items, CapturedCommand Candidates)> RelatedAsync(
        AppDbContext db, Guid anchor, CancellationToken ct)
    {
        using var capture = new CommandCapture(db);
        var result = await HandleAsync(db, anchor, ct);
        result.IsSuccess.ShouldBeTrue();
        capture.Commands.Count.ShouldBe(2, "the anchor read, then the candidates read");
        return (result.Value.Items, capture.Commands[1]);
    }

    private static ValueTask<Result<RelatedPublicJobAdsDto>> HandleAsync(
        AppDbContext db, Guid anchor, CancellationToken ct) =>
        new GetRelatedPublicJobAdsQueryHandler(db, Taxonomy(), new SystemClock())
            .Handle(new GetRelatedPublicJobAdsQuery(anchor), ct);

    // CA2012: an NSubstitute stub on a ValueTask-returning port member is the library's own idiom.
#pragma warning disable CA2012
    private static ITaxonomyReadModel Taxonomy()
    {
        var taxonomy = Substitute.For<ITaxonomyReadModel>();
        taxonomy.ResolveLabelsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<IReadOnlyList<TaxonomyLabelDto>>(
                call.Arg<IReadOnlyList<string>>().Select(id => new TaxonomyLabelDto(id, null)).ToList()));
        return taxonomy;
    }
#pragma warning restore CA2012

    private static async Task<(double P50, double P95)> MeasureAsync(Func<Task> call, CancellationToken ct)
    {
        for (var i = 0; i < 5; i++)
            await call();

        var samples = new List<double>(Iterations);
        for (var i = 0; i < Iterations; i++)
        {
            ct.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            await call();
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        return (Percentile(samples, 0.50), Percentile(samples, 0.95));
    }

    private static double Percentile(List<double> sorted, double p) =>
        sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];

    private static async Task<List<string>> ExplainAsync(
        AppDbContext db, string explain, CapturedCommand statement, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = new NpgsqlCommand(explain + statement.Sql, (NpgsqlConnection)db.Database.GetDbConnection());
            command.Parameters.AddRange(statement.Parameters.Select(p => p.Clone()).ToArray());
            var plan = new List<string>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                plan.Add(reader.GetString(0));
            return plan;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// TRUNCATE-and-own, then seed the box's shape, then VACUUM (ANALYZE): TRUNCATE wipes the statistics, and without
    /// them the planner falls back on default selectivities and the choice is arbitrary.
    /// </summary>
    private static async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        db.Database.SetCommandTimeout(300);
        await db.Database.ExecuteSqlRawAsync("TRUNCATE job_ads CASCADE;", ct);

        var kommun = $"'k' || (i % {BigGroupKommuner})";
        var lan = $"'l' || ((i % {BigGroupKommuner}) % {Lans})";
        await InsertAsync(db, "big-listed-", BigGroupListed - BigGroupListedWithoutKommun, $"'{BigGroup}'", kommun, lan, "Active", ct);
        await InsertAsync(db, "big-placeless-", BigGroupListedWithoutKommun, $"'{BigGroup}'", "NULL", "NULL", "Active", ct);
        await InsertAsync(db, "big-archived-", BigGroupArchived, $"'{BigGroup}'", kommun, lan, "Archived", ct);
        var group = $"'g' || (1 + i % {OtherGroups})";
        await InsertAsync(db, "other-listed-", OtherListed, group, "'k' || (i % 290)", $"'l' || ((i % 290) % {Lans})", "Active", ct);
        await InsertAsync(db, "other-archived-", OtherArchived, group, "'k' || (i % 290)", $"'l' || ((i % 290) % {Lans})", "Archived", ct);

        await db.Database.ExecuteSqlRawAsync(
            Insert("anchor-", 1, $"'{BigGroup}'", "'k7'", "'l7'", "Active", $"'{PlacedAnchor}'::uuid", "'Ankarbolaget AB'"), ct);
        await db.Database.ExecuteSqlRawAsync(
            Insert("anchor-placeless-", 1, $"'{BigGroup}'", "NULL", "NULL", "Active", $"'{PlacelessAnchor}'::uuid", "'Ankarbolaget AB'"), ct);
        await db.Database.ExecuteSqlRawAsync(
            Insert("anchor-small-", 1, "'g1'", "NULL", "NULL", "Active", $"'{PlacelessSmallGroupAnchor}'::uuid", "'Ankarbolaget AB'"), ct);

        await db.Database.ExecuteSqlRawAsync("VACUUM (ANALYZE) job_ads;", ct);
    }

    private static Task<int> InsertAsync(
        AppDbContext db, string marker, int count, string group, string kommun, string lan, string status, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync(
            Insert(marker, count, group, kommun, lan, status, "gen_random_uuid()", "'Arbetsgivare ' || (i % 700) || ' AB'"), ct);

    /// <summary>
    /// One bulk insert in the ingest's shape: imported from Platsbanken with its external id and payload, a wide
    /// description, a deadline 60 days out, published one minute apart.
    /// </summary>
    private static string Insert(
        string marker, int count, string group, string kommun, string lan, string status, string id, string company) =>
        "INSERT INTO job_ads ("
        + "id, title, company_name, description, url, source, external_source, external_id, raw_payload, status, "
        + "published_at, expires_at, created_at, remote, occupation_group_concept_id, municipality_concept_id, region_concept_id) "
        + $"SELECT {id}, 'Annons ' || i, {company}, "
        + "'Beskrivning ' || i || '. ' || repeat('Arbetsuppgifter, kvalifikationer och villkor for tjansten. ', 30), "
        + "'https://example.com/jobb/' || i, 'Platsbanken', 'Platsbanken', "
        + $"'{marker}' || i, jsonb_build_object('id', '{marker}' || i), '{status}', "
        + "now() - (i || ' minutes')::interval, now() + interval '60 days', now() - interval '2 days', false, "
        + $"{group}, {kommun}, {lan} "
        + $"FROM generate_series(0, {count - 1}) AS i;";

    private sealed class SystemClock : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private sealed record CapturedCommand(string Sql, NpgsqlParameter[] Parameters);

    /// <summary>The statements EF sends for one context while the capture is open, read from its diagnostic events.</summary>
    private sealed class CommandCapture
        : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly DbContext _context;
        private readonly List<IDisposable> _subscriptions = [];
        private readonly List<CapturedCommand> _commands = [];

        public CommandCapture(DbContext context)
        {
            _context = context;
            var all = DiagnosticListener.AllListeners.Subscribe(this);
            lock (_subscriptions)
                _subscriptions.Add(all);
        }

        public IReadOnlyList<CapturedCommand> Commands
        {
            get
            {
                lock (_commands)
                    return [.. _commands];
            }
        }

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name != DbLoggerCategory.Name)
                return;
            var subscription = listener.Subscribe(this);
            lock (_subscriptions)
                _subscriptions.Add(subscription);
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key != RelationalEventId.CommandExecuting.Name
                || value.Value is not CommandEventData data
                || !ReferenceEquals(data.Context, _context))
                return;

            var parameters = data.Command.Parameters.Cast<DbParameter>()
                .Select(p => ((NpgsqlParameter)p).Clone())
                .ToArray();
            lock (_commands)
                _commands.Add(new CapturedCommand(data.Command.CommandText, parameters));
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void Dispose()
        {
            lock (_subscriptions)
            {
                foreach (var subscription in _subscriptions)
                    subscription.Dispose();
                _subscriptions.Clear();
            }
        }
    }
}
