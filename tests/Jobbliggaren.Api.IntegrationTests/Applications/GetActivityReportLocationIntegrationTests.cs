using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Applications.Queries.GetActivityReport;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

// Application-typen krockar med Jobbliggaren.Application-namespacet; alias per fil
// (integrationsprojektet saknar global alias, jfr ReadHandlerManualPostingFallback).
using DomainApplication = Jobbliggaren.Domain.Applications.Application;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

// #316 — Location-resolvering för AF-aktivitetsrapporten. municipality_concept_id
// är en EF SHADOW-prop (STORED generated column härledd ur raw_payload->
// workplace_address->municipality_concept_id). EF InMemory beräknar inte
// generated columns → resolveringen kan BARA testas mot riktig Postgres
// (Testcontainers). Körs på handler-nivå (som ReadHandlerManualPostingFallback-
// IntegrationTests) eftersom POST /api/v1/applications inte sätter raw_payload;
// JobAd.Import gör det. Concept-id 1gEC_kvM_TXK ("Olofström") finns i den
// seedade taxonomi-snapshoten (taxonomy-snapshot.json, Blekinge län).
[Collection("Api")]
public class GetActivityReportLocationIntegrationTests
{
    private readonly ApiFactory _factory;
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public GetActivityReportLocationIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
        _currentUser.UserId.Returns(_userId);
    }

    // Känt seedat kommun-concept-id + dess svenska label (taxonomy-snapshot.json,
    // Blekinge län).
    private const string OlofstromConceptId = "1gEC_kvM_TXK";
    private const string OlofstromLabel = "Olofström";

    // Stämplings-/fönster-månad. Fast UTC-datum (provider-oberoende); rapporten
    // efterfrågas för exakt denna månad.
    private static readonly DateTimeOffset AppliedAt =
        new(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
    }

    private async Task<JobSeeker> SeedSeekerAsync(AppDbContext db, IDateTimeProvider clock)
    {
        var seeker = JobSeeker.Register(_userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        return seeker;
    }

    // Importerar en JobAd vars raw_payload bär municipality_concept_id på den
    // exakta JSON-pathen som STORED-kolumnen läser.
    private static JobAd SeedImportedJobAdWithMunicipality(
        AppDbContext db, IDateTimeProvider clock, string? municipalityConceptId)
    {
        var externalId = $"ext-{Guid.NewGuid():N}";
        var municipalityJson = municipalityConceptId is null
            ? "null"
            : $"\"{municipalityConceptId}\"";
        var rawPayload =
            $"{{\"id\":\"{externalId}\",\"workplace_address\":{{" +
            $"\"municipality_concept_id\":{municipalityJson}}}}}";

        var jobAd = JobAd.Import(
            title: "Backend-utvecklare",
            company: Company.Create("Klarna").Value,
            description: "En beskrivning",
            url: "https://example.com/jobb/1",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            publishedAt: new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            expiresAt: null,
            clock: clock).Value;
        db.JobAds.Add(jobAd);
        return jobAd;
    }

    private static DomainApplication SubmittedAt(
        JobSeekerId seekerId, JobAdId jobAdId, IDateTimeProvider clock)
    {
        var app = DomainApplication.Create(seekerId, jobAdId, null, null, clock).Value;
        app.TransitionTo(ApplicationStatus.Submitted, clock);
        return app;
    }

    private GetActivityReportQueryHandler CreateHandler(
        AppDbContext db, ITaxonomyReadModel taxonomy, IDateTimeProvider clock) =>
        new(db, _currentUser, taxonomy, clock);

    // ---------------------------------------------------------------
    // Resolvering — känt kommun-concept-id → svensk label
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_JobAdWithMunicipalityConceptId_ResolvesLocationToSwedishLabel()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var taxonomy = scope.ServiceProvider.GetRequiredService<ITaxonomyReadModel>();
        var clock = new FixedClock(AppliedAt);

        var seeker = await SeedSeekerAsync(db, clock);
        var jobAd = SeedImportedJobAdWithMunicipality(db, clock, OlofstromConceptId);
        db.Applications.Add(SubmittedAt(seeker.Id, jobAd.Id, clock));
        await db.SaveChangesAsync(CancellationToken.None);
        db.ChangeTracker.Clear();

        var handler = CreateHandler(db, taxonomy, clock);
        var result = await handler.Handle(
            new GetActivityReportQuery(AppliedAt.Year, AppliedAt.Month), CancellationToken.None);

        var item = result.Applications.ShouldHaveSingleItem();
        item.Location.ShouldBe(OlofstromLabel);
        item.Employer.ShouldBe("Klarna");
        item.Source.ShouldBe(JobSource.Platsbanken.Value);
    }

    // ---------------------------------------------------------------
    // Olösbart concept-id → fallback "Okänd kod (id)" droppas → Location null
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_JobAdWithUnresolvedMunicipalityConceptId_DropsFallbackAndYieldsNullLocation()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var taxonomy = scope.ServiceProvider.GetRequiredService<ITaxonomyReadModel>();
        var clock = new FixedClock(AppliedAt);

        var seeker = await SeedSeekerAsync(db, clock);
        // Syntetiskt concept-id som inte finns i snapshoten → ResolveLabelsAsync
        // ger "Okänd kod (...)"; handlern droppar fallbacken → Location == null.
        var jobAd = SeedImportedJobAdWithMunicipality(db, clock, "ZZZZ_zzz_ZZZ");
        db.Applications.Add(SubmittedAt(seeker.Id, jobAd.Id, clock));
        await db.SaveChangesAsync(CancellationToken.None);
        db.ChangeTracker.Clear();

        var handler = CreateHandler(db, taxonomy, clock);
        var result = await handler.Handle(
            new GetActivityReportQuery(AppliedAt.Year, AppliedAt.Month), CancellationToken.None);

        var item = result.Applications.ShouldHaveSingleItem();
        item.Location.ShouldBeNull("olösbart concept-id ska aldrig läcka som ort");
    }

    // ---------------------------------------------------------------
    // Ingen kommun i payloaden → Location null (shadow-prop NULL)
    // ---------------------------------------------------------------

    [Fact]
    public async Task Handle_JobAdWithoutMunicipality_YieldsNullLocation()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var taxonomy = scope.ServiceProvider.GetRequiredService<ITaxonomyReadModel>();
        var clock = new FixedClock(AppliedAt);

        var seeker = await SeedSeekerAsync(db, clock);
        var jobAd = SeedImportedJobAdWithMunicipality(db, clock, municipalityConceptId: null);
        db.Applications.Add(SubmittedAt(seeker.Id, jobAd.Id, clock));
        await db.SaveChangesAsync(CancellationToken.None);
        db.ChangeTracker.Clear();

        var handler = CreateHandler(db, taxonomy, clock);
        var result = await handler.Handle(
            new GetActivityReportQuery(AppliedAt.Year, AppliedAt.Month), CancellationToken.None);

        var item = result.Applications.ShouldHaveSingleItem();
        item.Location.ShouldBeNull();
    }
}
