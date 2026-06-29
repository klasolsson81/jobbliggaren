using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

// Application-typen krockar med Jobbliggaren.Application-namespacet; alias per fil
// (integrationsprojektet saknar global alias, jfr ManualPostingPersistenceTests).
using DomainApplication = Jobbliggaren.Domain.Applications.Application;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

// RÖD svit (TDD) mot Testcontainers Postgres (relationell provider).
// Spec: issue #315 / ADR 0086 — AdSnapshot owned VO (OwnsOne, IsRequired(false),
// snapshot_* kolumner). Speglar ManualPostingPersistenceTests.cs-mönstret (DI-
// scope, riktig Postgres). InMemory hedrar inte optional-owned-null-semantiken
// meningsfullt (architect-design §3b) → null-materialiseringen MÅSTE testas mot
// Npgsql.
[Collection("Api")]
public class AdSnapshotPersistenceTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private static readonly DateTimeOffset PublishedAt =
        new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAt =
        new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    // Fast capture-instant: den DI-resolverade IDateTimeProvider:n är en RIKTIG
    // systemklocka (varje .UtcNow ger ny tid) och Postgres timestamptz trunkerar
    // till mikrosekunder. Ett fast värde gör round-trip-assertionen deterministisk
    // (CapturedAt är ren VO-data, oberoende av aggregatets klocka).
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 4, 19, 12, 0, 0, TimeSpan.Zero);

    // Råt municipality concept-id (D4 final ruling: fryses som-det-är, resolveras
    // till namn på läs-vägen). Round-trippar via snapshot_municipality_concept_id-
    // kolumnen (varchar(64)).
    private const string MunicipalityConceptId = "1gEC_kvM_TXK";

    private static AdSnapshot FullSnapshot() =>
        AdSnapshot.Capture(
            title: "Backend-utvecklare",
            company: "Klarna",
            municipalityConceptId: MunicipalityConceptId,
            url: "https://example.com/jobb/1",
            source: JobSource.Platsbanken.Value,
            publishedAt: PublishedAt,
            expiresAt: ExpiresAt,
            description: "En lång beskrivning av tjänsten.",
            capturedAt: CapturedAt);

    private static async Task<JobSeekerId> SeedSeekerAsync(
        AppDbContext db, IDateTimeProvider clock, CancellationToken ct)
    {
        var seeker = JobSeeker.Register(Guid.NewGuid(), "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker.Id;
    }

    // ---------------------------------------------------------------
    // BLOCKING — optional owned-entity null-semantik (back-compat / IsRequired(false))
    // ---------------------------------------------------------------

    [Fact]
    public async Task Application_WithoutAdSnapshot_MaterializesAsNull()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seekerId = await SeedSeekerAsync(db, clock, ct);
        // TD-13 C3: cover_letter ("Bara brev") krypteras → värm ägar-DEK i samma
        // scope FÖRE Add. Reload-grenen läser tillbaka cover_letter ⇒ samma scope
        // måste ha varm DEK; warm:ad ovan räcker.
        await EncryptionKeyTestSeed.WarmAsync(scope, seekerId, ct);
        // Application.Create sätter aldrig ett snapshot (cover-letter-only).
        var app = DomainApplication.Create(seekerId, null, "Bara brev", null, clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var reloaded = await db.Applications
            .AsNoTracking()
            .FirstAsync(a => a.Id == app.Id, ct);

        // BLOCKING: alla snapshot_*-kolumner NULL → navigeringen materialiseras
        // som null, EJ en all-null AdSnapshot-instans (back-compat med pre-#315-rader).
        reloaded.AdSnapshot.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // Round-trip — alla 9 fält (inkl MunicipalityConceptId + Description) icke-null
    // ---------------------------------------------------------------

    [Fact]
    public async Task Application_WithAdSnapshot_RoundTripsAllFields()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seekerId = await SeedSeekerAsync(db, clock, ct);
        var jobAdId = new JobAdId(Guid.NewGuid());
        var app = DomainApplication.CreateFromJobAd(
            seekerId, jobAdId, FullSnapshot(), coverLetter: null, clock).Value;
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var reloaded = await db.Applications
            .AsNoTracking()
            .FirstAsync(a => a.Id == app.Id, ct);

        reloaded.AdSnapshot.ShouldNotBeNull();
        reloaded.AdSnapshot!.Title.ShouldBe("Backend-utvecklare");
        reloaded.AdSnapshot.Company.ShouldBe("Klarna");
        reloaded.AdSnapshot.MunicipalityConceptId.ShouldBe(MunicipalityConceptId);
        reloaded.AdSnapshot.Url.ShouldBe("https://example.com/jobb/1");
        reloaded.AdSnapshot.Source.ShouldBe(JobSource.Platsbanken.Value);
        reloaded.AdSnapshot.PublishedAt.ShouldBe(PublishedAt);
        reloaded.AdSnapshot.ExpiresAt.ShouldBe(ExpiresAt);
        reloaded.AdSnapshot.Description.ShouldBe("En lång beskrivning av tjänsten.");
        reloaded.AdSnapshot.CapturedAt.ShouldBe(CapturedAt);
    }

    // ---------------------------------------------------------------
    // Retention — terminal transition minimerar Description (persisterat null)
    // ---------------------------------------------------------------

    [Fact]
    public async Task AdSnapshot_MinimizedOnTerminalTransition_PersistsDescriptionNull()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seekerId = await SeedSeekerAsync(db, clock, ct);
        var jobAdId = new JobAdId(Guid.NewGuid());
        var app = DomainApplication.CreateFromJobAd(
            seekerId, jobAdId, FullSnapshot(), coverLetter: null, clock).Value;
        app.TransitionTo(ApplicationStatus.Submitted, clock);
        app.TransitionTo(ApplicationStatus.Rejected, clock); // terminal → minimering
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var reloaded = await db.Applications
            .AsNoTracking()
            .FirstAsync(a => a.Id == app.Id, ct);

        // Snapshot finns kvar (aldrig rensat), Description null, metadata behållen.
        reloaded.AdSnapshot.ShouldNotBeNull();
        reloaded.AdSnapshot!.Description.ShouldBeNull();
        reloaded.AdSnapshot.Title.ShouldBe("Backend-utvecklare");
        reloaded.AdSnapshot.Company.ShouldBe("Klarna");
        reloaded.AdSnapshot.MunicipalityConceptId.ShouldBe(MunicipalityConceptId);
        reloaded.AdSnapshot.PublishedAt.ShouldBe(PublishedAt);
        reloaded.AdSnapshot.ExpiresAt.ShouldBe(ExpiresAt);
        reloaded.AdSnapshot.CapturedAt.ShouldBe(CapturedAt);
    }
}
