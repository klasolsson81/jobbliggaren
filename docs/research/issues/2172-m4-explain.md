# ADR 0176 measurement M4: `EXPLAIN (ANALYZE, BUFFERS)` of the catalogue's reads (#2172)

**Taken:** 2026-10-18 at 15:44 UTC (`work_mem` comparison 15:52 UTC), on branch `feat/seo-s5-public-catalogue`, against Testcontainers `postgres:18` with the repository's migrations.
**Why here:** ADR 0176 asks for a one-off measurement with the command that regenerates it, not a committed CI test, because of the wall clock. This note is that command.

## How to regenerate it

1. Copy the class below into `tests/Jobbliggaren.Api.IntegrationTests/JobAds/CatalogueExplainMeasurement.cs`.
2. Set `JBL_M4_OUT` to the output file.
3. Run:

   ```bash
   dotnet test --project tests/Jobbliggaren.Api.IntegrationTests -- --filter-class "*CatalogueExplainMeasurement"
   ```
4. Delete the class.

**What it does:**
- It seeds 45 000 listed ads, 1 000 archived ads and 500 ads past their deadline, each through `JobAd.Import` (archived through the aggregate).
- It runs `ANALYZE job_ads`.
- It captures the commands EF sends for the real sampler, for the S4 sampler form (`8ce73fa36`) and for the real handler on pages 1 and 2 000.
- It runs `EXPLAIN (ANALYZE, BUFFERS)` on each with the captured parameters, the sampler reads under the container's default `work_mem` and under the box's `8MB` (`deploy/docker-compose.yml`).

## What it showed

**The content read.**
- It is a bitmap index scan on `pk_job_ads` with `id = ANY(…)` as the index condition, about 0.1 ms.
- Page 1 and page 2 000 read the same way, so depth costs nothing.
- The ids travel as one `uuid[]` parameter.

**The sampler read.**
- Its plan keeps its shape: a sequential scan, a sort on `id`, a limit. Only its target list widens.
- The sort's memory does not stay the same. At 45 000 rows, the S4 read sorted in 1 537 kB, and the widened read sorts in 4 701 kB.
  - Under the box's `work_mem` of 8 MB, it stays an in-memory quicksort.
  - Under the container default of 4 MB, it becomes an external merge, writing 1 504 kB to disk.
- That is about 27 ms against 24 ms, once per five-minute tick.

**Computed, not measured:** at the snapshot's ceiling of 80 000 rows, the widened sort would need about 8 360 kB (4 701 kB × 80 000 / 45 000), just over the box's 8 192 kB, and would then spill to disk once per tick.

## The readings

rows in job_ads: 46500; seed 45000 listed-or-future-deadline, 1000 archived, 500 past deadline; `ANALYZE job_ads` run after the seed.
snapshot count: 45000

## The sampler read, widened (S5), container default work_mem

```sql
SELECT j.id, j.published_at
FROM job_ads AS j
WHERE j.external_id IS NOT NULL AND j.external_source IS NOT NULL AND j.external_source = 'Platsbanken' AND j.status = 'Active' AND (j.expires_at IS NULL OR j.expires_at >= @now)
ORDER BY j.id
LIMIT @p
```
parameters: @now (TimestampTz), @p (Integer)
work_mem: 4MB
```
Limit  (cost=6043.77..6131.40 rows=35053 width=24) (actual time=24.450..30.534 rows=45000.00 loops=1)
  Buffers: shared hit=2584, temp read=188 written=189
  ->  Sort  (cost=6043.77..6131.40 rows=35053 width=24) (actual time=24.449..27.691 rows=45000.00 loops=1)
        Sort Key: id
        Sort Method: external merge  Disk: 1504kB
        Buffers: shared hit=2584, temp read=188 written=189
        ->  Seq Scan on job_ads j  (cost=0.00..3397.75 rows=35053 width=24) (actual time=0.009..11.356 rows=45000.00 loops=1)
              Filter: ((external_id IS NOT NULL) AND (external_source IS NOT NULL) AND ((expires_at IS NULL) OR (expires_at >= '2026-10-18 12:00:00+00'::timestamp with time zone)) AND ((external_source)::text = 'Platsbanken'::text) AND ((status)::text = 'Active'::text))
              Rows Removed by Filter: 1500
              Buffers: shared hit=2584
Planning:
  Buffers: shared hit=9
Planning Time: 0.141 ms
Execution Time: 32.648 ms
```

## The sampler read, widened (S5), work_mem 8MB (deploy/docker-compose.yml)

```sql
SELECT j.id, j.published_at
FROM job_ads AS j
WHERE j.external_id IS NOT NULL AND j.external_source IS NOT NULL AND j.external_source = 'Platsbanken' AND j.status = 'Active' AND (j.expires_at IS NULL OR j.expires_at >= @now)
ORDER BY j.id
LIMIT @p
```
parameters: @now (TimestampTz), @p (Integer)
work_mem: 8MB
```
Limit  (cost=6043.77..6131.40 rows=35053 width=24) (actual time=21.986..26.145 rows=45000.00 loops=1)
  Buffers: shared hit=2584
  ->  Sort  (cost=6043.77..6131.40 rows=35053 width=24) (actual time=21.985..23.636 rows=45000.00 loops=1)
        Sort Key: id
        Sort Method: quicksort  Memory: 4701kB
        Buffers: shared hit=2584
        ->  Seq Scan on job_ads j  (cost=0.00..3397.75 rows=35053 width=24) (actual time=0.009..11.333 rows=45000.00 loops=1)
              Filter: ((external_id IS NOT NULL) AND (external_source IS NOT NULL) AND ((expires_at IS NULL) OR (expires_at >= '2026-10-18 12:00:00+00'::timestamp with time zone)) AND ((external_source)::text = 'Platsbanken'::text) AND ((status)::text = 'Active'::text))
              Rows Removed by Filter: 1500
              Buffers: shared hit=2584
Planning:
  Buffers: shared hit=9
Planning Time: 0.212 ms
Execution Time: 27.425 ms
```

## The sampler read, before widening (S4, 8ce73fa36), container default work_mem

```sql
SELECT j.id
FROM job_ads AS j
WHERE j.external_id IS NOT NULL AND j.external_source IS NOT NULL AND j.external_source = 'Platsbanken' AND j.status = 'Active' AND (j.expires_at IS NULL OR j.expires_at >= @now)
ORDER BY j.id
LIMIT @p
```
parameters: @now (TimestampTz), @p (Integer)
work_mem: 4MB
```
Limit  (cost=6043.77..6131.40 rows=35053 width=16) (actual time=18.451..22.461 rows=45000.00 loops=1)
  Buffers: shared hit=2584
  ->  Sort  (cost=6043.77..6131.40 rows=35053 width=16) (actual time=18.450..19.999 rows=45000.00 loops=1)
        Sort Key: id
        Sort Method: quicksort  Memory: 1537kB
        Buffers: shared hit=2584
        ->  Seq Scan on job_ads j  (cost=0.00..3397.75 rows=35053 width=16) (actual time=0.009..10.656 rows=45000.00 loops=1)
              Filter: ((external_id IS NOT NULL) AND (external_source IS NOT NULL) AND ((expires_at IS NULL) OR (expires_at >= '2026-10-18 12:00:00+00'::timestamp with time zone)) AND ((external_source)::text = 'Platsbanken'::text) AND ((status)::text = 'Active'::text))
              Rows Removed by Filter: 1500
              Buffers: shared hit=2584
Planning:
  Buffers: shared hit=9
Planning Time: 0.158 ms
Execution Time: 23.703 ms
```

## The sampler read, before widening (S4, 8ce73fa36), work_mem 8MB

```sql
SELECT j.id
FROM job_ads AS j
WHERE j.external_id IS NOT NULL AND j.external_source IS NOT NULL AND j.external_source = 'Platsbanken' AND j.status = 'Active' AND (j.expires_at IS NULL OR j.expires_at >= @now)
ORDER BY j.id
LIMIT @p
```
parameters: @now (TimestampTz), @p (Integer)
work_mem: 8MB
```
Limit  (cost=6043.77..6131.40 rows=35053 width=16) (actual time=18.668..22.800 rows=45000.00 loops=1)
  Buffers: shared hit=2584
  ->  Sort  (cost=6043.77..6131.40 rows=35053 width=16) (actual time=18.667..20.264 rows=45000.00 loops=1)
        Sort Key: id
        Sort Method: quicksort  Memory: 1537kB
        Buffers: shared hit=2584
        ->  Seq Scan on job_ads j  (cost=0.00..3397.75 rows=35053 width=16) (actual time=0.008..10.647 rows=45000.00 loops=1)
              Filter: ((external_id IS NOT NULL) AND (external_source IS NOT NULL) AND ((expires_at IS NULL) OR (expires_at >= '2026-10-18 12:00:00+00'::timestamp with time zone)) AND ((external_source)::text = 'Platsbanken'::text) AND ((status)::text = 'Active'::text))
              Rows Removed by Filter: 1500
              Buffers: shared hit=2584
Planning:
  Buffers: shared hit=9
Planning Time: 0.142 ms
Execution Time: 24.078 ms
```

## The content read, page 1

```sql
SELECT j.id, j.title, j.company_name, j.municipality_concept_id, j.region_concept_id, j.published_at
FROM job_ads AS j
WHERE j.id = ANY (@typedIds) AND j.external_id IS NOT NULL AND j.external_source IS NOT NULL AND j.external_source = 'Platsbanken' AND j.status = 'Active' AND (j.expires_at IS NULL OR j.expires_at >= @now)
```
parameters: @typedIds (uuid[], 20 elements), @now (TimestampTz)
work_mem: 4MB
```
Bitmap Heap Scan on job_ads j  (cost=86.00..161.18 rows=15 width=140) (actual time=0.064..0.094 rows=20.00 loops=1)
  Recheck Cond: (id = ANY ('{…the page's 20 ids…}'::uuid[]))
  Filter: ((external_id IS NOT NULL) AND (external_source IS NOT NULL) AND ((expires_at IS NULL) OR (expires_at >= '2026-10-18 12:00:00+00'::timestamp with time zone)) AND ((external_source)::text = 'Platsbanken'::text) AND ((status)::text = 'Active'::text))
  Heap Blocks: exact=20
  Buffers: shared hit=56
  ->  Bitmap Index Scan on pk_job_ads  (cost=0.00..85.95 rows=20 width=0) (actual time=0.053..0.053 rows=20.00 loops=1)
        Index Cond: (id = ANY ('{…the page's 20 ids…}'::uuid[]))
        Index Searches: 18
        Buffers: shared hit=36
Planning:
  Buffers: shared hit=9
Planning Time: 0.154 ms
Execution Time: 0.109 ms
```

## The content read, page 2000

```sql
SELECT j.id, j.title, j.company_name, j.municipality_concept_id, j.region_concept_id, j.published_at
FROM job_ads AS j
WHERE j.id = ANY (@typedIds) AND j.external_id IS NOT NULL AND j.external_source IS NOT NULL AND j.external_source = 'Platsbanken' AND j.status = 'Active' AND (j.expires_at IS NULL OR j.expires_at >= @now)
```
parameters: @typedIds (uuid[], 20 elements), @now (TimestampTz)
work_mem: 4MB
```
Bitmap Heap Scan on job_ads j  (cost=86.00..161.18 rows=15 width=140) (actual time=0.054..0.075 rows=20.00 loops=1)
  Recheck Cond: (id = ANY ('{…the page's 20 ids…}'::uuid[]))
  Filter: ((external_id IS NOT NULL) AND (external_source IS NOT NULL) AND ((expires_at IS NULL) OR (expires_at >= '2026-10-18 12:00:00+00'::timestamp with time zone)) AND ((external_source)::text = 'Platsbanken'::text) AND ((status)::text = 'Active'::text))
  Heap Blocks: exact=20
  Buffers: shared hit=56
  ->  Bitmap Index Scan on pk_job_ads  (cost=0.00..85.95 rows=20 width=0) (actual time=0.046..0.046 rows=20.00 loops=1)
        Index Cond: (id = ANY ('{…the page's 20 ids…}'::uuid[]))
        Index Searches: 18
        Buffers: shared hit=36
Planning:
  Buffers: shared hit=9
Planning Time: 0.118 ms
Execution Time: 0.084 ms
```

## The class

```csharp
#pragma warning disable CA1305
using System.Data.Common;
using System.Text;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Listing;
using Jobbliggaren.Application.JobAds.Publication;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAdCatalogue;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

// ADR 0176 M4: a one-off EXPLAIN (ANALYZE, BUFFERS) measurement. NOT committed (the wall clock); its text is kept
// in docs/research/issues/2172-m4-explain.md. Seeds 45 000 imported ads through JobAd.Import, ANALYZEs job_ads, then
// explains the content read (pages 1 and 2 000), the widened sampler read and the S4 sampler read.
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class CatalogueExplainMeasurement(SharedPostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private const int Listed = 45_000;
    private const int Archived = 1_000;
    private const int Expired = 500;

    private readonly MutableFakeDateTimeProvider _clock = new() { UtcNow = Now };
    private readonly Capture _capture = new();
    private string _cs = string.Empty;
    private ServiceProvider _provider = default!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _cs = await postgres.CreateDatabaseAsync();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(_cs, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(_capture)
            .ConfigureWarnings(w => w.Log(CoreEventId.ManyServiceProvidersCreatedWarning)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        _provider = services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await postgres.DropDatabaseAsync(_cs);
    }

    [Fact]
    public async Task Explain()
    {
        var output = Environment.GetEnvironmentVariable("JBL_M4_OUT") ?? throw new InvalidOperationException("JBL_M4_OUT");
        var report = new StringBuilder();
        report.AppendLine($"# ADR 0176 M4 — EXPLAIN (ANALYZE, BUFFERS), {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC");

        var rng = new Random(2176);
        _capture.On = false;
        var batch = new List<JobAd>(1_000);
        for (var i = 0; i < Listed + Archived + Expired; i++)
        {
            var published = i >= Listed + Archived ? Now.AddDays(-rng.Next(2, 30)) : Now.AddMinutes(-rng.Next(1, 60 * 24 * 30));
            DateTimeOffset? expires = i >= Listed + Archived ? Now.AddMinutes(-rng.Next(1, 600)) : rng.Next(3) == 0 ? null : Now.AddDays(rng.Next(1, 60));
            var ad = Imported(published, expires);
            if (i >= Listed && i < Listed + Archived) ad.Archive(_clock).IsSuccess.ShouldBeTrue();
            batch.Add(ad);
            if (batch.Count == 1_000) await FlushAsync(batch);
        }
        await FlushAsync(batch);

        await using (var conn = new NpgsqlConnection(_cs))
        {
            await conn.OpenAsync(Ct);
            await using var analyze = new NpgsqlCommand("ANALYZE job_ads", conn);
            await analyze.ExecuteNonQueryAsync(Ct);
            await using var count = new NpgsqlCommand("SELECT count(*) FROM job_ads", conn);
            report.AppendLine($"rows in job_ads: {await count.ExecuteScalarAsync(Ct)}; seed {Listed} listed-or-future-deadline, {Archived} archived, {Expired} past deadline; `ANALYZE job_ads` run after the seed.");
        }

        // The widened sampler read (S5), through the real sampler.
        _capture.On = true;
        _capture.Clear();
        var sampler = new ListedJobAdSnapshotSampler(
            _provider.GetRequiredService<IServiceScopeFactory>(), _clock, NullLogger<ListedJobAdSnapshotSampler>.Instance);
        await sampler.SampleAsync(Ct);
        var widened = _capture.Single();
        report.AppendLine($"snapshot count: {sampler.Current!.Count}");
        await ExplainAsync(report, "The sampler read, widened (S5), container default work_mem", widened);
        await ExplainAsync(report, "The sampler read, widened (S5), work_mem 8MB (deploy/docker-compose.yml)", widened, "8MB");

        // The S4 sampler read, as it stood at 8ce73fa36.
        _capture.Clear();
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            _ = await db.JobAds.AsNoTracking().Where(JobAdPublication.IsListed(Now)).OrderBy(j => j.Id)
                .Take(ListedJobAdSnapshotLimits.Ceiling + 1).Select(j => j.Id).ToListAsync(Ct);
        }
        var s4 = _capture.Single();
        await ExplainAsync(report, "The sampler read, before widening (S4, 8ce73fa36), container default work_mem", s4);
        await ExplainAsync(report, "The sampler read, before widening (S4, 8ce73fa36), work_mem 8MB", s4, "8MB");

        // The content read, through the handler, on page 1 and a deep page.
        foreach (var page in new[] { 1, 2_000 })
        {
            _capture.Clear();
            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var answer = await new GetPublicJobAdCatalogueQueryHandler(sampler, db, Substitute.For<ITaxonomyReadModel>(), _clock)
                .Handle(new GetPublicJobAdCatalogueQuery(page), Ct);
            answer.ShouldBeOfType<PublicJobAdCataloguePage.Available>().Rows.Items.Count.ShouldBe(20);
            await ExplainAsync(report, $"The content read, page {page}", _capture.Single());
        }

        await File.WriteAllTextAsync(output, report.ToString(), Ct);
    }

    private async Task FlushAsync(List<JobAd> batch)
    {
        if (batch.Count == 0) return;
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.JobAds.AddRange(batch);
        await db.SaveChangesAsync(Ct);
        batch.Clear();
    }

    private async Task ExplainAsync(StringBuilder report, string heading, Captured command, string? workMem = null)
    {
        report.AppendLine().AppendLine($"## {heading}").AppendLine().AppendLine("```sql").AppendLine(command.Text).AppendLine("```");
        report.AppendLine($"parameters: {string.Join(", ", command.Parameters.Select(p => $"{p.Name} ({p.Type}{(p.Value is Array a ? $", {a.Length} elements" : "")})"))}");
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(Ct);
        await using (var show = new NpgsqlCommand(workMem is null ? "SHOW work_mem" : $"SET work_mem = '{workMem}'; SHOW work_mem", conn))
            report.AppendLine($"work_mem: {await show.ExecuteScalarAsync(Ct)}");
        await using var cmd = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS) " + command.Text, conn);
        foreach (var p in command.Parameters)
            cmd.Parameters.Add(new NpgsqlParameter(p.Name, p.Value) { NpgsqlDbType = p.NpgsqlType });
        report.AppendLine("```");
        await using var reader = await cmd.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct)) report.AppendLine(reader.GetString(0));
        report.AppendLine("```");
    }

    private JobAd Imported(DateTimeOffset publishedAt, DateTimeOffset? expiresAt)
    {
        var externalId = TestIds.ExternalId();
        return JobAd.Import(
            title: "Undersköterska till hemtjänsten",
            company: Company.Create("Exempelbolaget AB").Value,
            description: "Vi söker en undersköterska.",
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: $"{{\"id\":\"{externalId}\"}}",
            facets: TestFacets.None,
            declaredContacts: [],
            publishedAt: publishedAt,
            expiresAt: expiresAt,
            clock: _clock,
            extractTerms: TestKeywordExtraction.None).Value;
    }

    private sealed record CapturedParameter(string Name, object? Value, NpgsqlTypes.NpgsqlDbType NpgsqlType, string Type);

    private sealed record Captured(string Text, IReadOnlyList<CapturedParameter> Parameters);

    private sealed class Capture : DbCommandInterceptor
    {
        private readonly List<Captured> _commands = [];
        public bool On { get; set; }

        public void Clear()
        {
            lock (_commands) _commands.Clear();
        }

        public Captured Single()
        {
            lock (_commands) return _commands.Where(c => c.Text.Contains("job_ads", StringComparison.Ordinal)).ShouldHaveSingleItem();
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (On)
            {
                var parameters = command.Parameters.Cast<NpgsqlParameter>()
                    .Select(p => new CapturedParameter(p.ParameterName, p.Value, p.NpgsqlDbType, p.NpgsqlDbType.ToString()))
                    .ToList();
                lock (_commands) _commands.Add(new Captured(command.CommandText, parameters));
            }
            return ValueTask.FromResult(result);
        }
    }
}
```
