using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Applications.Commands.CreateApplicationFromJobAd;
using Jobbliggaren.Application.Applications.Queries.GetApplicationById;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

// Spec: issue #315 / ADR 0086 (D4 FINAL RULING: concept-id-at-read). The capture
// (write) handler freezes the JobAd's RAW MunicipalityConceptId; the read handler
// (GetApplicationByIdQueryHandler) resolves it to a human name via the taxonomy
// ACL. This test exercises the full journey: capture stores the raw concept-id,
// then the read resolves it to "Olofström".
//
// municipality_concept_id är en EF SHADOW-prop (STORED generated column härledd
// ur raw_payload->workplace_address->municipality_concept_id) → InMemory beräknar
// den inte. Capture + read-resolvering kan därför BARA testas mot riktig Postgres.
// Mönstret (importerad JobAd med raw_payload + seedad taxonomi-snapshot) speglar
// GetActivityReportLocationIntegrationTests.cs. Concept-id 1gEC_kvM_TXK
// ("Olofström", Blekinge län) finns i den seedade taxonomy-snapshot.json.
[Collection("Api")]
public class AdSnapshotCaptureLocationIntegrationTests
{
    private readonly ApiFactory _factory;
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _userId = Guid.NewGuid();

    public AdSnapshotCaptureLocationIntegrationTests(ApiFactory factory)
    {
        _factory = factory;
        _currentUser.UserId.Returns(_userId);
    }

    // Känt seedat kommun-concept-id + dess svenska label (taxonomy-snapshot.json,
    // Blekinge län — identiskt med GetActivityReportLocationIntegrationTests).
    private const string OlofstromConceptId = "1gEC_kvM_TXK";
    private const string OlofstromLabel = "Olofström";

    private static readonly DateTimeOffset PublishedAt =
        new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow => now;
    }

    private async Task<JobSeekerId> SeedSeekerAsync(
        IServiceScope scope, AppDbContext db, IDateTimeProvider clock)
    {
        var seeker = JobSeeker.Register(_userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(CancellationToken.None);
        // Read-handlern materialiserar aggregatet (krypterad cover_letter-väg) →
        // värm ägar-DEK i samma scope (TD-13 C3), som de andra read-integ-testerna.
        await EncryptionKeyTestSeed.WarmAsync(scope, seeker.Id, CancellationToken.None);
        return seeker.Id;
    }

    // Importerar en JobAd vars raw_payload bär municipality_concept_id på den
    // exakta JSON-pathen som STORED-kolumnen läser (= #316-helpern).
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
            description: "En beskrivning av tjänsten.",
            url: "https://example.com/jobb/1",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: rawPayload,
            facets: TestFacets.FromPayload(rawPayload),
            publishedAt: PublishedAt,
            expiresAt: null,
            clock: clock).Value;
        db.JobAds.Add(jobAd);
        return jobAd;
    }

    private CreateApplicationFromJobAdCommandHandler CreateCaptureHandler(
        AppDbContext db, IDateTimeProvider clock) =>
        new(db, _currentUser, clock);

    private GetApplicationByIdQueryHandler CreateReadHandler(
        AppDbContext db, ITaxonomyReadModel taxonomy) =>
        new(db, _currentUser, Substitute.For<IFailedAccessLogger>(), taxonomy);

    // ---------------------------------------------------------------
    // Write fryser RÅ concept-id; read resolverar till svensk label
    // ---------------------------------------------------------------

    [Fact]
    public async Task CaptureFreezesRawConceptId_ThenReadResolvesOrtName()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var taxonomy = scope.ServiceProvider.GetRequiredService<ITaxonomyReadModel>();
        var clock = new FixedClock(PublishedAt.AddDays(9));

        await SeedSeekerAsync(scope, db, clock);
        var jobAd = SeedImportedJobAdWithMunicipality(db, clock, OlofstromConceptId);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        // WRITE: capture-handlern (3-arg ctor, INGEN taxonomi) fryser concept-id:t rått.
        var captureHandler = CreateCaptureHandler(db, clock);
        var created = await captureHandler.Handle(
            new CreateApplicationFromJobAdCommand(jobAd.Id.Value), ct);
        created.IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        // Verifiera att det RÅA concept-id:t persisterades (ingen namn-resolvering
        // på write-vägen).
        var appId = new Jobbliggaren.Domain.Applications.ApplicationId(created.Value);
        var persisted = await db.Applications.AsNoTracking().FirstAsync(a => a.Id == appId, ct);
        persisted.AdSnapshot.ShouldNotBeNull();
        persisted.AdSnapshot!.MunicipalityConceptId.ShouldBe(OlofstromConceptId);
        persisted.AdSnapshot.Title.ShouldBe("Backend-utvecklare");
        persisted.AdSnapshot.Company.ShouldBe("Klarna");
        persisted.AdSnapshot.Description.ShouldBe("En beskrivning av tjänsten.");
        persisted.AdSnapshot.Source.ShouldBe(JobSource.Platsbanken.Value);
        persisted.AdSnapshot.PublishedAt.ShouldBe(PublishedAt);
        db.ChangeTracker.Clear();

        // READ: GetApplicationByIdQueryHandler (riktig ITaxonomyReadModel) resolverar
        // concept-id:t → svensk label i PreservedAd.Location.
        var readHandler = CreateReadHandler(db, taxonomy);
        var detail = await readHandler.Handle(new GetApplicationByIdQuery(created.Value), ct);

        detail.ShouldNotBeNull();
        detail!.PreservedAd.ShouldNotBeNull();
        detail.PreservedAd!.Location.ShouldBe(OlofstromLabel);
        detail.PreservedAd.Title.ShouldBe("Backend-utvecklare");
        detail.PreservedAd.Company.ShouldBe("Klarna");
        detail.PreservedAd.Description.ShouldBe("En beskrivning av tjänsten.");
    }

    // ---------------------------------------------------------------
    // Ingen kommun → RÅtt null fryses; read ger graceful null Location
    // ---------------------------------------------------------------

    [Fact]
    public async Task CaptureWithoutMunicipality_ThenReadYieldsNullLocation()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var taxonomy = scope.ServiceProvider.GetRequiredService<ITaxonomyReadModel>();
        var clock = new FixedClock(PublishedAt.AddDays(9));

        await SeedSeekerAsync(scope, db, clock);
        var jobAd = SeedImportedJobAdWithMunicipality(db, clock, municipalityConceptId: null);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var captureHandler = CreateCaptureHandler(db, clock);
        var created = await captureHandler.Handle(
            new CreateApplicationFromJobAdCommand(jobAd.Id.Value), ct);
        created.IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var appId = new Jobbliggaren.Domain.Applications.ApplicationId(created.Value);
        var persisted = await db.Applications.AsNoTracking().FirstAsync(a => a.Id == appId, ct);
        persisted.AdSnapshot.ShouldNotBeNull();
        persisted.AdSnapshot!.MunicipalityConceptId.ShouldBeNull();
        persisted.AdSnapshot.Title.ShouldBe("Backend-utvecklare");
        persisted.AdSnapshot.Description.ShouldBe("En beskrivning av tjänsten.");
        db.ChangeTracker.Clear();

        var readHandler = CreateReadHandler(db, taxonomy);
        var detail = await readHandler.Handle(new GetApplicationByIdQuery(created.Value), ct);

        detail.ShouldNotBeNull();
        detail!.PreservedAd.ShouldNotBeNull();
        detail.PreservedAd!.Location.ShouldBeNull();
        detail.PreservedAd.Title.ShouldBe("Backend-utvecklare");
    }

    // ---------------------------------------------------------------
    // Olösbart concept-id fryses rått; read droppar "Okänd kod (id)" → null
    // (aldrig ett opakt concept-id läckt till användaren, CLAUDE.md §5)
    // ---------------------------------------------------------------

    [Fact]
    public async Task CaptureUnresolvableConceptId_ThenReadDropsFallbackToNullLocation()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var taxonomy = scope.ServiceProvider.GetRequiredService<ITaxonomyReadModel>();
        var clock = new FixedClock(PublishedAt.AddDays(9));

        await SeedSeekerAsync(scope, db, clock);
        // Syntetiskt concept-id som inte finns i snapshoten. Det fryses RÅTT på
        // write-vägen; läs-vägen får "Okänd kod (...)" och droppar fallbacken → null.
        var jobAd = SeedImportedJobAdWithMunicipality(db, clock, "ZZZZ_zzz_ZZZ");
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var captureHandler = CreateCaptureHandler(db, clock);
        var created = await captureHandler.Handle(
            new CreateApplicationFromJobAdCommand(jobAd.Id.Value), ct);
        created.IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        // Rått olösbart concept-id BEVARAS i snapshot:et (ingen write-side drop).
        var appId = new Jobbliggaren.Domain.Applications.ApplicationId(created.Value);
        var persisted = await db.Applications.AsNoTracking().FirstAsync(a => a.Id == appId, ct);
        persisted.AdSnapshot.ShouldNotBeNull();
        persisted.AdSnapshot!.MunicipalityConceptId.ShouldBe("ZZZZ_zzz_ZZZ");
        db.ChangeTracker.Clear();

        // Läs-vägen surfacerar ALDRIG det opaka id:t → Location null.
        var readHandler = CreateReadHandler(db, taxonomy);
        var detail = await readHandler.Handle(new GetApplicationByIdQuery(created.Value), ct);

        detail.ShouldNotBeNull();
        detail!.PreservedAd.ShouldNotBeNull();
        detail.PreservedAd!.Location.ShouldBeNull("olösbart concept-id ska aldrig läcka som ort");
    }
}
