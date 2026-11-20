using System.Text.RegularExpressions;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.JobAds.JuniorIt;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// The shipped headline pattern (ADR 0186) means the same thing to .NET's <c>Regex</c> and to PostgreSQL's <c>~*</c>, title
/// by title, and the same under the database's own collation as under <c>"C"</c>, whose case folding stops at ASCII. The
/// pattern writes both cases of every non-ASCII letter so no locale decides a match; this is what pins that.
/// </summary>
/// <remarks>
/// The Postgres verdict is read through EF with the same <c>Regex.IsMatch(…, RegexOptions.IgnoreCase)</c> translation the
/// list's predicate uses: once through <c>JuniorItPredicate.Matches</c> itself, once with the title collated <c>"C"</c>.
/// Every title is imported into the field's group through <c>JobAd.Import</c>.
/// </remarks>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class JuniorItHeadlinePatternAgreementTests(SharedPostgresFixture postgres) : IAsyncLifetime
{
    private const string DataIt = "DJh5_yyF_hEM";

    private static readonly string Pattern = JuniorItListLoader.Load().HeadlinePattern;

    private static readonly (string Title, bool Junior)[] Titles =
    [
        ("Junior utvecklare", true),
        ("Juniorutvecklare", true),
        ("Utvecklare, junior", true),
        ("Junior", true),
        ("Seniorjunior", false),
        ("Senior utvecklare", false),
        ("Utvecklare", false),
        ("Traineeprogram IT", true),
        ("Graduate Engineer", true),
        ("GRADUATE PROGRAMME", true),
        ("Nyexaminerad systemutvecklare", true),
        ("Nyutexaminerad utvecklare", true),
        ("Nyexad utvecklare", true),
        ("Entry-level developer", true),
        ("Entry level developer", true),
        ("Early career", true),
        ("Praktikant", true),
        ("Internship", true),
        ("Intern", true),
        ("Intern på IT-avdelningen", true),
        ("Systemutvecklare (intern)", true),
        ("Internal tools engineer", false),
        ("International sales", false),
        ("Interna system", false),
        ("Internö", false),
        ("Åintern", false),
        ("FÖRSTA JOBBET som utvecklare", true),
        ("Första jobb", true),
        ("första jobbet", true),
        ("Första Jobbet", true),
        ("ÄRJUNIOR", false),
        ("Är junior", true),
        ("Åjunior", false),
        ("åjunior", false),
        ("Öjunior", false),
        ("öjunior", false),
        ("Éjunior", false),
        ("üjunior", false),
        ("Ä-junior", true),
    ];

    private readonly MutableFakeDateTimeProvider _clock = new() { UtcNow = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero) };
    private string _connectionString = string.Empty;
    private ServiceProvider _provider = default!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();
        _provider = new ServiceCollection()
            .AddDbContext<AppDbContext>(options => options
                .UseNpgsql(_connectionString,
                    npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                .UseSnakeCaseNamingConvention()
                .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning)))
            .BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await postgres.DropDatabaseAsync(_connectionString);
    }

    [Fact]
    public async Task ThePattern_ReadsEveryTitleAlike_InDotNet_InPostgres_AndInPostgresUnderTheCCollation()
    {
        var ids = new Dictionary<Guid, (string Title, bool Junior)>();
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var n = 0;
            foreach (var title in Titles)
            {
                var ad = Imported(title.Title, $"8{++n:D7}");
                db.JobAds.Add(ad);
                ids.Add(ad.Id.Value, title);
            }
            await db.SaveChangesAsync(Ct);
        }

        HashSet<Guid> postgresDefault;
        HashSet<Guid> postgresC;
        using (var scope = _provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pattern = Pattern;
            postgresDefault = [.. await db.JobAds.AsNoTracking()
                .Where(JuniorItPredicate.Matches(new JuniorItSet([], [DataIt], pattern)))
                .Select(j => j.Id.Value)
                .ToListAsync(Ct)];
            postgresC = [.. await db.JobAds.AsNoTracking()
                .Where(j => Regex.IsMatch(EF.Functions.Collate(j.Title, "C"), pattern, RegexOptions.IgnoreCase))
                .Select(j => j.Id.Value)
                .ToListAsync(Ct)];
        }

        var dotNet = new Regex(Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var disagreements = ids
            .Select(pair => (
                pair.Value.Title,
                pair.Value.Junior,
                DotNet: dotNet.IsMatch(pair.Value.Title),
                Postgres: postgresDefault.Contains(pair.Key),
                PostgresC: postgresC.Contains(pair.Key)))
            .Where(v => v.DotNet != v.Junior || v.Postgres != v.Junior || v.PostgresC != v.Junior)
            .Select(v => $"'{v.Title}': expected {v.Junior}, .NET {v.DotNet}, Postgres {v.Postgres}, Postgres under C {v.PostgresC}")
            .ToList();

        disagreements.ShouldBeEmpty();
        postgresDefault.Count.ShouldBe(Titles.Count(t => t.Junior), "non-vacuity: the read admitted every junior title");
    }

    private JobAd Imported(string title, string externalId) =>
        JobAd.Import(
            title: title,
            company: Company.Create("Exempelbolaget AB").Value,
            description: "Vi söker en utvecklare.",
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: $"{{\"id\":\"{externalId}\"}}",
            facets: TestFacets.From(occupationGroup: DataIt),
            declaredContacts: [],
            publishedAt: _clock.UtcNow.AddDays(-1),
            expiresAt: null,
            clock: _clock,
            applicationUrl: null, extractTerms: TestKeywordExtraction.None).Value;
}
