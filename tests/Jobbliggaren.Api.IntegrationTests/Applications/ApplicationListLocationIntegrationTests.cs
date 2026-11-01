using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Applications.Attention;
using Jobbliggaren.Application.Applications.Queries;
using Jobbliggaren.Application.Applications.Queries.GetApplications;
using Jobbliggaren.Application.Applications.Queries.GetPipeline;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using DomainApplication = Jobbliggaren.Domain.Applications.Application;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

// #2248 — the application card's "Ort": both list reads resolve the ad's municipality concept-id
// through the taxonomy ACL to the same name the activity report shows. Real Postgres, because the
// concept-id is read from the imported ad's facets (GetActivityReportLocationIntegrationTests is the
// report-side twin). Concept-id 1gEC_kvM_TXK ("Olofström") is in the seeded taxonomy snapshot.
[Collection("Api")]
public class ApplicationListLocationIntegrationTests(ApiFactory factory)
{
    private const string OlofstromConceptId = "1gEC_kvM_TXK";
    private const string OlofstromLabel = "Olofström";

    private static readonly DateTimeOffset T0 = new(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly IOptions<ApplicationAttentionOptions> AttentionOptions =
        Options.Create(new ApplicationAttentionOptions());

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
    }

    private static ICurrentUser UserWith(Guid userId)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        return user;
    }

    private static JobAd ImportAd(string? municipalityConceptId, IDateTimeProvider clock)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";
        var municipalityJson = municipalityConceptId is null ? "null" : $"\"{municipalityConceptId}\"";
        var rawPayload =
            $"{{\"id\":\"{externalId}\",\"workplace_address\":{{" +
            $"\"municipality_concept_id\":{municipalityJson}}}}}";

        return JobAd.Import(
            title: "Testledare",
            company: Company.Create("Exempelbolaget AB").Value,
            description: "En beskrivning",
            url: $"https://example.com/jobb/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            expiresAt: null,
            clock: clock, declaredContacts: [], applicationUrl: null, extractTerms: TestKeywordExtraction.None).Value;
    }

    private sealed record Seed(
        Guid UserId, Guid ResolvedId, Guid UnresolvedId, Guid NoMunicipalityId, Guid ManualId, Guid ErasedId);

    private static async Task<Seed> SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var clock = new FixedClock(T0);
        var userId = Guid.NewGuid();
        var seeker = JobSeeker.Register(userId, TermsAcceptance.AcceptCurrent(clock), clock).Value;
        db.JobSeekers.Add(seeker);

        var resolvedAd = ImportAd(OlofstromConceptId, clock);
        var unresolvedAd = ImportAd("ZZZZ_zzz_ZZZ", clock);
        var noMunicipalityAd = ImportAd(municipalityConceptId: null, clock);
        var erasedAd = ImportAd(OlofstromConceptId, clock);
        db.JobAds.AddRange(resolvedAd, unresolvedAd, noMunicipalityAd, erasedAd);

        DomainApplication Submitted(JobAdId? jobAdId, ManualPosting? manual)
        {
            var app = DomainApplication.Create(seeker.Id, jobAdId, coverLetter: null, manualPosting: manual, clock).Value;
            app.TransitionTo(ApplicationStatus.Submitted, clock).IsSuccess.ShouldBeTrue();
            db.Applications.Add(app);
            return app;
        }

        var resolved = Submitted(resolvedAd.Id, null);
        var unresolved = Submitted(unresolvedAd.Id, null);
        var noMunicipality = Submitted(noMunicipalityAd.Id, null);
        var manual = Submitted(null, ManualPosting.Create(
            "Manuell titel", "Manuellt företag", "https://example.com/manuell", expiresAt: null).Value);
        var erased = Submitted(erasedAd.Id, null);
        await db.SaveChangesAsync(ct);

        // Through the production method, never a column write (#843): the facets survive Erase().
        erasedAd.Erase(clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        return new Seed(
            userId, resolved.Id.Value, unresolved.Id.Value, noMunicipality.Id.Value,
            manual.Id.Value, erased.Id.Value);
    }

    private static void ShouldCarryTheAdsOrt(IReadOnlyList<ApplicationDto> items, Seed seed)
    {
        items.Count.ShouldBe(5);
        items.Single(i => i.Id == seed.ResolvedId).Location.ShouldBe(OlofstromLabel);
        items.Single(i => i.Id == seed.UnresolvedId).Location
            .ShouldBeNull("an unresolved concept-id must never surface as an Ort");
        items.Single(i => i.Id == seed.NoMunicipalityId).Location.ShouldBeNull();
        items.Single(i => i.Id == seed.ManualId).Location.ShouldBeNull("a manual application has no ad row");
        items.Single(i => i.Id == seed.ErasedId).Location
            .ShouldBe(OlofstromLabel, "the municipality facet survives Erase(), as in the activity report");
    }

    [Fact]
    public async Task GetPipeline_ResolvesEachApplicationsOrtFromItsAd()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(db, ct);

        var handler = new GetPipelineQueryHandler(
            db, UserWith(seed.UserId), new FixedClock(T0), AttentionOptions,
            scope.ServiceProvider.GetRequiredService<ITaxonomyReadModel>());
        var groups = await handler.Handle(new GetPipelineQuery(), ct);

        ShouldCarryTheAdsOrt([.. groups.SelectMany(g => g.Applications)], seed);
    }

    [Fact]
    public async Task GetApplications_ResolvesEachApplicationsOrtFromItsAd()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(db, ct);

        var handler = new GetApplicationsQueryHandler(
            db, UserWith(seed.UserId), new FixedClock(T0), AttentionOptions,
            scope.ServiceProvider.GetRequiredService<ITaxonomyReadModel>());
        var page = await handler.Handle(new GetApplicationsQuery(), ct);

        ShouldCarryTheAdsOrt(page.Items, seed);
    }
}
