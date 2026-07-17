using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Resumes.Commands.PromoteParsedResume;
using Jobbliggaren.Application.Resumes.Queries;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands.PromoteParsedResume;

// Fas 4 STEG A PR-2 (ADR 0074) — the deterministic, NO-AI promotion of a PendingReview
// ParsedResume into a canonical Resume. The handler orchestrates: auth → owner resolve →
// owner-scoped ParsedResume load (IDOR fail-closed identical to ReviewParsedResumeQueryHandler)
// → personnummer RE-SCAN over the SUBMITTED content (CTO DQ6 — the parse gate only saw the
// ORIGINAL parse) → map DTO → Resume.CreateFromParsed → parsed.Promote → Add. Returns the NEW
// Resume's Guid.
//
// EF InMemory is sufficient here: the seeded ParsedResume Content shadow is read back IN THE
// SAME CONTEXT (no Form-B decrypt needed), and there is no SmartEnum→SQL translation on this
// path (the Npgsql round-trip + DEK is proven in PromoteParsedResumeEncryptionTests). The
// personnummer-positive cases use a real Luhn-valid Swedish number (811218-9876), pnr-free
// text otherwise.
//
// SPEC-DRIVEN. RED until the command + handler + Resume.CreateFromParsed + ParsedResume.Promote
// + ResumeContentMapper ship.
// Fas 4b PR-8.1 (#657): on a successful promote the handler runs a review reconcile over the new
// canonical Resume. IResumeReviewReconciler is threaded as a trailing ctor dependency.
// CA2012: stubbing the ValueTask-returning ReconcileAsync is the known NSubstitute analyzer false positive.
#pragma warning disable CA2012
public class PromoteParsedResumeCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IResumeReviewReconciler _reconciler = Substitute.For<IResumeReviewReconciler>();
    private readonly IFailedAccessLogger _failedAccess = Substitute.For<IFailedAccessLogger>();
    private readonly Guid _userId = Guid.NewGuid();

    // A real Luhn-valid Swedish personnummer the scanner flags (parity with the encryption
    // tests' positive case). Must NOT appear in the clean fixtures below.
    private const string ValidPersonnummer = "811218-9876";

    public PromoteParsedResumeCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private PromoteParsedResumeCommandHandler CreateSut(Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, FakeDateTimeProvider.Default, _failedAccess, _reconciler);

    // ── Fixtures ─────────────────────────────────────────────────────────

    // A clean, gap-filled DTO that maps to content passing ValidateContent — no personnummer.
    private static ResumeContentDto CleanContent() => new(
        new PersonalInfoDto("Anna Andersson", "anna@example.com", "0701234567", "Stockholm"),
        Experiences:
        [
            new ExperienceDto("Beta AB", "Backend-utvecklare", new DateOnly(2021, 1, 1), null, "Byggde betaltjänster."),
        ],
        Educations:
        [
            new EducationDto("KTH", "Civilingenjör", new DateOnly(2013, 9, 1), new DateOnly(2018, 6, 1)),
        ],
        Skills:
        [
            new SkillDto("C#", 8),
            new SkillDto("PostgreSQL", 5),
        ],
        Summary: "Erfaren backend-utvecklare.");

    // Same clean DTO but with a personnummer smuggled into the Summary — the re-scan must catch it.
    private static ResumeContentDto ContentWithPersonnummerInSummary() => new(
        new PersonalInfoDto("Anna Andersson", "anna@example.com", "0701234567", "Stockholm"),
        Experiences: [],
        Educations: [],
        Skills: [],
        Summary: $"Erfaren backend-utvecklare. Pnr {ValidPersonnummer}.");

    // Personnummer in an Experience.Description — the scan must concatenate ALL user free text.
    private static ResumeContentDto ContentWithPersonnummerInDescription() => new(
        new PersonalInfoDto("Anna Andersson", null, null, null),
        Experiences:
        [
            new ExperienceDto("Beta AB", "Backend-utvecklare", new DateOnly(2021, 1, 1), null,
                $"Anställd, mitt nummer är {ValidPersonnummer}."),
        ],
        Educations: [],
        Skills: [],
        Summary: null);

    private static PromoteParsedResumeCommand Command(Guid parsedResumeId, ResumeContentDto? content = null) =>
        new(parsedResumeId, "Mitt importerade CV", content ?? CleanContent());

    // #844: unclassified text the segmenter carried down from above the first heading. The engine
    // refused to call it a profile; only the user may. Seeded here so the promotion guard below is
    // not vacuous.
    private const string UnclassifiedPreamble = "Driven utvecklare som trivs nära produktionen.";

    private static ParsedResume BuildPendingReview(JobSeekerId owner, PersonnummerScanOutcome? pnr = null)
    {
        var content = new ParsedResumeContent(
            new ParsedContact("Anna Andersson", "anna@example.com", "070-1234567", "Stockholm"),
            profile: "Erfaren backend-utvecklare.",
            experience: [new ParsedExperience("Backend-utvecklare", "Beta AB", "2021–", "raw entry")],
            preamble: UnclassifiedPreamble);

        return ParsedResume.Create(
            owner, "anna-cv.pdf", "application/pdf", ResumeLanguage.Sv,
            content, "Anna Andersson\nBackend-utvecklare, Beta AB",
            ParseConfidence.FromSections(
            [
                new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident, []),
                new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident, []),
            ]),
            pnr ?? PersonnummerScanOutcome.None, [], FakeDateTimeProvider.Default).Value;
    }

    private static async Task<(ParsedResume Parsed, JobSeeker Owner)> SeedOwnedAsync(
        Infrastructure.Persistence.AppDbContext db, Guid userId, PersonnummerScanOutcome? pnr = null)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var parsed = BuildPendingReview(seeker.Id, pnr);
        db.ParsedResumes.Add(parsed);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (parsed, seeker);
    }

    // ===============================================================
    // #844 — the carrier is NEVER silently promoted
    // ===============================================================

    /// <summary>
    /// The unclassified preamble (#844) is text the engine explicitly refused to call a profile.
    /// Promotion writes the USER-APPROVED payload — never the artifact — so a parsed CV carrying a
    /// preamble but whose approved content has no summary must promote with <c>Summary == null</c>.
    ///
    /// <para>If this ever goes red, the engine has started classifying: the residue would have become
    /// the user's Profil without her ever saying it was one, which is precisely the auto-classify
    /// this design refused (ADR 0071 / ADR 0074 propose-and-approve). The carrier's whole safety
    /// rests on the user being the one who decides.</para>
    /// </summary>
    [Fact]
    public async Task Handle_ParsedCarriesUnclassifiedPreamble_PromotesWithNullSummary_NeverAdoptsIt()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        // Precondition: the artifact really is carrying unclassified text.
        parsed.Content.Preamble.ShouldBe(UnclassifiedPreamble);

        // The approved payload leaves the summary empty — the user did NOT adopt the preamble.
        var approved = new ResumeContentDto(
            new PersonalInfoDto("Anna Andersson", "anna@example.com", "070-1234567", "Stockholm"),
            Experiences: [],
            Educations: [],
            Skills: [],
            Summary: null);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value, approved), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();

        var resume = db.Resumes.Local.ShouldHaveSingleItem();
        resume.MasterVersion.Content.Summary.ShouldBeNull();
    }

    // ===============================================================
    // Happy path
    // ===============================================================

    [Fact]
    public async Task Handle_ValidPromote_ReturnsNewResumeGuid_PersistsResume_PromotesAndSoftDeletesParsed()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBe(Guid.Empty);

        // The new Resume exists with the gap-filled Master content.
        var resume = db.Resumes.Local.ShouldHaveSingleItem();
        resume.Id.Value.ShouldBe(result.Value);
        resume.MasterVersion.Content.PersonalInfo.FullName.ShouldBe("Anna Andersson");
        resume.MasterVersion.Content.Experiences.Count.ShouldBe(1);

        // The ParsedResume is Promoted + soft-deleted (CTO DQ7).
        var reloaded = db.ParsedResumes.Local.ShouldHaveSingleItem();
        reloaded.Status.ShouldBe(ParsedResumeStatus.Promoted);
        reloaded.DeletedAt.ShouldBe(FakeDateTimeProvider.Default.UtcNow);
    }

    [Fact]
    public async Task Handle_PromotesWithImportOrigin()
    {
        // Fas 4b PR-3 (ADR 0096): promoting a parsed import must stamp the canonical Resume's
        // provenance as Import — set by CreateFromParsed construction, never by a setter.
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var resume = db.Resumes.Local.ShouldHaveSingleItem();
        resume.Origin.ShouldBe(ResumeSourceOrigin.Import);
    }

    // ===============================================================
    // Auth / not-found
    // ===============================================================

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ThrowsUnauthorizedException()
    {
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var sut = new PromoteParsedResumeCommandHandler(
            db, anon, FakeDateTimeProvider.Default, _failedAccess, _reconciler);

        await Should.ThrowAsync<UnauthorizedException>(
            () => sut.Handle(Command(Guid.NewGuid()), TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Handle_WhenJobSeekerNotFound_ReturnsNotFoundFailure()
    {
        var db = TestAppDbContextFactory.Create(); // no JobSeeker for _userId

        var result = await CreateSut(db).Handle(
            Command(Guid.NewGuid()), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("JobSeeker.NotFound");
        db.Resumes.Local.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenParsedResumeNotFound_ReturnsNotFoundFailure_NoCrossUserLog()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            Command(Guid.NewGuid()), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ParsedResume.NotFound");
        db.Resumes.Local.ShouldBeEmpty();
        // Unknown id (legitimate typo) is NOT a cross-user attempt.
        _failedAccess.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    // ===============================================================
    // IDOR cross-user (fail-closed, identical NotFound, log once, no side effects)
    // ===============================================================

    [Fact]
    public async Task Handle_WhenParsedResumeBelongsToOtherUser_ReturnsNotFound_LogsCrossUser_NoPromotion_NoResume()
    {
        var db = TestAppDbContextFactory.Create();
        // Another user's ParsedResume.
        var (otherParsed, _) = await SeedOwnedAsync(db, Guid.NewGuid());
        // The requesting user has a JobSeeker but does not own the artifact.
        var self = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(self);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            Command(otherParsed.Id.Value), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ParsedResume.NotFound"); // identical NotFound — no enumeration oracle
        _failedAccess.Received(1).LogCrossUserAttempt(
            "ParsedResume", otherParsed.Id.Value, _userId, Arg.Any<string>());

        // No promotion, no Resume created.
        otherParsed.Status.ShouldBe(ParsedResumeStatus.PendingReview);
        otherParsed.DeletedAt.ShouldBeNull();
        db.Resumes.Local.ShouldBeEmpty();
    }

    // ===============================================================
    // Personnummer re-scan over the SUBMITTED content (CTO DQ6 — highest severity)
    // ===============================================================

    [Fact]
    public async Task Handle_WhenSubmittedSummaryContainsPersonnummer_ReturnsMustBeRemoved_NoPromotion_NoResume()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value, ContentWithPersonnummerInSummary()),
            TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.PersonnummerMustBeRemoved");

        // The ParsedResume stays PendingReview; no Resume is created.
        parsed.Status.ShouldBe(ParsedResumeStatus.PendingReview);
        parsed.DeletedAt.ShouldBeNull();
        db.Resumes.Local.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenSubmittedExperienceDescriptionContainsPersonnummer_ReturnsMustBeRemoved()
    {
        // The re-scan must concatenate EVERY free-text field, not just Summary.
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value, ContentWithPersonnummerInDescription()),
            TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.PersonnummerMustBeRemoved");
        parsed.Status.ShouldBe(ParsedResumeStatus.PendingReview);
        db.Resumes.Local.ShouldBeEmpty();
    }

    // ===============================================================
    // Gate failures — the ORIGINAL parse flagged a pnr, or already Promoted
    // ===============================================================

    [Fact]
    public async Task Handle_WhenOriginalParseFlaggedPersonnummer_ReturnsMustBeRemoved_NoResume()
    {
        // The submitted content is CLEAN, but the ParsedResume's ORIGINAL parse flagged a pnr →
        // parsed.Promote() → EnsureReadyForPromotion rejects with PersonnummerMustBeRemoved.
        var flagged = PersonnummerScanOutcome.FromMatches(
            PersonnummerScanner.Scan($"Pnr {ValidPersonnummer} i CV."));
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId, flagged);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ParsedResume.PersonnummerMustBeRemoved");
        parsed.Status.ShouldBe(ParsedResumeStatus.PendingReview);
        db.Resumes.Local.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_WhenParsedResumeAlreadyDiscarded_ReturnsNotFound_NoResume()
    {
        // A Discarded ParsedResume is soft-deleted (DeletedAt set) → the global query filter
        // (DeletedAt == null) hides it from the owner-scoped load, so the handler returns the
        // fail-closed NotFound (NOT the NotPendingReview gate code, which is only reachable for a
        // LOADED non-PendingReview row — and the soft-delete filter prevents loading one). The
        // NotPendingReview gate itself is proven at the aggregate level in ParsedResumeTests.
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);
        parsed.Discard(FakeDateTimeProvider.Default); // → Discarded, soft-deleted
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ParsedResume.NotFound");
        db.Resumes.Local.ShouldBeEmpty();
    }

    // ===============================================================
    // Degraded submitted content → ValidateContent code via CreateFromParsed
    // ===============================================================

    [Fact]
    public async Task Handle_WhenSubmittedContentHasEmptyFullName_ReturnsFullNameRequired_NoPromotion_NoResume()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        var degraded = new ResumeContentDto(
            new PersonalInfoDto(string.Empty, null, null, null),
            Experiences: [], Educations: [], Skills: [], Summary: null);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value, degraded), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FullNameRequired");
        // CreateFromParsed failed before Promote — the ParsedResume is untouched.
        parsed.Status.ShouldBe(ParsedResumeStatus.PendingReview);
        parsed.DeletedAt.ShouldBeNull();
        db.Resumes.Local.ShouldBeEmpty();
    }

    // ===============================================================
    // Fas 4b PR-8.1 call-site pins (#657) — the review reconcile runs on success only
    // ===============================================================

    [Fact]
    public async Task Handle_OnSuccess_RunsReviewReconcileForTheNewResume_WithNoAutoResolve()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _reconciler.Received(1).ReconcileAsync(
            Arg.Is<Resume>(r => r.Id.Value == result.Value),
            Arg.Is<IReadOnlyCollection<string>>(x => x == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenParsedResumeNotFound_DoesNotRunReviewReconcile()
    {
        var db = TestAppDbContextFactory.Create();
        var seeker = JobSeeker.Register(_userId, "Test User", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            Command(Guid.NewGuid()), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        await _reconciler.DidNotReceive().ReconcileAsync(
            Arg.Any<Resume>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }
}
