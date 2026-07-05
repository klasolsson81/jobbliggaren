using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

// Application-typen krockar med Jobbliggaren.Application-namespacet; alias per fil
// (integrationsprojektet saknar global alias, jfr AdSnapshotPersistenceTests).
using DomainApplication = Jobbliggaren.Domain.Applications.Application;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

// RÖD svit (TDD) mot Testcontainers Postgres (relationell provider).
// Spec: ADR 0092 D4 — StatusChange append-only timeline (relaterad aggregat-ägd
// entitet med shadow-FK ApplicationId + cascade, from_status/to_status name-
// strängar, query filter DeletedAt == null). Speglar AdSnapshotPersistenceTests-
// mönstret (DI-scope, riktig Postgres). Query-filter-semantiken (soft-delete
// exkludering + IgnoreQueryFilters-retrieval) hedras inte meningsfullt av
// InMemory → MÅSTE testas mot Npgsql.
[Collection("Api")]
public class ApplicationStatusChangeTimelinePersistenceTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // Fasta capture-instanter: den DI-resolverade IDateTimeProvider:n är en RIKTIG
    // systemklocka (varje .UtcNow ger ny tid). StatusChange.ChangedAt fångas från
    // den klocka som skickas till TransitionTo, så fasta värden (0 sub-sekund →
    // exakt round-trip genom Postgres timestamptz) gör assertionen deterministisk.
    private static readonly DateTimeOffset T1 =
        new(2026, 5, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = T1.AddDays(2);
    private static readonly DateTimeOffset T3 = T1.AddDays(5);

    private static async Task<JobSeekerId> SeedSeekerAsync(
        AppDbContext db, IDateTimeProvider clock, CancellationToken ct)
    {
        var seeker = JobSeeker.Register(Guid.NewGuid(), "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);
        return seeker.Id;
    }

    // Fast klocka per transition (ChangedAt fångas från clock.UtcNow). NSubstitute
    // — samma mock-bibliotek som resten av sviten; ett värde per instans.
    private static IDateTimeProvider ClockAt(DateTimeOffset instant)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(instant);
        return clock;
    }

    // ---------------------------------------------------------------
    // Round-trip — From/To/ChangedAt överlever save + reload intakt
    // ---------------------------------------------------------------

    [Fact]
    public async Task StatusChanges_RoundTrip_FromToChangedAtIntact()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seekerId = await SeedSeekerAsync(db, clock, ct);
        // coverLetter null → ingen fält-kryptering, ingen DEK-värmning krävs
        // (jfr AdSnapshotPersistenceTests.Application_WithAdSnapshot_RoundTripsAllFields).
        var app = DomainApplication.Create(seekerId, null, null, null, clock).Value;
        app.TransitionTo(ApplicationStatus.Submitted, ClockAt(T1));
        app.TransitionTo(ApplicationStatus.Acknowledged, ClockAt(T2));
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        var reloaded = await db.Applications
            .AsNoTracking()
            .Include(a => a.StatusChanges)
            .FirstAsync(a => a.Id == app.Id, ct);

        // Navigation-collection-ordningen är inte garanterad av EF → sortera i
        // assertionen (handlern gör motsvarande OrderBy(ChangedAt)).
        var ordered = reloaded.StatusChanges.OrderBy(s => s.ChangedAt).ToList();
        ordered.Count.ShouldBe(2);

        ordered[0].From.ShouldBe(ApplicationStatus.Draft);
        ordered[0].To.ShouldBe(ApplicationStatus.Submitted);
        ordered[0].ChangedAt.ShouldBe(T1);

        ordered[1].From.ShouldBe(ApplicationStatus.Submitted);
        ordered[1].To.ShouldBe(ApplicationStatus.Acknowledged);
        ordered[1].ChangedAt.ShouldBe(T2);
    }

    // ---------------------------------------------------------------
    // Soft-delete — query filter exkluderar den soft-deletade raden, men den
    // finns kvar (IgnoreQueryFilters hämtar den → soft, inte hard, delete)
    // ---------------------------------------------------------------

    [Fact]
    public async Task StatusChange_WhenSoftDeleted_IsFilteredOut_ButRetrievableWithIgnoreQueryFilters()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var seekerId = await SeedSeekerAsync(db, clock, ct);
        var app = DomainApplication.Create(seekerId, null, null, null, clock).Value;
        app.TransitionTo(ApplicationStatus.Submitted, ClockAt(T1));
        app.TransitionTo(ApplicationStatus.Acknowledged, ClockAt(T2));
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        // Soft-deleta EN StatusChange (spåra aggregatet, mutera barnet, spara).
        // OBS: aggregatets egen SoftDelete raderar hela ansökan — här isoleras
        // en enskild timeline-rad för att bevisa barn-filtret separat.
        var tracked = await db.Applications
            .Include(a => a.StatusChanges)
            .FirstAsync(a => a.Id == app.Id, ct);
        var target = tracked.StatusChanges.OrderBy(s => s.ChangedAt).First();
        target.SoftDelete(ClockAt(T3));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();

        // Default query filter (DeletedAt == null) exkluderar den soft-deletade.
        var filtered = await db.Applications
            .AsNoTracking()
            .Include(a => a.StatusChanges)
            .FirstAsync(a => a.Id == app.Id, ct);
        filtered.StatusChanges.Count.ShouldBe(1);
        filtered.StatusChanges.ShouldAllBe(s => s.DeletedAt == null);

        // IgnoreQueryFilters hämtar tillbaka den soft-deletade → raden finns kvar
        // (soft delete, inte hard delete).
        var all = await db.Applications
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(a => a.StatusChanges)
            .FirstAsync(a => a.Id == app.Id, ct);
        all.StatusChanges.Count.ShouldBe(2);
        all.StatusChanges.ShouldContain(s => s.DeletedAt.HasValue);
    }
}
