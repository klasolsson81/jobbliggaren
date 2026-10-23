using System.Data.Common;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Api.IntegrationTests.Sessions;
using Jobbliggaren.Application.JobAds;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetPublicJobAd;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.JobAds;

/// <summary>
/// ADR 0160 — the public read classifies twice: once on the publication facts, and again on the row
/// that carries the content. The two reads share no transaction, so an ad erased or archived between
/// them must still get the 410, never a 200 built from the changed row. Here the change is made by the
/// aggregate's own <c>Erase()</c>/<c>Archive()</c>, committed on a second connection right after the
/// first read, which is where a concurrent erasure request lands in production.
/// </summary>
[Collection(SharedPostgresFixtureGroup.Name)]
public sealed class PublicJobAdReadRaceTests(SharedPostgresFixture postgres) : IAsyncLifetime
{
    private static readonly MutableFakeDateTimeProvider Clock = new() { UtcNow = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero) };

    private string _connectionString = string.Empty;

    /// <summary>
    /// The taxonomy port, kept to the adapter's contract (one row per asked id, in input order, a null label
    /// for an id the snapshot does not know). The ad carries a kommun and a län id, so a Listed answer asks
    /// the port once; what these tests pin is that the second classification gates the port as well as the
    /// body (ADR 0171): an ad that ended between the reads never reaches it.
    /// </summary>
    private readonly ITaxonomyReadModel _taxonomy = Substitute.For<ITaxonomyReadModel>();

    private static readonly string[] RaceTaxonomyIds = ["mun_race", "reg_race", "grp_race"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();
        // CA2012: NSubstitute's stub of a ValueTask-returning port member is the library's idiom (same waiver as the unit tests).
#pragma warning disable CA2012
        _taxonomy.ResolveLabelsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => new ValueTask<IReadOnlyList<TaxonomyLabelDto>>(
                call.Arg<IReadOnlyList<string>>().Select(id => new TaxonomyLabelDto(id, Label: null)).ToList()));
#pragma warning restore CA2012
    }

    public async ValueTask DisposeAsync() => await postgres.DropDatabaseAsync(_connectionString);

    private AppDbContext NewContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
            .AddInterceptors(interceptors)
            .Options);

    private async Task<Guid> ImportListedAsync()
    {
        await using var db = NewContext();
        var externalId = TestIds.ExternalId();
        var payload = $"{{\"id\":\"{externalId}\"}}";
        var jobAd = JobAd.Import(
            title: "Backend-utvecklare",
            company: Company.Create("Exempelbolaget AB").Value,
            description: "Vi söker en backend-utvecklare.",
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload,
            facets: TestFacets.From(occupationGroup: "grp_race", municipality: "mun_race", region: "reg_race"),
            declaredContacts: [],
            publishedAt: Clock.UtcNow.AddDays(-2),
            expiresAt: Clock.UtcNow.AddDays(20),
            clock: Clock,
            applicationUrl: null, extractTerms: TestKeywordExtraction.None).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(Ct);
        return jobAd.Id.Value;
    }

    private async Task<Result<PublicJobAdDto>> ReadWithChangeAfterFirstReadAsync(
        Guid id, Func<JobAd, Result> change)
    {
        var interleave = new AfterFirstRead(async () =>
        {
            await using var other = NewContext();
            var ad = await other.JobAds.SingleAsync(j => j.Id == new JobAdId(id), Ct);
            change(ad).IsSuccess.ShouldBeTrue();
            await other.SaveChangesAsync(Ct);
        });

        await using var db = NewContext(interleave);
        var result = await new GetPublicJobAdQueryHandler(db, _taxonomy, Clock).Handle(new GetPublicJobAdQuery(id), Ct);
        interleave.Reads.ShouldBe(2, "the change must land between the facts read and the content read");
        return result;
    }

    [Fact]
    public async Task An_ad_unchanged_between_the_two_reads_is_listed_and_asks_the_port_once()
    {
        // The control case: the fixture's ids do reach the port on the Listed path, so the two
        // DidNotReceive pins below cannot hold vacuously.
        var id = await ImportListedAsync();

        var result = await ReadWithChangeAfterFirstReadAsync(id, _ => Result.Success());

        result.IsSuccess.ShouldBeTrue();
        result.Value.MunicipalityConceptId.ShouldBe("mun_race");
        result.Value.MunicipalityName.ShouldBeNull("the stub knows no id, as the real adapter answers an unknown one");
        await _taxonomy.Received(1).ResolveLabelsAsync(
            Arg.Is<IReadOnlyList<string>>(ids => ids.SequenceEqual(RaceTaxonomyIds)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_ad_erased_between_the_two_reads_answers_410()
    {
        var id = await ImportListedAsync();

        var result = await ReadWithChangeAfterFirstReadAsync(id, ad => ad.Erase(Clock));

        result.IsFailure.ShouldBeTrue("a 200 here would serve the erased tombstone's '[raderad]'.");
        result.Error.ShouldBe(JobAdErrors.Gone);
        await _taxonomy.DidNotReceive().ResolveLabelsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_ad_archived_between_the_two_reads_answers_410()
    {
        var id = await ImportListedAsync();

        var result = await ReadWithChangeAfterFirstReadAsync(id, ad => ad.Archive(Clock));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(JobAdErrors.Gone);
        await _taxonomy.DidNotReceive().ResolveLabelsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Runs <paramref name="action"/> once, after the first reader of the context has executed.</summary>
    private sealed class AfterFirstRead(Func<Task> action) : DbCommandInterceptor
    {
        public int Reads { get; private set; }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (++Reads == 1)
                await action();
            return result;
        }
    }
}
