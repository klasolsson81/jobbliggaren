using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Applications.Attention;
using Jobbliggaren.Application.Applications.Commands.CreateApplicationFromJobAd;
using Jobbliggaren.Application.Applications.Queries.GetActivityReport;
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
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

using DomainApplication = Jobbliggaren.Domain.Applications.Application;

namespace Jobbliggaren.Api.IntegrationTests.Applications;

/// <summary>
/// #892 (CTO R1/R2/R3/R5): the applicant's preserved record survives an Art. 17 erasure.
/// An erased JobAd is a TOMBSTONE ROW (Title="", Company="[raderad]", Status=Erased) that
/// still LEFT-joins — before #892 every Applications read surface rendered the tombstone
/// verbatim, degrading the applicant's own record. The fix is a PROJECTION-level fallback
/// to the application's own <c>AdSnapshot</c> (ADR 0086) — never a status predicate in the
/// join (that would reintroduce #805-3: <c>j == null</c> must keep meaning "no ad row").
///
/// <para><b>Why real Postgres:</b> the fallback is a nested CASE over a SmartEnum
/// status equality AND an optional-owned-dependent null check (<c>a.AdSnapshot != null</c>,
/// all-null sentinel) inside the projection tree. EF InMemory client-evaluates the ternary
/// and passes REGARDLESS of whether the SQL translates — Testcontainers is the only
/// translatability oracle (architect T1/T6).</para>
///
/// <para><b>Asymmetric seed, one user:</b> Active + Archived + Erased-with-snapshot +
/// Erased-without-snapshot (pre-#315 shape via the generic <c>Application.Create</c>,
/// which links a JobAdId without a snapshot). The Archived control proves the fallback
/// fires on EXACTLY Erased (an archived ad keeps reading live identity); the
/// without-snapshot row proves the guard is <c>AdSnapshot != null</c>, not just status;
/// and a fail-loud seed read-back proves the tombstone really is degraded in the DB —
/// without it, "summary shows the real title" cannot distinguish "fallback fired" from
/// "the ad was never blanked".</para>
///
/// <para>Seeded through the REAL write path: <c>JobAd.Import</c> → <c>Erase()</c>
/// (#843/#913 — never a hand-set status), applications through the real factories.</para>
/// </summary>
[Collection("Api")]
public sealed class ErasedAdSnapshotFallbackTests(ApiFactory factory)
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);

    private static readonly IOptions<ApplicationAttentionOptions> AttentionOptions =
        Options.Create(new ApplicationAttentionOptions());

    private static IDateTimeProvider ClockAt(DateTimeOffset at)
    {
        var clock = Substitute.For<IDateTimeProvider>();
        clock.UtcNow.Returns(at);
        return clock;
    }

    private static ICurrentUser UserWith(Guid userId)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        return currentUser;
    }

    private static JobAd ImportAd(string title, string company, IDateTimeProvider clock)
    {
        var externalId = $"snapshot-fallback-{Guid.NewGuid():N}";
        var payload = $"{{\"id\":\"{externalId}\"}}";
        return JobAd.Import(
            title: title,
            company: Company.Create(company).Value,
            description: "beskrivning",
            url: $"https://arbetsformedlingen.se/platsbanken/annonser/{externalId}",
            external: ExternalReference.Create(JobSource.Platsbanken, externalId).Value,
            rawPayload: payload,
            facets: TestFacets.FromPayload(payload),
            publishedAt: T0,
            expiresAt: T0.AddDays(30),
            clock: clock, declaredContacts: [], extractTerms: TestKeywordExtraction.None).Value;
    }

    private static AdSnapshot CaptureFrom(JobAd ad, IDateTimeProvider clock) =>
        AdSnapshot.Capture(
            ad.Title, ad.Company.Name, municipalityConceptId: null, ad.Url,
            ad.Source.Value, ad.PublishedAt, ad.ExpiresAt, ad.Description,
            contacts: null, clock.UtcNow);

    private sealed record Seed(
        Guid UserId,
        Guid ActiveAppId, Guid ArchivedAppId, Guid ErasedAppId, Guid ErasedNoSnapAppId,
        Guid ActiveAdId, Guid ArchivedAdId, Guid ErasedAdId, Guid ErasedNoSnapAdId,
        // The erased ad's PRE-erase URL (Erase() blanks it in the DB and on the
        // in-memory aggregate) — the one summary field where live-tombstone ("")
        // and snapshot (real URL) DIFFER even textually, so it pins the Url arm
        // of the fallback (code-review Minor 1).
        string ErasedAdUrl);

    /// <summary>
    /// One user, four submitted applications: Active ad, Archived ad, Erased ad WITH
    /// snapshot, Erased ad WITHOUT snapshot (pre-#315 shape). Applications are created
    /// and submitted BEFORE the lifecycle transitions run — the real-world order.
    /// Ends with the fail-loud tombstone read-back.
    /// </summary>
    private static async Task<Seed> SeedAsync(AppDbContext db, CancellationToken ct)
    {
        var clock = ClockAt(T0);
        var userId = Guid.NewGuid();

        var seeker = JobSeeker.Register(userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);

        var activeAd = ImportAd("Aktiv systemutvecklare", "Aktiv AB", clock);
        var archivedAd = ImportAd("Arkiverad testare", "Arkiverad AB", clock);
        var erasedAd = ImportAd("Raderad datatekniker", "Raderad AB", clock);
        var erasedNoSnapAd = ImportAd("Raderad utan kopia", "Utan Kopia AB", clock);
        db.JobAds.AddRange(activeAd, archivedAd, erasedAd, erasedNoSnapAd);

        DomainApplication Apply(JobAd ad, bool withSnapshot)
        {
            var app = withSnapshot
                ? DomainApplication.CreateFromJobAd(
                    seeker.Id, ad.Id, CaptureFrom(ad, clock), coverLetter: null, clock).Value
                // Pre-#315 shape: JobAd-LINKED but snapshot-free — the generic factory
                // never captures one. This population is what the F5/R5 branch exists for.
                : DomainApplication.Create(
                    seeker.Id, ad.Id, coverLetter: null, manualPosting: null, clock).Value;
            app.TransitionTo(ApplicationStatus.Submitted, clock).IsSuccess.ShouldBeTrue();
            db.Applications.Add(app);
            return app;
        }

        var activeApp = Apply(activeAd, withSnapshot: true);
        var archivedApp = Apply(archivedAd, withSnapshot: true);
        var erasedApp = Apply(erasedAd, withSnapshot: true);
        var erasedNoSnapApp = Apply(erasedNoSnapAd, withSnapshot: false);
        await db.SaveChangesAsync(ct);

        // Captured BEFORE Erase() blanks it — the snapshot froze this value at
        // apply-time and the fallback must surface it (code-review Minor 1).
        var erasedAdUrl = erasedAd.Url;

        // Lifecycle transitions through the PRODUCTION methods, never column writes (#843).
        archivedAd.Archive(clock).IsSuccess.ShouldBeTrue();
        erasedAd.Erase(clock).IsSuccess.ShouldBeTrue();
        erasedNoSnapAd.Erase(clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);

        // Fail-loud seed read-back (the #913 M7 discipline): the erased rows must REALLY
        // be degraded tombstones in the database, or every "summary shows the real title"
        // assertion below is vacuously satisfiable by an intact ad.
        var tombstone = await db.JobAds.AsNoTracking()
            .Where(j => j.Id == erasedAd.Id)
            .Select(j => new { j.Title, Company = j.Company.Name, Status = j.Status.Value })
            .SingleAsync(ct);
        tombstone.Title.ShouldBe(string.Empty, "seed is broken: the erased ad kept its title");
        tombstone.Company.ShouldBe("[raderad]", "seed is broken: the erased ad kept its company");
        tombstone.Status.ShouldBe("Erased");

        return new Seed(
            userId,
            activeApp.Id.Value, archivedApp.Id.Value, erasedApp.Id.Value, erasedNoSnapApp.Id.Value,
            activeAd.Id.Value, archivedAd.Id.Value, erasedAd.Id.Value, erasedNoSnapAd.Id.Value,
            erasedAdUrl);
    }

    // ─── GetApplications (the /ansokningar list) ────────────────────────────────────

    [Fact]
    public async Task GetApplications_ErasedAdWithSnapshot_ProjectsSnapshotIdentityAndErasedStatus()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(db, ct);

        var handler = new GetApplicationsQueryHandler(
            db, UserWith(seed.UserId), ClockAt(T0), AttentionOptions);
        var result = await handler.Handle(new GetApplicationsQuery(), ct);

        result.Items.Count.ShouldBe(4);

        var erased = result.Items.Single(i => i.Id == seed.ErasedAppId).JobAd.ShouldNotBeNull();
        erased.Title.ShouldBe("Raderad datatekniker");
        erased.Company.ShouldBe("Raderad AB");
        // The Url arm's own pin: the tombstone's url is "" — only the snapshot
        // can supply the real one (code-review Minor 1).
        erased.Url.ShouldBe(seed.ErasedAdUrl);
        erased.Source.ShouldBe("Platsbanken");
        erased.Status.ShouldBe("Erased");
        erased.JobAdId.ShouldBe(seed.ErasedAdId); // R2: the tombstone row exists — truthful.

        // The Archived CONTROL: identity keeps reading LIVE — the fallback predicate is
        // exactly Erased, never != Active (overriding live archived data would be R1-wrong).
        var archived = result.Items.Single(i => i.Id == seed.ArchivedAppId).JobAd.ShouldNotBeNull();
        archived.Title.ShouldBe("Arkiverad testare");
        archived.Company.ShouldBe("Arkiverad AB");
        archived.Status.ShouldBe("Archived");

        var active = result.Items.Single(i => i.Id == seed.ActiveAppId).JobAd.ShouldNotBeNull();
        active.Status.ShouldBe("Active");
    }

    [Fact]
    public async Task GetApplications_ErasedAdWithoutSnapshot_ProjectsEmptyIdentityNeverTheSentinel()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(db, ct);

        var handler = new GetApplicationsQueryHandler(
            db, UserWith(seed.UserId), ClockAt(T0), AttentionOptions);
        var result = await handler.Handle(new GetApplicationsQuery(), ct);

        // R5: the domain tombstone sentinel "[raderad]" must never cross the Application
        // boundary — the DTO speaks absence (empty identity), the FE renders structurally.
        var row = result.Items.Single(i => i.Id == seed.ErasedNoSnapAppId).JobAd.ShouldNotBeNull();
        row.Title.ShouldBe(string.Empty);
        row.Company.ShouldBe(string.Empty);
        row.Company.ShouldNotBe("[raderad]");
        row.Url.ShouldBeNull();
        row.Status.ShouldBe("Erased");
        row.JobAdId.ShouldBe(seed.ErasedNoSnapAdId); // non-null: "erased" ≠ "no ad row" (R2).
    }

    // ─── GetPipeline (the kanban board — lockstep with GetApplications) ─────────────

    [Fact]
    public async Task GetPipeline_ErasedAd_SnapshotIdentityAndEmptyIdentityBranchesBothProject()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(db, ct);

        var handler = new GetPipelineQueryHandler(
            db, UserWith(seed.UserId), ClockAt(T0), AttentionOptions);
        var groups = await handler.Handle(new GetPipelineQuery(), ct);

        var all = groups.SelectMany(g => g.Applications).ToList();
        all.Count.ShouldBe(4);

        var erased = all.Single(i => i.Id == seed.ErasedAppId).JobAd.ShouldNotBeNull();
        erased.Title.ShouldBe("Raderad datatekniker");
        erased.Company.ShouldBe("Raderad AB");
        erased.Url.ShouldBe(seed.ErasedAdUrl); // lockstep Url pin (code-review Minor 1)
        erased.Status.ShouldBe("Erased");

        var noSnap = all.Single(i => i.Id == seed.ErasedNoSnapAppId).JobAd.ShouldNotBeNull();
        noSnap.Title.ShouldBe(string.Empty);
        noSnap.Company.ShouldBe(string.Empty);
        noSnap.Status.ShouldBe("Erased");

        var archived = all.Single(i => i.Id == seed.ArchivedAppId).JobAd.ShouldNotBeNull();
        archived.Title.ShouldBe("Arkiverad testare"); // live control — Erased-only predicate.
    }

    // ─── GetActivityReport (the Art. 17(3)(e) oracle) ───────────────────────────────

    [Fact]
    public async Task GetActivityReport_ErasedAd_EmployerReadsTheSnapshotTheRetentionArguedFor()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(db, ct);

        var handler = new GetActivityReportQueryHandler(
            db, UserWith(seed.UserId), Substitute.For<ITaxonomyReadModel>(), ClockAt(T0));
        var report = await handler.Handle(new GetActivityReportQuery(T0.Year, T0.Month), ct);

        report.Applications.Count.ShouldBe(4);

        // §14.3: "We told the supervisory authority we retain snapshot_company under
        // Art. 17(3)(e) BECAUSE she needs the employer name for her aktivitetsrapport —
        // and the aktivitetsrapport does not read the snapshot." Now it does.
        var erased = report.Applications.Single(i => i.ApplicationId == seed.ErasedAppId);
        erased.Employer.ShouldBe("Raderad AB");
        erased.Title.ShouldBe("Raderad datatekniker");
        erased.AdStatus.ShouldBe("Erased");

        // Without a snapshot the DTO speaks absence (FE renders "Saknas") — never the
        // sentinel. Before #892 this row said "[raderad]" to Arbetsförmedlingen.
        var noSnap = report.Applications.Single(i => i.ApplicationId == seed.ErasedNoSnapAppId);
        noSnap.Employer.ShouldBeNull();
        noSnap.Title.ShouldBeNull();
        noSnap.AdStatus.ShouldBe("Erased");

        var archived = report.Applications.Single(i => i.ApplicationId == seed.ArchivedAppId);
        archived.Employer.ShouldBe("Arkiverad AB"); // live control.
        archived.AdStatus.ShouldBe("Archived");

        var active = report.Applications.Single(i => i.ApplicationId == seed.ActiveAppId);
        active.AdStatus.ShouldBe("Active");
    }

    // ─── GetApplicationById (detail header + preservedAd regression) ────────────────

    [Fact]
    public async Task GetApplicationById_ErasedAd_HeaderFallsBackAndPreservedAdStaysPopulated()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(db, ct);

        var handler = new GetApplicationByIdQueryHandler(
            db, UserWith(seed.UserId),
            Substitute.For<IFailedAccessLogger>(), Substitute.For<ITaxonomyReadModel>());

        var dto = (await handler.Handle(new GetApplicationByIdQuery(seed.ErasedAppId), ct))
            .ShouldNotBeNull();
        var jobAd = dto.JobAd.ShouldNotBeNull();
        jobAd.Title.ShouldBe("Raderad datatekniker");
        jobAd.Company.ShouldBe("Raderad AB");
        // Url arm of the with-expression pinned (code-review Minor 1).
        jobAd.Url.ShouldBe(seed.ErasedAdUrl);
        jobAd.Status.ShouldBe("Erased");
        // PR4 regression guard: the full preserved copy still rides the detail wire.
        var preserved = dto.PreservedAd.ShouldNotBeNull();
        preserved.Title.ShouldBe("Raderad datatekniker");
        preserved.Contacts.ShouldBeEmpty();

        var noSnap = (await handler.Handle(new GetApplicationByIdQuery(seed.ErasedNoSnapAppId), ct))
            .ShouldNotBeNull();
        var noSnapAd = noSnap.JobAd.ShouldNotBeNull();
        noSnapAd.Title.ShouldBe(string.Empty);
        noSnapAd.Company.ShouldBe(string.Empty);
        noSnapAd.Company.ShouldNotBe("[raderad]");
        noSnapAd.Status.ShouldBe("Erased");
        noSnap.PreservedAd.ShouldBeNull();
    }

    // ─── CreateApplicationFromJobAd (the write-path refusal, R3) ────────────────────

    [Fact]
    public async Task CreateApplicationFromJobAd_ErasedAd_RefusesGoneAndPersistsNothing()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ct = TestContext.Current.CancellationToken;
        var clock = ClockAt(T0);
        var userId = Guid.NewGuid();

        var seeker = JobSeeker.Register(userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        var erasedAd = ImportAd("Raderad annons", "Raderat Bolag AB", clock);
        db.JobAds.Add(erasedAd);
        await db.SaveChangesAsync(ct);
        erasedAd.Erase(clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);

        var handler = new CreateApplicationFromJobAdCommandHandler(db, UserWith(userId), clock);
        var result = await handler.Handle(
            new CreateApplicationFromJobAdCommand(erasedAd.Id.Value), ct);

        // R3: 410 Gone — "existed and is gone", mirroring GetJobAd's read gate. Freezing
        // the tombstone into a permanent snapshot was the write-path half of the defect.
        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.Gone);

        // Counterfactual: the refusal must leave NO application row behind (the user
        // has exactly zero applications — the refused create was their only attempt).
        (await db.Applications.AsNoTracking()
            .Where(a => a.JobSeekerId == seeker.Id)
            .AnyAsync(ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task CreateApplicationFromJobAd_ArchivedAd_StillCapturesARealSnapshot()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ct = TestContext.Current.CancellationToken;
        var clock = ClockAt(T0);
        var userId = Guid.NewGuid();

        var seeker = JobSeeker.Register(userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        var archivedAd = ImportAd("Arkiverad annons", "Arkiverat Bolag AB", clock);
        db.JobAds.Add(archivedAd);
        await db.SaveChangesAsync(ct);
        archivedAd.Archive(clock).IsSuccess.ShouldBeTrue();
        await db.SaveChangesAsync(ct);

        var handler = new CreateApplicationFromJobAdCommandHandler(db, UserWith(userId), clock);
        var result = await handler.Handle(
            new CreateApplicationFromJobAdCommand(archivedAd.Id.Value), ct);

        // The refusal is Erased-ONLY: applying to an archived ad remains a legitimate
        // user action and captures the real (undegraded) identity, exactly as before.
        result.IsSuccess.ShouldBeTrue();
        // Handler-direct invocation bypasses the UnitOfWork pipeline behavior — persist
        // explicitly so the snapshot read-back below sees the row.
        await db.SaveChangesAsync(ct);
        var appId = new Jobbliggaren.Domain.Applications.ApplicationId(result.Value);
        var snapshot = (await db.Applications.AsNoTracking()
                .Where(a => a.Id == appId)
                .Select(a => a.AdSnapshot)
                .SingleAsync(ct))
            .ShouldNotBeNull();
        snapshot.Title.ShouldBe("Arkiverad annons");
        snapshot.Company.ShouldBe("Arkiverat Bolag AB");
    }

    [Fact]
    public async Task CreateApplicationFromJobAd_MissingAd_StaysNotFound()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ct = TestContext.Current.CancellationToken;
        var clock = ClockAt(T0);
        var userId = Guid.NewGuid();

        var seeker = JobSeeker.Register(userId, "Test User", clock).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(ct);

        var handler = new CreateApplicationFromJobAdCommandHandler(db, UserWith(userId), clock);
        var result = await handler.Handle(
            new CreateApplicationFromJobAdCommand(Guid.NewGuid()), ct);

        // The 404/410 split is load-bearing (R3): "never existed here" stays NotFound;
        // only a real tombstone earns Gone.
        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }
}
