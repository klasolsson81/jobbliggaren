using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Commands.UpsertExternalJobAd;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Commands.UpsertExternalJobAd;

/// <summary>
/// F2-P8c — race-säker upsert per ADR 0032 §5. Verifierar:
/// <list type="bullet">
/// <item>Happy path: Add → SaveChanges → <see cref="UpsertOutcome.Added"/></item>
/// <item>Domain-validation failures → <see cref="UpsertOutcome.Skipped"/> (Company.Name tom, JobAd-invariants)</item>
/// <item>ExternalReference-validation failure (Source=Manual) → Skipped</item>
/// <item>UNIQUE-violation → detach + reload existing + UpdateFromSource → <see cref="UpsertOutcome.Updated"/></item>
/// <item>UNIQUE-violation men existing redan borttagen → Skipped</item>
/// <item>DbUpdateException som INTE är UNIQUE-violation → propagerar (handler sväljer inte okända DB-fel)</item>
/// </list>
/// </summary>
public class UpsertExternalJobAdCommandHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 13, 6, 0, 0, TimeSpan.Zero);

    private static JobAdImportItem ValidItem(
        string externalId = "ext-1",
        string title = "Backend-utvecklare",
        string companyName = "Klarna",
        string description = "Vi söker en backend-utvecklare.",
        string url = "https://example.com/jobs/1",
        IReadOnlyList<JobAdRequirement>? requirements = null,
        IReadOnlyList<AdContact>? declaredContacts = null) =>
        new(
            ExternalId: externalId,
            Title: title,
            CompanyName: companyName,
            Description: description,
            Url: url,
            PublishedAt: Now.AddDays(-1),
            ExpiresAt: Now.AddDays(30),
            SanitizedRawPayload: "{\"id\":\"ext-1\"}",
            Facets: TestFacets.FromPayload("{\"id\":\"ext-1\"}"),
            // F4-4b — defaults to empty so the existing upsert tests stay
            // keyword/skill-only.
            Requirements: requirements ?? [],
            // #842 Tier A — empty means "the source declared nobody".
            DeclaredContacts: declaredContacts ?? []);

    private static UpsertExternalJobAdCommandHandler CreateHandler(
        IAppDbContext db,
        IDbExceptionInspector? inspector = null,
        IJobAdKeywordExtractor? extractor = null)
    {
        if (extractor is null)
        {
            // F4-4 — default the extractor to a no-op (Empty) so these
            // upsert tests stay focused on the race-safe upsert behavior.
            extractor = Substitute.For<IJobAdKeywordExtractor>();
            extractor.Extract(Arg.Any<JobAdExtractionInput>()).Returns(ExtractedTerms.Empty);
        }

        return new(
            db,
            inspector ?? Substitute.For<IDbExceptionInspector>(),
            extractor,
            new FakeDateTimeProvider(Now),
            NullLogger<UpsertExternalJobAdCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WithNewExternalJobAd_AddsAndReturnsAddedOutcome()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = CreateHandler(db);
        var item = ValidItem("ext-new");
        var command = new UpsertExternalJobAdCommand(JobSource.Platsbanken, "ext-new", item);

        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(UpsertOutcome.Added);
        var persisted = await db.JobAds.AsNoTracking()
            .FirstOrDefaultAsync(j => j.External!.ExternalId == "ext-new",
                TestContext.Current.CancellationToken);
        persisted.ShouldNotBeNull();
        persisted.External!.Source.ShouldBe(JobSource.Platsbanken);
    }

    [Fact]
    public async Task Handle_WithEmptyCompanyName_SkipsViaCompanyValidation()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = CreateHandler(db);
        var item = ValidItem(companyName: "   ");
        var command = new UpsertExternalJobAdCommand(JobSource.Platsbanken, "ext-1", item);

        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(UpsertOutcome.Skipped);
        (await db.JobAds.AsNoTracking()
            .CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_WithManualSource_SkipsViaExternalReferenceValidation()
    {
        // ExternalReference.Create avvisar JobSource.Manual.
        var db = TestAppDbContextFactory.Create();
        var handler = CreateHandler(db);
        var item = ValidItem();
        var command = new UpsertExternalJobAdCommand(JobSource.Manual, "ext-1", item);

        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(UpsertOutcome.Skipped);
    }

    [Fact]
    public async Task Handle_WithEmptyTitle_SkipsViaJobAdImportValidation()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = CreateHandler(db);
        var item = ValidItem(title: "   ");
        var command = new UpsertExternalJobAdCommand(JobSource.Platsbanken, "ext-1", item);

        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(UpsertOutcome.Skipped);
    }

    [Fact]
    public async Task Handle_WhenUniqueViolation_DetachesAndReloadsExistingForUpdate()
    {
        // Seed existing via riktig in-memory DbContext.
        var seedDb = TestAppDbContextFactory.Create();
        var ct = TestContext.Current.CancellationToken;
        await SeedExistingExternalJobAdAsync(seedDb, "ext-collision", "Old Title", ct);

        // Mock IAppDbContext för att tvinga DbUpdateException på första SaveChanges.
        var db = Substitute.For<IAppDbContext>();
        db.JobAds.Returns(seedDb.JobAds);
        var saveCallCount = 0;
        db.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                saveCallCount++;
                if (saveCallCount == 1)
                {
                    throw new DbUpdateException("UNIQUE-violation simulerad");
                }
                return Task.FromResult(1);
            });

        var inspector = Substitute.For<IDbExceptionInspector>();
        inspector.IsUniqueConstraintViolation(Arg.Any<DbUpdateException>()).Returns(true);

        var handler = CreateHandler(db, inspector);
        var item = ValidItem("ext-collision", title: "New Title");
        var command = new UpsertExternalJobAdCommand(
            JobSource.Platsbanken, "ext-collision", item);

        var result = await handler.Handle(command, ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(UpsertOutcome.Updated);
        db.Received(1).Detach(Arg.Any<JobAd>());
    }

    [Fact]
    public async Task Handle_WhenUniqueViolationButExistingMissing_ReturnsSkipped()
    {
        // Empty store — INSERT skall failas av oss, men "reload" hittar inget.
        var seedDb = TestAppDbContextFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var db = Substitute.For<IAppDbContext>();
        db.JobAds.Returns(seedDb.JobAds);
        db.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new DbUpdateException("UNIQUE-violation"));

        var inspector = Substitute.For<IDbExceptionInspector>();
        inspector.IsUniqueConstraintViolation(Arg.Any<DbUpdateException>()).Returns(true);

        var handler = CreateHandler(db, inspector);
        var item = ValidItem("ext-gone");
        var command = new UpsertExternalJobAdCommand(
            JobSource.Platsbanken, "ext-gone", item);

        var result = await handler.Handle(command, ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(UpsertOutcome.Skipped);
    }

    [Fact]
    public async Task Handle_WhenNonUniqueDbUpdateException_PropagatesException()
    {
        // Andra DB-fel (t.ex. constraint NOT NULL, deadlock) får inte sväljas.
        var seedDb = TestAppDbContextFactory.Create();
        var ct = TestContext.Current.CancellationToken;

        var db = Substitute.For<IAppDbContext>();
        db.JobAds.Returns(seedDb.JobAds);
        var thrown = new DbUpdateException("annan DB-fail");
        db.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw thrown);

        var inspector = Substitute.For<IDbExceptionInspector>();
        inspector.IsUniqueConstraintViolation(Arg.Any<DbUpdateException>()).Returns(false);

        var handler = CreateHandler(db, inspector);
        var item = ValidItem("ext-other");
        var command = new UpsertExternalJobAdCommand(
            JobSource.Platsbanken, "ext-other", item);

        var ex = await Should.ThrowAsync<DbUpdateException>(
            () => handler.Handle(command, ct).AsTask());
        ex.ShouldBeSameAs(thrown);
    }

    [Fact]
    public async Task Handle_WithNullCommand_ThrowsArgumentNullException()
    {
        var db = TestAppDbContextFactory.Create();
        var handler = CreateHandler(db);

        await Should.ThrowAsync<ArgumentNullException>(
            () => handler.Handle(null!, TestContext.Current.CancellationToken).AsTask());
    }

    // ---------------------------------------------------------------
    // F4-4 (#874) — the deterministic extraction lands on the aggregate at the
    // single external-ad write funnel, on BOTH the Add and the Update path (the
    // handler's Extract closure is folded into Import/UpdateFromSource on both branches).
    // ---------------------------------------------------------------

    private static ExtractedTerms SampleTerms() =>
        ExtractedTerms.From(
        [
            new ExtractedTerm(
                Lexeme: "1TC7_x8s_V7V", Display: "JavaScript",
                Kind: ExtractedTermKind.Skill, Source: ExtractedTermSource.Description,
                MatchedOn: "JavaScript", ConceptId: "1TC7_x8s_V7V", Weight: 1),
        ]);

    [Fact]
    public async Task Handle_OnAddPath_SetsExtractedTermsOnTheAggregate()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var terms = SampleTerms();
        var extractor = Substitute.For<IJobAdKeywordExtractor>();
        extractor.Extract(Arg.Any<JobAdExtractionInput>()).Returns(terms);
        var handler = CreateHandler(db, extractor: extractor);
        var item = ValidItem("ext-extract-add");
        var command = new UpsertExternalJobAdCommand(JobSource.Platsbanken, "ext-extract-add", item);

        var result = await handler.Handle(command, ct);

        result.Value.ShouldBe(UpsertOutcome.Added);
        // The extractor was invoked with the ad's text and the result was persisted.
        extractor.Received().Extract(Arg.Is<JobAdExtractionInput>(
            i => i.Title == item.Title && i.Description == item.Description));
        var persisted = await db.JobAds.AsNoTracking()
            .FirstAsync(j => j.External!.ExternalId == "ext-extract-add", ct);
        persisted.ExtractedTerms.ShouldNotBeNull(
            "Add-vägens extraktion ska sätta ExtractedTerms på aggregatet.");
        persisted.ExtractedTerms!.ShouldBe(terms);
    }

    [Fact]
    public async Task Handle_OnUpdatePath_SetsExtractedTermsOnTheExistingAggregate()
    {
        // Force the UNIQUE-collision → Update path, and assert the re-extraction
        // lands on the reloaded `existing` aggregate (applying only on Add would
        // leave updated ads with stale terms — the production comment's invariant).
        var seedDb = TestAppDbContextFactory.Create();
        var ct = TestContext.Current.CancellationToken;
        await SeedExistingExternalJobAdAsync(seedDb, "ext-extract-upd", "Old Title", ct);

        var db = Substitute.For<IAppDbContext>();
        db.JobAds.Returns(seedDb.JobAds);
        var saveCallCount = 0;
        db.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                saveCallCount++;
                if (saveCallCount == 1)
                    throw new DbUpdateException("UNIQUE-violation simulerad");
                return Task.FromResult(1);
            });
        var inspector = Substitute.For<IDbExceptionInspector>();
        inspector.IsUniqueConstraintViolation(Arg.Any<DbUpdateException>()).Returns(true);

        var terms = SampleTerms();
        var extractor = Substitute.For<IJobAdKeywordExtractor>();
        extractor.Extract(Arg.Any<JobAdExtractionInput>()).Returns(terms);

        var handler = CreateHandler(db, inspector, extractor);
        var item = ValidItem("ext-extract-upd", title: "New Title", description: "Ny beskrivning");
        var command = new UpsertExternalJobAdCommand(JobSource.Platsbanken, "ext-extract-upd", item);

        var result = await handler.Handle(command, ct);

        result.Value.ShouldBe(UpsertOutcome.Updated);
        // The reloaded existing aggregate (tracked in seedDb) carries the new terms.
        var existing = await seedDb.JobAds
            .FirstAsync(j => j.External!.ExternalId == "ext-extract-upd", ct);
        existing.Title.ShouldBe("New Title", "Update-vägen uppdaterade titeln.");
        existing.ExtractedTerms.ShouldNotBeNull(
            "Update-vägens extraktion ska sätta ExtractedTerms på `existing`.");
        existing.ExtractedTerms!.ShouldBe(terms);
        // Re-extraction used the UPDATED text, not the stale seeded text.
        extractor.Received().Extract(Arg.Is<JobAdExtractionInput>(
            i => i.Title == "New Title" && i.Description == "Ny beskrivning"));
    }

    // ---------------------------------------------------------------
    // F4-4b — the handler's Extract closure threads item.Requirements into the
    // JobAdExtractionInput on BOTH the Add and the Update path (the closure captures
    // the import item so requirements — which live on the import DTO, not the
    // aggregate — reach the extractor).
    // ---------------------------------------------------------------

    private static IReadOnlyList<JobAdRequirement> SampleRequirements() =>
    [
        new JobAdRequirement(ExtractedTermSource.MustHave, "Rq01_must_aaa", "C#", 10),
        new JobAdRequirement(ExtractedTermSource.NiceToHave, "Rq02_nice_bbb", "Azure", 5),
    ];

    [Fact]
    public async Task Handle_OnAddPath_PassesItemRequirementsToTheExtractor()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var requirements = SampleRequirements();
        var extractor = Substitute.For<IJobAdKeywordExtractor>();
        extractor.Extract(Arg.Any<JobAdExtractionInput>()).Returns(ExtractedTerms.Empty);
        var handler = CreateHandler(db, extractor: extractor);
        var item = ValidItem("ext-req-add", requirements: requirements);
        var command = new UpsertExternalJobAdCommand(JobSource.Platsbanken, "ext-req-add", item);

        var result = await handler.Handle(command, ct);

        result.Value.ShouldBe(UpsertOutcome.Added);
        // The extraction input carries the item's requirements (same instances).
        extractor.Received().Extract(Arg.Is<JobAdExtractionInput>(
            i => i.Requirements != null && i.Requirements.Count == 2
                 && i.Requirements.Any(r => r.ConceptId == "Rq01_must_aaa" && r.Source == ExtractedTermSource.MustHave)
                 && i.Requirements.Any(r => r.ConceptId == "Rq02_nice_bbb" && r.Source == ExtractedTermSource.NiceToHave)));
    }

    [Fact]
    public async Task Handle_OnUpdatePath_PassesItemRequirementsToTheExtractor()
    {
        // Force the UNIQUE-collision → Update path; the re-extraction on `existing`
        // must thread the (possibly changed) item.Requirements — applying only on
        // Add would leave an updated ad with stale requirement terms.
        var seedDb = TestAppDbContextFactory.Create();
        var ct = TestContext.Current.CancellationToken;
        await SeedExistingExternalJobAdAsync(seedDb, "ext-req-upd", "Old Title", ct);

        var db = Substitute.For<IAppDbContext>();
        db.JobAds.Returns(seedDb.JobAds);
        var saveCallCount = 0;
        db.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                saveCallCount++;
                if (saveCallCount == 1)
                    throw new DbUpdateException("UNIQUE-violation simulerad");
                return Task.FromResult(1);
            });
        var inspector = Substitute.For<IDbExceptionInspector>();
        inspector.IsUniqueConstraintViolation(Arg.Any<DbUpdateException>()).Returns(true);

        var extractor = Substitute.For<IJobAdKeywordExtractor>();
        extractor.Extract(Arg.Any<JobAdExtractionInput>()).Returns(ExtractedTerms.Empty);

        var handler = CreateHandler(db, inspector, extractor);
        var item = ValidItem("ext-req-upd", title: "New Title", requirements: SampleRequirements());
        var command = new UpsertExternalJobAdCommand(JobSource.Platsbanken, "ext-req-upd", item);

        var result = await handler.Handle(command, ct);

        result.Value.ShouldBe(UpsertOutcome.Updated);
        extractor.Received().Extract(Arg.Is<JobAdExtractionInput>(
            i => i.Requirements != null && i.Requirements.Count == 2
                 && i.Requirements.Any(r => r.ConceptId == "Rq01_must_aaa")));
    }

    private static async Task SeedExistingExternalJobAdAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db,
        string externalId,
        string title,
        CancellationToken ct)
    {
        var company = Company.Create("Klarna").Value;
        var external = ExternalReference.Create(JobSource.Platsbanken, externalId).Value;
        var clock = new FakeDateTimeProvider(Now.AddDays(-2));
        var jobAd = JobAd.Import(
            title, company, "Beskrivning", "https://example.com/jobs/seed",
            external, "{\"id\":\"seed\"}", TestFacets.FromPayload("{\"id\":\"seed\"}"),
            [],
            Now.AddDays(-1), Now.AddDays(30), clock, extractTerms: TestKeywordExtraction.None).Value;
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
    }
}
