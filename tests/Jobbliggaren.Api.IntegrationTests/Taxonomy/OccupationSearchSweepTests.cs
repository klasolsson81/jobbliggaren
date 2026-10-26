using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Taxonomy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Taxonomy;

/// <summary>
/// #2198 (ADR 0180) — the measurement the issue asks for, over the real seeded snapshot, the real
/// read model, the shared index and the real searcher. The expected answers come from
/// <see cref="OccupationNameOracle"/> (the committed snapshot joined to the frozen map, read by
/// resource name), so the sweep counts what the files hold and never a hard-coded total. A name
/// that fails is a rule to fix, never a test to relax.
/// </summary>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class OccupationSearchSweepTests : IAsyncLifetime
{
    private const string Mjukvaru = "DJh5_yyF_hEM";
    private const string OvrigaIt = "UxT1_tPF_Kbg";
    private const string LagerTerminal = "kLyY_rwr_aJr";
    private const string SkolChefer = "z8di_AqX_GBr";

    private readonly SharedPostgresFixture _postgres;
    private string _connectionString = string.Empty;
    private ServiceProvider _provider = default!;
    private OccupationSearcher _sut = default!;
    private OccupationNameOracle.Expected _expected = default!;

    public OccupationSearchSweepTests(SharedPostgresFixture postgres) => _postgres = postgres;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await _postgres.CreateDatabaseAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options =>
            options
                .UseNpgsql(_connectionString,
                    npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                .UseSnakeCaseNamingConvention()
                .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning)));
        _provider = services.BuildServiceProvider();

        var scopeFactory = _provider.GetRequiredService<IServiceScopeFactory>();
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Test");
        await new TaxonomySnapshotSeeder(
                scopeFactory, env,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<TaxonomySnapshotSeeder>.Instance)
            .StartAsync(CancellationToken.None);

        _sut = new OccupationSearcher(new OccupationNameIndex(new TaxonomyReadModel(scopeFactory)));
        _expected = await OccupationNameOracle.LoadAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _postgres.DropDatabaseAsync(_connectionString);
        GC.SuppressFinalize(this);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SearchAsync_EveryMappedNameTypedInFull_IsInRowOneAndSelectsItsOwnGroup()
    {
        _expected.Names.ShouldNotBeEmpty();
        var failures = new List<string>();

        foreach (var name in _expected.Names)
        {
            var result = await _sut.SearchAsync(name.NameLabel, Ct);
            var first = result.Rows.Count > 0 ? result.Rows[0] : null;
            var selects = first?.Kind switch
            {
                OccupationSearchRowKind.Group =>
                    first.GroupConceptId == name.GroupConceptId
                    && first.MatchedNames.Contains(new OccupationSearchMatchedName(name.NameConceptId, name.NameLabel)),
                OccupationSearchRowKind.Word =>
                    first.Variants.Any(v => v.NameLabel == name.NameLabel
                        && v.OccupationNameConceptId == name.NameConceptId
                        && v.GroupConceptId == name.GroupConceptId),
                _ => false,
            };
            if (!selects)
                failures.Add(name.NameLabel);
        }

        failures.ShouldBeEmpty($"{failures.Count} of {_expected.Names.Count} mapped names, typed in full, were not in row 1");
    }

    [Fact]
    public async Task SearchAsync_EveryGroupLabelTypedInFull_IsRowOneAsThatGroupsLabelHit()
    {
        _expected.Groups.ShouldNotBeEmpty();
        var mapped = _expected.Names.Select(n => n.GroupConceptId).ToHashSet(StringComparer.Ordinal);
        _expected.Groups.Where(g => !mapped.Contains(g.ConceptId)).ShouldNotBeEmpty(
            "the groups with no mapped name are reachable only through their label, and v30 has some");
        var failures = new List<string>();

        foreach (var (conceptId, label) in _expected.Groups)
        {
            var result = await _sut.SearchAsync(label, Ct);
            var first = result.Rows.Count > 0 ? result.Rows[0] : null;
            if (first is not { Kind: OccupationSearchRowKind.Group, LabelHit: true } || first.GroupConceptId != conceptId)
                failures.Add(label);
        }

        failures.ShouldBeEmpty($"{failures.Count} of {_expected.Groups.Count} group labels, typed in full, were not row 1");
    }

    [Fact]
    public async Task SearchAsync_EveryHeadWordAlone_FindsEveryNameItHeads()
    {
        var failures = new List<string>();

        foreach (var head in _expected.Names
                     .GroupBy(n => HeadWord(n.NameLabel), StringComparer.Ordinal)
                     .Where(h => h.Key.Length >= OccupationSearchRule.MinQueryTokenLength))
        {
            var result = await _sut.SearchAsync(head.Key, Ct);
            var reached = result.Rows
                .SelectMany(r => r.MatchedNames.Select(m => m.NameLabel).Concat(r.Variants.Select(v => v.NameLabel)))
                .ToHashSet(StringComparer.Ordinal);
            if (!head.All(n => reached.Contains(n.NameLabel)))
                failures.Add(head.Key);
        }

        failures.ShouldBeEmpty($"{failures.Count} head words did not reach every name they head");
    }

    [Fact]
    public async Task SearchAsync_NameWhoseHeadIsBelowTheQueryMinimum_IsReachedByItsLongestWord()
    {
        // A one-character head (F&B Manager, 2:e Styrman) is not a query the rule runs; such a name
        // is found in full (the sweep above) and by its longest word, which this pins.
        var shortHeaded = _expected.Names
            .Where(n => HeadWord(n.NameLabel).Length < OccupationSearchRule.MinQueryTokenLength)
            .ToList();
        shortHeaded.ShouldNotBeEmpty();

        foreach (var name in shortHeaded)
        {
            var longest = Tokens(name.NameLabel).MaxBy(t => t.Length)!;
            var result = await _sut.SearchAsync(longest, Ct);
            result.Rows.ShouldContain(
                r => r.MatchedNames.Any(m => m.NameLabel == name.NameLabel)
                    || r.Variants.Any(v => v.NameLabel == name.NameLabel),
                $"{name.NameLabel} should be reached by '{longest}'");
        }
    }

    [Fact]
    public void MappedNameLabels_AreUniqueIgnoringCase_AndFitTheQueryBound()
    {
        _expected.Names.Select(n => n.NameLabel).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            .ShouldBe(_expected.Names.Count);
        _expected.Names.ShouldAllBe(n => n.NameLabel.Length <= Jobbliggaren.Application.JobAds.Queries
            .SearchOccupations.SearchOccupationsQuery.MaxQueryLength);
    }

    [Fact]
    public async Task SearchAsync_Projektledare_IsOneWordRowWithEveryVariantNamingItsGroup()
    {
        var result = await _sut.SearchAsync("projektledare", Ct);

        var row = result.Rows.ShouldHaveSingleItem();
        row.Kind.ShouldBe(OccupationSearchRowKind.Word);
        var expected = _expected.Names.Where(n => HeadWord(n.NameLabel) == "projektledare").ToList();
        row.Variants.Select(v => (v.NameLabel, v.GroupConceptId)).Order()
            .ShouldBe(expected.Select(n => (n.NameLabel, n.GroupConceptId)).Order());
        row.Variants.Select(v => v.GroupConceptId).Distinct().Count()
            .ShouldBe(expected.Select(n => n.GroupConceptId).Distinct().Count());
    }

    [Theory]
    [InlineData("programmerare", Mjukvaru)]
    [InlineData("mjukvaruutvecklare", Mjukvaru)]
    [InlineData("backend", Mjukvaru)]
    [InlineData("frontend", Mjukvaru)]
    [InlineData("fullstack", Mjukvaru)]
    [InlineData("lagerarbetare", LagerTerminal)]
    [InlineData("produktägare", OvrigaIt)]
    [InlineData("scrum", OvrigaIt)]
    [InlineData("UX", OvrigaIt)]
    public async Task SearchAsync_AcceptanceWord_FindsItsGroupRow(string word, string groupConceptId)
    {
        var result = await _sut.SearchAsync(word, Ct);

        result.Rows.ShouldContain(r => r.Kind == OccupationSearchRowKind.Group && r.GroupConceptId == groupConceptId);
    }

    [Fact]
    public async Task SearchAsync_Ux_NeverReachesVuxenutbildning()
    {
        var result = await _sut.SearchAsync("UX", Ct);

        result.Rows.ShouldNotContain(r => r.GroupConceptId == SkolChefer);
    }

    [Theory]
    [InlineData("sjuksköterska")]
    [InlineData("undersköterska")]
    public async Task SearchAsync_CompoundHead_ReachesEveryNameEndingInIt(string word)
    {
        var result = await _sut.SearchAsync(word, Ct);
        var reached = result.Rows
            .SelectMany(r => r.MatchedNames.Select(m => m.NameLabel).Concat(r.Variants.Select(v => v.NameLabel)))
            .ToHashSet(StringComparer.Ordinal);

        var compounds = _expected.Names
            .Where(n => Tokens(n.NameLabel).Any(t => t.EndsWith(word, StringComparison.Ordinal)))
            .Select(n => n.NameLabel)
            .ToList();
        compounds.ShouldNotBeEmpty();
        compounds.ShouldAllBe(n => reached.Contains(n));
    }

    [Fact]
    public async Task SearchAsync_Chauffor_HasNoOccupationNameInV30()
    {
        var result = await _sut.SearchAsync("chaufför", Ct);

        result.Rows.ShouldBeEmpty();
        result.TotalRows.ShouldBe(0);
    }

    [Fact]
    public async Task SearchAsync_TheFloorsCompoundHead_ReachesGrillkock()
    {
        // kock is exactly SuffixFloor letters: the compound-head arm admits it (FLOOR 5 would not).
        "kock".Length.ShouldBe(OccupationSearchRule.SuffixFloor);

        var result = await _sut.SearchAsync("kock", Ct);

        result.Rows.SelectMany(r => r.MatchedNames).Select(m => m.NameLabel).ShouldContain("Grillkock snabbmat");
    }

    /// <summary>
    /// ADR 0180's handler timing, measured not asserted (ADR 0045 Beslut 5/6: observe-only). The
    /// lines land in the test output; run it with <c>dotnet test --project
    /// tests/Jobbliggaren.Api.IntegrationTests -- --filter-class "*OccupationSearchSweepTests"
    /// --filter-method "*Timing*" --explicit only --report-xunit-trx --results-directory &lt;dir&gt;</c>
    /// and read them from the TRX file.
    /// </summary>
    [Fact(Explicit = true)]
    public async Task SearchAsync_Timing_ForTheWorstQueries()
    {
        // The costly shape: 33 copies of a token that hits, so every hitting label runs the AND to its end.
        var adversarial = string.Join(' ', Enumerable.Repeat("in", 33));
        await _sut.SearchAsync("warm", Ct);

        foreach (var query in new[] { "chef", "in", "utvecklare", adversarial })
        {
            var samples = new List<double>();
            OccupationSearchResult result = OccupationSearchResult.Empty;
            for (var i = 0; i < 200; i++)
            {
                var watch = Stopwatch.StartNew();
                result = await _sut.SearchAsync(query, Ct);
                samples.Add(watch.Elapsed.TotalMilliseconds);
            }
            samples.Sort();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(result, JsonSerializerOptions.Web).Length;
            result.Rows.Count.ShouldBeLessThanOrEqualTo(OccupationSearchRule.MaxRows);
            TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{(query.Length > 20 ? "adversarial(" + query.Length + ")" : query)}: p50 {samples[100]:F3} ms, " +
                $"p95 {samples[189]:F3} ms, rows {result.Rows.Count}/{result.TotalRows}, {bytes} B"));
        }
    }

    // The issue's head word: the label's first run of letters, digits and hyphens, lower-cased.
    private static string HeadWord(string label) =>
        SplitOn(label.ToLowerInvariant(), c => !(char.IsLetterOrDigit(c) || c == '-'))[0];

    private static string[] Tokens(string label) =>
        SplitOn(label.ToLowerInvariant(), c => !char.IsLetterOrDigit(c));

    // Splits on every character the predicate marks as a separator, dropping empty parts.
    private static string[] SplitOn(string text, Func<char, bool> isSeparator)
    {
        var parts = new List<string>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var inPart = i < text.Length && !isSeparator(text[i]);
            if (inPart && start < 0)
                start = i;
            else if (!inPart && start >= 0)
            {
                parts.Add(text[start..i]);
                start = -1;
            }
        }
        return [.. parts];
    }
}
