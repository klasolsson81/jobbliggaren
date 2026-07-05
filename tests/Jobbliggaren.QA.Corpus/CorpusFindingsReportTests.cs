using System.Text.Json;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.SavedSearches;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Jobbliggaren.Infrastructure.Taxonomy;
using Jobbliggaren.Infrastructure.TextAnalysis;
using Jobbliggaren.QA.Corpus.Generation;
using Jobbliggaren.QA.Corpus.Reporting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Jobbliggaren.QA.Corpus;

/// <summary>
/// Fas 4 STEG C, PR 4 — the findings report (CTO Fork 6 = 6C). One comprehensive run over BOTH
/// engines (deriver against the seeded taxonomy + reviewer in-memory) that emits a deterministic
/// markdown artifact (<c>artifacts/findings-report.md</c>, gitignored — the EMITTER is the
/// deliverable, the artifact is regenerated on demand and feeds a hardening STEG).
///
/// <para>The two MUST-gates (bearing invariant + crash-safety) are RE-ASSERTED here as
/// belt-and-suspenders and lead the report (rows 1–2). Everything else — deriver hit-rate,
/// reviewer verdict distribution, thin tier, the non-B4 pnr-echo finding — is OBSERVE-ONLY
/// (CTO Fork 6 / CLAUDE.md §2.5: fitness functions stay observe-only until a Klas ratchet).</para>
/// </summary>
public sealed class CorpusFindingsReportTests : IAsyncLifetime
{
    private const string FrozenMapResource =
        "Jobbliggaren.Infrastructure.Persistence.Migrations.Resources." +
        "occupation-name-to-ssyk-level-4.v30.json";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18").Build();
    private ServiceProvider _provider = default!;
    private IReadOnlyDictionary<string, string> _frozenMap = default!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(_postgres.GetConnectionString(),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention());
        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var appDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await appDb.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
        await appDb.Database.MigrateAsync();

        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Test");
        await new TaxonomySnapshotSeeder(
            ScopeFactory, env,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TaxonomySnapshotSeeder>.Instance)
            .StartAsync(CancellationToken.None);

        _frozenMap = await ReadFrozenMapAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _postgres.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private IServiceScopeFactory ScopeFactory => _provider.GetRequiredService<IServiceScopeFactory>();

    [Fact]
    public async Task EmitFindingsReport_AndGateInvariantAndCrashSafety()
    {
        var ct = TestContext.Current.CancellationToken;
        var config = CorpusConfig.Default;
        var groundTruth = await BuildGroundTruthAsync(ct);
        var titleCorpus = new CorpusGenerator(config).GenerateTitleCorpus(groundTruth);
        var cvCorpus = new CorpusGenerator(config).GenerateCvCorpus(groundTruth);

        // ── Deriver pass: hit-rate per stratum + crash count ────────────────
        var deriver = new OccupationCodeDeriver(new TaxonomyReadModel(ScopeFactory), new LocalTextAnalyzer(new SnowballStemmer()));
        var deriverStats = new Dictionary<CorpusStratum, int[]>(); // [total, exact, stemmed, empty]
        var deriverCrashes = 0;
        foreach (var c in titleCorpus)
        {
            var slot = deriverStats.TryGetValue(c.Stratum, out var s) ? s : (deriverStats[c.Stratum] = new int[4]);
            slot[0]++;
            try
            {
                var r = await deriver.DeriveAsync(c.Title, ct);
                if (r.Candidates.Any(x => x.MatchKind == OccupationMatchKind.ExactOccupationName)) slot[1]++;
                else if (r.Candidates.Count > 0) slot[2]++;
                else slot[3]++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { deriverCrashes++; } // observe-only crash tally; the GATE below asserts it is 0.
        }

        // ── Reviewer pass: verdict distribution + non-B4 pnr-echo finding ───
        var engine = new CvReviewEngine(new RubricProvider(), new ClicheLexicon(), new VerbMapper(),
            new LocalTextAnalyzer(new SnowballStemmer()));
        var verdictStats = new Dictionary<string, int[]>(StringComparer.Ordinal); // [pass, warn, fail, notassessed]
        var reviewerCrashes = 0;
        var fakePnrCases = 0;
        var nonB4PnrEchoCases = 0;
        var rubricVersion = "?";
        foreach (var c in cvCorpus)
        {
            try
            {
                var r = await engine.ReviewAsync(CvReviewContext.FromParsed(c.Cv), RenderProfile.Ats, ct);
                rubricVersion = r.RubricVersion.ToString();
                foreach (var v in r.Verdicts)
                {
                    var slot = verdictStats.TryGetValue(v.CriterionId, out var vs)
                        ? vs : (verdictStats[v.CriterionId] = new int[4]);
                    slot[(int)v.Verdict]++;
                }

                if (c.Stratum == CorpusStratum.FakePersonnummer)
                {
                    fakePnrCases++;
                    if (HasNonB4PersonnummerEcho(r)) nonB4PnrEchoCases++;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { reviewerCrashes++; }
        }

        var savedSearchesCreated = await CountSavedSearchesAsync(ct);

        // ── Build + write the deterministic artifact ────────────────────────
        var data = new FindingsReportData(
            config.Seed, config.Scale, rubricVersion,
            savedSearchesCreated,
            titleCorpus.Count, deriverCrashes,
            cvCorpus.Count, reviewerCrashes,
            fakePnrCases, nonB4PnrEchoCases,
            [.. deriverStats.OrderBy(kv => kv.Key).Select(kv =>
                new DeriverStratumStat(kv.Key, kv.Value[0], kv.Value[1], kv.Value[2], kv.Value[3]))],
            [.. verdictStats.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv =>
                new ReviewerCriterionStat(kv.Key, kv.Value[0], kv.Value[1], kv.Value[2], kv.Value[3]))]);

        await WriteArtifactAsync(FindingsReport.Build(data), ct);

        // ── GATES (CTO Fork 4/5) — re-asserted; observe-only metrics already written ──
        savedSearchesCreated.ShouldBe(0,
            $"BÄRANDE INVARIANT BRUTEN (ADR 0040 Beslut 4): {savedSearchesCreated} SavedSearch skapade av derivering.");
        deriverCrashes.ShouldBe(0, "KRASCH-SÄKERHET BRUTEN — derivern kraschade på minst en korpus-input.");
        reviewerCrashes.ShouldBe(0, "KRASCH-SÄKERHET BRUTEN — granskaren kraschade på minst en korpus-input.");
    }

    private static bool HasNonB4PersonnummerEcho(CvReviewResult result)
    {
        foreach (var v in result.Verdicts.Where(v => v.CriterionId != "B4"))
            foreach (var e in v.Evidence)
                if (e is TextSpanEvidence ts
                    && SwedishCorpusLexicon.FakePersonnummer.Any(f => ts.Span.Quote.Contains(f, StringComparison.Ordinal)))
                    return true;
        return false;
    }

    private static async Task WriteArtifactAsync(string markdown, CancellationToken ct)
    {
        // Deterministic path (no timestamp in the filename — CLAUDE.md §5): walk up from
        // bin/Debug/net10.0 to the project root, then artifacts/.
        var dir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "artifacts"));
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "findings-report.md"), markdown, ct);
    }

    private async Task<IReadOnlyList<OccupationGroundTruth>> BuildGroundTruthAsync(CancellationToken ct)
    {
        var tree = await new TaxonomyReadModel(ScopeFactory).GetTreeAsync(ct);
        var occLabelById = tree.OccupationFields.SelectMany(f => f.Occupations)
            .ToDictionary(o => o.ConceptId, o => o.Label, StringComparer.Ordinal);
        var groupIds = tree.OccupationFields.SelectMany(f => f.OccupationGroups)
            .Select(g => g.ConceptId).ToHashSet(StringComparer.Ordinal);
        var pairs = new List<OccupationGroundTruth>();
        foreach (var (occId, groupId) in _frozenMap)
            if (occLabelById.TryGetValue(occId, out var label) && groupIds.Contains(groupId))
                pairs.Add(new OccupationGroundTruth(label, groupId));
        return pairs;
    }

    private async Task<int> CountSavedSearchesAsync(CancellationToken ct)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Set<SavedSearch>().IgnoreQueryFilters().CountAsync(ct);
    }

    private static async Task<IReadOnlyDictionary<string, string>> ReadFrozenMapAsync()
    {
        var asm = typeof(TaxonomyReadModel).Assembly;
        await using var stream = asm.GetManifestResourceStream(FrozenMapResource);
        stream.ShouldNotBeNull();
        using var doc = await JsonDocument.ParseAsync(stream!);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in doc.RootElement.GetProperty("mappings").EnumerateObject())
            dict[entry.Name] = entry.Value.GetString()!;
        return dict;
    }
}
