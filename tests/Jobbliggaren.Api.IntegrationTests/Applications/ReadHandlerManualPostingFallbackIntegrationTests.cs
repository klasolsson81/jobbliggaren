using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Applications.Attention;
using Jobbliggaren.Application.Applications.Queries.GetApplicationById;
using Jobbliggaren.Application.Applications.Queries.GetApplications;
using Jobbliggaren.Application.Applications.Queries.GetPipeline;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

// Alias matchar Application.UnitTests GlobalUsings.cs (Application-typen
// krockar med Jobbliggaren.Application-namespacet); integrationsprojektet har
// ingen global alias, så den deklareras per fil.
using DomainApplication = Jobbliggaren.Domain.Applications.Application;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

// Flyttad från Jobbliggaren.Application.UnitTests (EF InMemory) till Npgsql/
// Testcontainers per senior-cto-advisor rev2 (B), docs/reviews/
// 2026-05-17-fas3-stopp3a-divergence-cto-2.md §3.1/§4.3. Detta är kärnan
// i divergensen: 3-grens JobAdSummaryDto-projektion via cross-aggregat
// LEFT JOIN med value-converter på nullable-struct-FK (ADR 0048). EF
// InMemory är ej relationell → kan inte översätta converter + join (tre
// in-block-fixar falsifierade). Hela projektionen körs nu via Npgsql =
// relationell, översätter converter + LEFT JOIN korrekt.
//
// Scenarier + assertions bevarade 1:1 (gren 1 JobAd-kopplad, gren 2
// ManualPosting Source="Manual"/PublishedAt=null [J1], gren 3 null,
// cross-user ADR 0031, soft-deletad JobAd → fallback). Testnamn bevarade
// för spårbar täckning (ADR 0044). Mönster (scope/AppDbContext/clock,
// soft-delete via db.Entry(...).Property(nameof(JobAd.DeletedAt))) kopierat
// verbatim från ManualPostingPersistenceTests.cs (redan grön mot Npgsql).
[Collection("Api")]
public class ReadHandlerManualPostingFallbackIntegrationTests
{
    private readonly ApiFactory _factory;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    private static readonly IOptions<ApplicationAttentionOptions> AttentionOptions =
        Options.Create(new ApplicationAttentionOptions());

    public ReadHandlerManualPostingFallbackIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
        _currentUser.UserId.Returns(_userId);
    }

    // Fast past-datum — provider-oberoende (JobAd.ValidateCore begränsar
    // endast expiresAt > publishedAt; ingen framtids-/dåtidskontroll på
    // publishedAt). Bevarar unit-sviten s assertion 1:1.
    private static readonly DateTimeOffset JobAdPublishedAt =
        new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

    private static async Task<JobSeeker> SeedSeekerAsync(
        AppDbContext db, IDateTimeProvider clock, Guid userId)
    {
        var seeker = JobSeeker.Register(userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        return seeker;
    }

    private static JobAd SeedJobAd(AppDbContext db, IDateTimeProvider clock)
    {
        var jobAd = JobAd.Create(
            "Backend-utvecklare",
            Company.Create("Klarna").Value,
            "En beskrivning",
            "https://example.com/jobb/1",
            JobSource.Platsbanken,
            JobAdPublishedAt,
            null,
            clock).Value;
        db.JobAds.Add(jobAd);
        return jobAd;
    }

    private static ManualPosting ManualVo() =>
        ManualPosting.Create(
            "Manuell titel", "Manuellt företag", "https://example.com/manuell",
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)).Value;

    // ---------------------------------------------------------------
    // GetApplications — gren 1 (JobAd-kopplad)
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetApplications_WithJobAdLinked_ProjectsJobAdSummaryFromJobAd()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);
        var jobAd = SeedJobAd(db, clock);
        var app = DomainApplication.Create(seeker.Id, jobAd.Id, null, null, clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetApplicationsQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetApplicationsQuery(), CancellationToken.None);

        var dto = result.Items.ShouldHaveSingleItem();
        dto.JobAd.ShouldNotBeNull();
        dto.JobAd!.JobAdId.ShouldBe(jobAd.Id.Value);
        dto.JobAd.Title.ShouldBe("Backend-utvecklare");
        dto.JobAd.Company.ShouldBe("Klarna");
        dto.JobAd.Url.ShouldBe("https://example.com/jobb/1");
        dto.JobAd.Source.ShouldBe(JobSource.Platsbanken.Value);
        dto.JobAd.PublishedAt.ShouldBe(JobAdPublishedAt);
        // #805-3: en nyskapad JobAd är Active → FE visar "Visa annonsen"-utlänken.
        dto.JobAd.Status.ShouldBe(JobAdStatus.Active.Value);
    }

    // ---------------------------------------------------------------
    // GetApplications — gren 2 (manuell, ManualPosting != null)
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetApplications_WithManualPosting_ProjectsManualSourceAndNullPublishedAt()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);
        var app = DomainApplication.Create(seeker.Id, null, null, ManualVo(), clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetApplicationsQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetApplicationsQuery(), CancellationToken.None);

        var dto = result.Items.ShouldHaveSingleItem();
        dto.JobAd.ShouldNotBeNull();
        dto.JobAd!.JobAdId.ShouldBeNull();
        dto.JobAd.Title.ShouldBe("Manuell titel");
        dto.JobAd.Company.ShouldBe("Manuellt företag");
        dto.JobAd.Url.ShouldBe("https://example.com/manuell");
        dto.JobAd.Source.ShouldBe("Manual");
        dto.JobAd.PublishedAt.ShouldBeNull(); // J1 — CreatedAt projiceras ALDRIG som PublishedAt
        dto.JobAd.ExpiresAt.ShouldBe(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
        dto.JobAd.Status.ShouldBeNull(); // #805-3 — ingen annonsrad ⇒ ingen livs-utsaga
    }

    // ---------------------------------------------------------------
    // GetApplications — gren 3 (varken eller)
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetApplications_WithNeitherJobAdNorManual_ProjectsNullJobAd()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);
        // TD-13 C3: cover_letter ("Bara brev") krypteras → värm ägar-DEK i
        // samma scope FÖRE Add (direkt-seed förbi Mediator-prefetch). Handlern
        // anropas i samma scope och läser tillbaka cover_letter ⇒ varm DEK
        // täcker både write- och läs-vägen här.
        await EncryptionKeyTestSeed.WarmAsync(scope, seeker.Id, CancellationToken.None);
        var app = DomainApplication.Create(seeker.Id, null, "Bara brev", null, clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetApplicationsQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetApplicationsQuery(), CancellationToken.None);

        var dto = result.Items.ShouldHaveSingleItem();
        dto.JobAd.ShouldBeNull();
    }

    [Fact]
    public async Task GetApplications_DoesNotLeakOtherUsersApplications()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);
        db.Applications.Add(
            DomainApplication.Create(seeker.Id, null, null, ManualVo(), clock).Value);

        var otherUserId = Guid.NewGuid();
        var otherSeeker = await SeedSeekerAsync(db, clock, otherUserId);
        db.Applications.Add(
            DomainApplication.Create(otherSeeker.Id, null, null, ManualVo(), clock).Value);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetApplicationsQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetApplicationsQuery(), CancellationToken.None);

        result.Items.Count.ShouldBe(1);
    }

    // ---------------------------------------------------------------
    // GetApplicationById — 3-grens-projektion + FollowUps/Notes bevaras
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetApplicationById_WithJobAdLinked_ProjectsJobAdSummaryFromJobAd()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);
        var jobAd = SeedJobAd(db, clock);
        var app = DomainApplication.Create(seeker.Id, jobAd.Id, null, null, clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetApplicationByIdQueryHandler(
            db, _currentUser, Substitute.For<IFailedAccessLogger>(), Substitute.For<ITaxonomyReadModel>());
        var result = await handler.Handle(
            new GetApplicationByIdQuery(app.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.JobAd.ShouldNotBeNull();
        result.JobAd!.JobAdId.ShouldBe(jobAd.Id.Value);
        result.JobAd.Source.ShouldBe(JobSource.Platsbanken.Value);
        result.JobAd.PublishedAt.ShouldBe(JobAdPublishedAt);
        result.JobAd.Status.ShouldBe(JobAdStatus.Active.Value);
    }

    // ---------------------------------------------------------------
    // #805-3 — den ARKIVERADE annonsen. Detta är kärntestet: det pinnar att
    // arkivering INTE ger jobAd == null (den premiss hela läsvägen tidigare
    // vilade på), utan en fullt projicerad summary med Status = "Archived".
    //
    // Före #805-3 kodades "annonsen är borta" som jobAd == null, delegerat till
    // soft-delete-axeln JobAd.DeletedAt — som saknar writer (#821). Följd:
    // PreservedAdPanel (ADR 0086/#315) renderades ALDRIG i produktion, och
    // produktens enda "Visa annonsen"-utlänk bodde inuti den. Testet gör den
    // buggen omöjlig att återinföra tyst: skulle någon åter-koppla borta-läget
    // till null-heten faller ShouldNotBeNull().
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetApplicationById_WithArchivedJobAd_KeepsJobAdSummaryAndProjectsArchivedStatus()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);
        var jobAd = SeedJobAd(db, clock);
        var app = DomainApplication.Create(seeker.Id, jobAd.Id, null, null, clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        // Arkivera via domänmetoden — exakt den väg RetainPlatsbankenJobAdsJob
        // (snapshot-miss) och ExpireJobAdsJob (utgången) tar i produktion.
        jobAd.Archive(clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetApplicationByIdQueryHandler(
            db, _currentUser, Substitute.For<IFailedAccessLogger>(), Substitute.For<ITaxonomyReadModel>());
        var result = await handler.Handle(
            new GetApplicationByIdQuery(app.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        // Annonsraden joinar FORTFARANDE — arkivering är inte radering.
        result!.JobAd.ShouldNotBeNull();
        result.JobAd!.JobAdId.ShouldBe(jobAd.Id.Value);
        result.JobAd.Title.ShouldBe("Backend-utvecklare");
        // …och bär nu den sanningsenliga borta-signalen. FE:t döljer utlänken
        // och visar den bevarade kopian på exakt detta värde.
        result.JobAd.Status.ShouldBe(JobAdStatus.Archived.Value);
    }

    [Fact]
    public async Task GetApplications_WithArchivedJobAd_KeepsJobAdSummaryAndProjectsArchivedStatus()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);
        var jobAd = SeedJobAd(db, clock);
        var app = DomainApplication.Create(seeker.Id, jobAd.Id, null, null, clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        jobAd.Archive(clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetApplicationsQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetApplicationsQuery(), CancellationToken.None);

        var dto = result.Items.ShouldHaveSingleItem();
        dto.JobAd.ShouldNotBeNull();
        dto.JobAd!.Status.ShouldBe(JobAdStatus.Archived.Value);
    }

    [Fact]
    public async Task GetApplicationById_WithManualPosting_ProjectsManualSourceNullPublishedAt()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);
        var app = DomainApplication.Create(seeker.Id, null, null, ManualVo(), clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetApplicationByIdQueryHandler(
            db, _currentUser, Substitute.For<IFailedAccessLogger>(), Substitute.For<ITaxonomyReadModel>());
        var result = await handler.Handle(
            new GetApplicationByIdQuery(app.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.JobAd.ShouldNotBeNull();
        result.JobAd!.JobAdId.ShouldBeNull();
        result.JobAd.Source.ShouldBe("Manual");
        result.JobAd.PublishedAt.ShouldBeNull();
        result.JobAd.Title.ShouldBe("Manuell titel");
        // #805-3: ingen JobAd-rad ⇒ ingen arkivering ⇒ ingen livs-utsaga.
        // Status är null, ALDRIG defaultad till "Active" (det vore en lögn i
        // payloaden). FE:t visar länken utan att hävda vare sig live eller borta.
        result.JobAd.Status.ShouldBeNull();
    }

    [Fact]
    public async Task GetApplicationById_WithNeither_ProjectsNullJobAdButKeepsDetail()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);
        // TD-13 C3: cover_letter ("Bara brev") krypteras → värm ägar-DEK i
        // samma scope FÖRE Add (direkt-seed förbi Mediator-prefetch). Handlern
        // anropas i samma scope och läser tillbaka cover_letter ⇒ varm DEK
        // täcker både write- och läs-vägen här.
        await EncryptionKeyTestSeed.WarmAsync(scope, seeker.Id, CancellationToken.None);
        var app = DomainApplication.Create(seeker.Id, null, "Bara brev", null, clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetApplicationByIdQueryHandler(
            db, _currentUser, Substitute.For<IFailedAccessLogger>(), Substitute.For<ITaxonomyReadModel>());
        var result = await handler.Handle(
            new GetApplicationByIdQuery(app.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result!.JobAd.ShouldBeNull();
        result.CoverLetter.ShouldBe("Bara brev");
    }

    [Fact]
    public async Task GetApplicationById_CrossUserAccess_ReturnsNullAndLogsAttempt()
    {
        // ADR 0031/TD-67 — failed-access-logg bevaras oförändrad efter join-tillägg.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var otherSeeker = await SeedSeekerAsync(db, clock, Guid.NewGuid());
        var app = DomainApplication.Create(otherSeeker.Id, null, null, ManualVo(), clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);
        await SeedSeekerAsync(db, clock, _userId);

        var logger = Substitute.For<IFailedAccessLogger>();
        var handler = new GetApplicationByIdQueryHandler(db, _currentUser, logger, Substitute.For<ITaxonomyReadModel>());

        var result = await handler.Handle(
            new GetApplicationByIdQuery(app.Id.Value), CancellationToken.None);

        result.ShouldBeNull();
        logger.Received(1).LogCrossUserAttempt(
            "Application", app.Id.Value, _userId, "GetApplicationById");
    }

    // ---------------------------------------------------------------
    // GetPipeline — 3-grens-projektion (join FÖRE materialisering)
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetPipeline_WithManualPosting_ProjectsManualSourceNullPublishedAt()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);
        var app = DomainApplication.Create(seeker.Id, null, null, ManualVo(), clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        var group = result.ShouldHaveSingleItem();
        var dto = group.Applications.ShouldHaveSingleItem();
        dto.JobAd.ShouldNotBeNull();
        dto.JobAd!.JobAdId.ShouldBeNull();
        dto.JobAd.Source.ShouldBe("Manual");
        dto.JobAd.PublishedAt.ShouldBeNull();
        dto.JobAd.Status.ShouldBeNull(); // #805-3 — ingen annonsrad ⇒ ingen livs-utsaga
    }

    [Fact]
    public async Task GetPipeline_WithJobAdLinked_ProjectsJobAdSummaryFromJobAd()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);
        var jobAd = SeedJobAd(db, clock);
        var app = DomainApplication.Create(seeker.Id, jobAd.Id, null, null, clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        var group = result.ShouldHaveSingleItem();
        var dto = group.Applications.ShouldHaveSingleItem();
        dto.JobAd.ShouldNotBeNull();
        dto.JobAd!.JobAdId.ShouldBe(jobAd.Id.Value);
        dto.JobAd.PublishedAt.ShouldBe(JobAdPublishedAt);
        dto.JobAd.Status.ShouldBe(JobAdStatus.Active.Value); // #805-3
    }

    // #805-3: GetPipeline bygger SAMMA vidgade DTO som GetApplications/
    // GetApplicationById och är därför lika åter-brytbar — utan detta test kunde
    // någon åter-koppla borta-läget till null-heten HÄR (t.ex. ett
    // Status == Active-predikat i joinen) och lämna sviten grön. Kanban-vyn läser
    // inte Status ännu, men DTO-kontraktet är delat: en tappad Status-projektion
    // är en tyst regression i det ögonblick ytan börjar läsa den.
    [Fact]
    public async Task GetPipeline_WithArchivedJobAd_KeepsJobAdSummaryAndProjectsArchivedStatus()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);
        var jobAd = SeedJobAd(db, clock);
        var app = DomainApplication.Create(seeker.Id, jobAd.Id, null, null, clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        jobAd.Archive(clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        var group = result.ShouldHaveSingleItem();
        var dto = group.Applications.ShouldHaveSingleItem();
        // Arkivering är inte radering — raden joinar kvar…
        dto.JobAd.ShouldNotBeNull();
        dto.JobAd!.JobAdId.ShouldBe(jobAd.Id.Value);
        // …och bär den sanningsenliga borta-signalen.
        dto.JobAd.Status.ShouldBe(JobAdStatus.Archived.Value);
    }

    [Fact]
    public async Task GetPipeline_WithNeither_ProjectsNullJobAd()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);
        // TD-13 C3: cover_letter ("Bara brev") krypteras → värm ägar-DEK i
        // samma scope FÖRE Add (direkt-seed förbi Mediator-prefetch). Handlern
        // anropas i samma scope och läser tillbaka cover_letter ⇒ varm DEK
        // täcker både write- och läs-vägen här.
        await EncryptionKeyTestSeed.WarmAsync(scope, seeker.Id, CancellationToken.None);
        var app = DomainApplication.Create(seeker.Id, null, "Bara brev", null, clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        var group = result.ShouldHaveSingleItem();
        group.Applications.ShouldHaveSingleItem().JobAd.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // ADR 0048 c — MEKANISM-test: OM en JobAd bär DeletedAt faller den ut via
    // query-filter + DefaultIfEmpty (jobAd = null), UTAN IgnoreQueryFilters/eget
    // predikat. Testet pinnar filtret, inte ett produktions-scenario.
    //
    // #805-3 sanningssynk — läs detta innan du drar slutsatser av testet: INGEN
    // produktionsväg sätter JobAd.DeletedAt. Domänen saknar SoftDelete-metod och
    // src/ har noll writers (#821 retirerar axeln). Testet fabricerar tillståndet
    // via db.Entry(...) — det är därför det passerar, och det är precis den
    // falska tryggheten som lät läsvägen koda "annonsen är borta" som
    // jobAd == null i två releaser. Den verkliga borta-signalen är Status
    // ("Archived"), pinnad av GetApplicationById/GetApplications_WithArchivedJobAd_*
    // ovan. Behåll gärna detta test (filtret ÄR korrekt implementerat), men
    // härled aldrig produktionsbeteende ur det.
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetApplications_WithSoftDeletedJobAd_FallsBackToNullViaQueryFilter()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);
        var jobAd = SeedJobAd(db, clock);
        var app = DomainApplication.Create(seeker.Id, jobAd.Id, null, null, clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        db.Entry(jobAd).Property(nameof(JobAd.DeletedAt)).CurrentValue = clock.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);
        db.ChangeTracker.Clear();

        var handler = new GetApplicationsQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetApplicationsQuery(), CancellationToken.None);

        // Application själv finns kvar (ej soft-deletad), men JobAd-grenen
        // faller ut via JobAd:s query-filter + DefaultIfEmpty → JobAd == null.
        var dto = result.Items.ShouldHaveSingleItem();
        dto.JobAd.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // #343 (ADR 0085 §3, CTO Option a) — the draft-deadline attention signal (5)
    // reads JobAd?.ExpiresAt, which the handler populates from ManualPosting.ExpiresAt
    // when there is no linked JobAd. Pins that the signal is NOT silently lost on the
    // manual-posting fallback branch (architect's flagged case).
    // ---------------------------------------------------------------

    [Fact]
    public async Task GetPipeline_ManualPostingDraftClosingSoon_StampsDraftDeadlineApproaching()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seeker = await SeedSeekerAsync(db, clock, _userId);
        // Draft (Create defaults to Draft) with a manual posting closing in 3 days —
        // inside the default DraftDeadlineDays = 5 window. The signal must fire from the
        // ManualPosting.ExpiresAt fallback, not just from a linked JobAd.
        var manualClosingSoon = ManualPosting.Create(
            "Manuell titel", "Manuellt företag", "https://example.com/manuell",
            clock.UtcNow.AddDays(3)).Value;
        var app = DomainApplication.Create(seeker.Id, null, null, manualClosingSoon, clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(CancellationToken.None);

        var handler = new GetPipelineQueryHandler(db, _currentUser, clock, AttentionOptions);
        var result = await handler.Handle(new GetPipelineQuery(), CancellationToken.None);

        var dto = result.First(g => g.Status == "Draft").Applications.Single();
        dto.AttentionSignal.ShouldBe(ApplicationAttentionSignal.DraftDeadlineApproaching);
    }
}
