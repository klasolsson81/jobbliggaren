using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Resumes.Commands.AutoPromoteParsedResume;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands.AutoPromoteParsedResume;

// CV-pivot PR 5a (CTO-bind 2026-07-17) — the "spara direkt" auto-promote of a CLEAN
// PendingReview ParsedResume, verbatim, no synthesis. The handler orchestrates: auth →
// owner {Id, DisplayName} projection → owner-scoped tracked load (IDOR fail-closed, parity
// PromoteParsedResume) → THREE policy gates (pnr → preamble → parser confidence) → verbatim
// projection → shared pnr guard on the COMPOSED dto → ToDomain → CreateFromParsed
// (buildability) → parsed.Promote → Add → reconciler-seed → in-handler Art. 22 audit row
// (Promoted branch ONLY). Every non-promote exit is Result.Success(LeftPending(reason)) and
// precedes every mutation; Result.Failure is reserved for genuine faults (owner/IDOR).
//
// EF InMemory is sufficient here (parity PromoteParsedResumeCommandHandlerTests): content
// shadows read back in the same context, no SmartEnum→SQL translation on this path; the
// real-Postgres DEK round-trip is proven in AutoPromoteParsedResumeEncryptionTests.
// CA2012: stubbing the ValueTask-returning ReconcileAsync is the known NSubstitute analyzer
// false positive.
#pragma warning disable CA2012
public class AutoPromoteParsedResumeCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IResumeReviewReconciler _reconciler = Substitute.For<IResumeReviewReconciler>();
    private readonly IFailedAccessLogger _failedAccess = Substitute.For<IFailedAccessLogger>();
    private readonly ICorrelationIdProvider _correlationId = Substitute.For<ICorrelationIdProvider>();
    private readonly IRequestContextProvider _requestContext = Substitute.For<IRequestContextProvider>();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _correlation = Guid.NewGuid();

    // A real Luhn-valid Swedish personnummer the scanner flags (parity with the promote
    // tests' positive cases). Must NOT appear in the clean fixtures below.
    private const string ValidPersonnummer = "811218-9876";

    /// <summary>The account holder's display name — the BOUND name source (Klas
    /// 2026-07-16). Deliberately different from the parsed contact name below so every
    /// happy-path assertion also pins "never the parsed name".</summary>
    private const string AccountName = "Anna Kontosson";

    /// <summary>The name the FILE claims — must never reach the canonical CV.</summary>
    private const string ParsedContactName = "Fil Namnsson";

    public AutoPromoteParsedResumeCommandHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
        _correlationId.Current.Returns(_correlation);
        _requestContext.IpAddress.Returns("203.0.113.7");
        _requestContext.UserAgent.Returns("test-agent");
    }

    private AutoPromoteParsedResumeCommandHandler CreateSut(
        Infrastructure.Persistence.AppDbContext db) =>
        new(db, _currentUser, FakeDateTimeProvider.Default, _failedAccess, _reconciler,
            _correlationId, _requestContext);

    // ── Fixtures ─────────────────────────────────────────────────────────

    private static ParsedResumeContent CleanParsedContent(
        string? preamble = null,
        IReadOnlyList<ParsedExperience>? experience = null) =>
        new(
            new ParsedContact(ParsedContactName, "fil@example.com", "070-1234567", "Stockholm"),
            profile: "Erfaren backend-utvecklare.",
            experience: experience ??
                [new ParsedExperience("Backend-utvecklare", "Beta AB", "2019–2022", "raw entry")],
            education: [new ParsedEducation("KTH", "Civilingenjör", "2013–2018", "raw edu")],
            skills: ["C#"],
            languages: ["Svenska"],
            sections: [new ParsedSection("Projekt", [new ParsedSectionEntry("Kassasystem", ["Byggde kassasystem."])])],
            preamble: preamble);

    private static ParseConfidence Confident() =>
        ParseConfidence.FromSections(
        [
            new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident, []),
            new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident, []),
        ]);

    private static ParseConfidence Degraded() =>
        ParseConfidence.FromSections(
        [
            new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Degraded, []),
            new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident, []),
        ]);

    private static ParsedResume BuildParsed(
        JobSeekerId owner,
        ParsedResumeContent? content = null,
        ParseConfidence? confidence = null,
        PersonnummerScanOutcome? pnr = null) =>
        ParsedResume.Create(
            owner, "anna-cv.pdf", "application/pdf", ResumeLanguage.Sv,
            content ?? CleanParsedContent(),
            "raw text",
            confidence ?? Confident(),
            pnr ?? PersonnummerScanOutcome.None,
            [], FakeDateTimeProvider.Default).Value;

    private static async Task<(ParsedResume Parsed, JobSeeker Owner)> SeedOwnedAsync(
        Infrastructure.Persistence.AppDbContext db,
        Guid userId,
        ParsedResumeContent? content = null,
        ParseConfidence? confidence = null,
        PersonnummerScanOutcome? pnr = null,
        string displayName = AccountName)
    {
        var seeker = JobSeeker.Register(userId, displayName, FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var parsed = BuildParsed(seeker.Id, content, confidence, pnr);
        db.ParsedResumes.Add(parsed);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (parsed, seeker);
    }

    private static AutoPromoteParsedResumeCommand Command(Guid parsedResumeId, string? nameOverride = null) =>
        new(parsedResumeId, nameOverride);

    /// <summary>The shared LeftPending contract: Success carrying the reason, the artifact
    /// untouched and still PendingReview, no Resume, no audit row, no reconcile — nothing
    /// for the unconditional UnitOfWork save to persist.</summary>
    private async Task AssertLeftPendingAsync(
        Infrastructure.Persistence.AppDbContext db,
        Result<AutoPromoteOutcome> result,
        ParsedResume parsed,
        AutoPromoteBlockReason expectedReason)
    {
        result.IsSuccess.ShouldBeTrue();
        var pending = result.Value.ShouldBeOfType<AutoPromoteOutcome.LeftPending>();
        pending.Reason.ShouldBe(expectedReason);

        parsed.Status.ShouldBe(ParsedResumeStatus.PendingReview);
        parsed.DeletedAt.ShouldBeNull();
        db.Resumes.Local.ShouldBeEmpty();
        db.AuditLogEntries.Local.ShouldBeEmpty();
        await _reconciler.DidNotReceive().ReconcileAsync(
            Arg.Any<Resume>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
    }

    // ===============================================================
    // Happy path — clean, confident parse promotes verbatim
    // ===============================================================

    [Fact]
    public async Task Handle_CleanConfidentParse_ReturnsPromoted_PersistsResume_PromotesAndSoftDeletesParsed()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var promoted = result.Value.ShouldBeOfType<AutoPromoteOutcome.Promoted>();

        var resume = db.Resumes.Local.ShouldHaveSingleItem();
        resume.Id.Value.ShouldBe(promoted.ResumeId);
        resume.Origin.ShouldBe(ResumeSourceOrigin.Import);
        resume.SourceParsedResumeId.ShouldBe(parsed.Id);

        parsed.Status.ShouldBe(ParsedResumeStatus.Promoted);
        parsed.DeletedAt.ShouldBe(FakeDateTimeProvider.Default.UtcNow);
    }

    [Fact]
    public async Task Handle_CleanParse_MapsVerbatimPerBoundTable()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var content = db.Resumes.Local.ShouldHaveSingleItem().MasterVersion.Content;

        content.PersonalInfo.Email.ShouldBe("fil@example.com");
        content.PersonalInfo.Phone.ShouldBe("070-1234567");
        content.PersonalInfo.Location.ShouldBe("Stockholm");
        content.Summary.ShouldBe("Erfaren backend-utvecklare.");

        var exp = content.Experiences.ShouldHaveSingleItem();
        exp.Company.ShouldBe("Beta AB");
        exp.Role.ShouldBe("Backend-utvecklare");
        exp.StartDate.ShouldBeNull();       // honest date absence (#914)
        exp.EndDate.ShouldBeNull();
        exp.RawPeriod.ShouldBe("2019–2022"); // verbatim from the file

        var edu = content.Educations.ShouldHaveSingleItem();
        edu.Institution.ShouldBe("KTH");
        edu.Degree.ShouldBe("Civilingenjör");
        edu.StartDate.ShouldBeNull();
        edu.EndDate.ShouldBeNull();
        edu.RawPeriod.ShouldBe("2013–2018");

        var skill = content.Skills.ShouldHaveSingleItem();
        skill.Name.ShouldBe("C#");
        skill.YearsExperience.ShouldBeNull();

        var language = content.Languages.ShouldHaveSingleItem();
        language.Name.ShouldBe("Svenska");
        language.Proficiency.ShouldBe(LanguageProficiency.NotStated);

        content.SkillGroups.ShouldBeEmpty();

        var section = content.Sections.ShouldHaveSingleItem();
        section.Heading.ShouldBe("Projekt");
        var entry = section.Entries.ShouldHaveSingleItem();
        entry.Title.ShouldBe("Kassasystem");
        entry.Lines.ShouldBe(["Byggde kassasystem."]);
    }

    /// <summary>
    /// The Klas-bound name rule: the canonical CV carries the ACCOUNT holder's name — both
    /// as Resume.Name and as PersonalInfo.FullName — never the name the FILE claims. If
    /// this goes red, an uploaded document has started deciding who the user is.
    /// </summary>
    [Fact]
    public async Task Handle_NameIsTheAccountDisplayName_NeverTheParsedContactName()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var resume = db.Resumes.Local.ShouldHaveSingleItem();
        resume.Name.ShouldBe(AccountName);
        resume.MasterVersion.Content.PersonalInfo.FullName.ShouldBe(AccountName);
        resume.MasterVersion.Content.PersonalInfo.FullName.ShouldNotBe(ParsedContactName);
    }

    /// <summary>Every auto-promoted entry is date-less by construction — auto-promote can
    /// never emit an end-only entry (CTO-bind triage A: the documented v1 display drop has
    /// no producer on this path).</summary>
    [Fact]
    public async Task Handle_CleanParse_NeverEmitsStructuredDates()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        await CreateSut(db).Handle(Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        var content = db.Resumes.Local.ShouldHaveSingleItem().MasterVersion.Content;
        content.Experiences.ShouldAllBe(e => e.StartDate == null && e.EndDate == null);
        content.Educations.ShouldAllBe(e => e.StartDate == null && e.EndDate == null);
    }

    /// <summary>
    /// The Description fork, bound Option (a): the parse has no structured description —
    /// RawText is the WHOLE entry block (title/org/period lines included), so promoting it
    /// as Description would double those lines in render and corrupt the review engine's
    /// TextIsDescriptionOnly scoring. The canonical entry honestly carries none.
    /// </summary>
    [Fact]
    public async Task Handle_CleanParse_ExperienceDescriptionIsNull_RawTextNeverPromoted()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        await CreateSut(db).Handle(Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        var exp = db.Resumes.Local.ShouldHaveSingleItem()
            .MasterVersion.Content.Experiences.ShouldHaveSingleItem();
        exp.Description.ShouldBeNull();
    }

    // ===============================================================
    // Tier 1 policy gates — each leaves the artifact pending, untouched
    // ===============================================================

    [Fact]
    public async Task Handle_PnrFlaggedParse_LeftPendingPersonnummerPresent_NothingMutated()
    {
        var flagged = PersonnummerScanOutcome.FromMatches(
            PersonnummerScanner.Scan($"Pnr {ValidPersonnummer} i CV."));
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId, pnr: flagged);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        await AssertLeftPendingAsync(db, result, parsed, AutoPromoteBlockReason.PersonnummerPresent);
    }

    /// <summary>
    /// The #844 carrier rule, auto-promote side: user-promote may promote past a preamble
    /// because the USER approved content that leaves it unadopted — auto-promote has no
    /// user in the loop, so a preamble-carrying parse must never be silently promoted
    /// (ADR 0109: only the user classifies unheaded text; the 5c affordance owns adoption).
    /// </summary>
    [Fact]
    public async Task Handle_PreambleCarryingParse_LeftPendingUnclassifiedPreamble()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(
            db, _userId, content: CleanParsedContent(preamble: "Driven utvecklare nära produktionen."));

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        await AssertLeftPendingAsync(db, result, parsed, AutoPromoteBlockReason.UnclassifiedPreamble);
    }

    /// <summary>Condition 3 is the parser's OWN cleanliness verdict (RequiresManualReview —
    /// anything below Confident), not a weaker "not Failed": a Degraded parse is one the
    /// parser itself says needs review, and auto-promoting it would push a low-confidence
    /// PII extraction to canonical with nobody looking (CTO-bind R3).</summary>
    [Fact]
    public async Task Handle_DegradedParse_LeftPendingParseNotConfident()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId, confidence: Degraded());

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        await AssertLeftPendingAsync(db, result, parsed, AutoPromoteBlockReason.ParseNotConfident);
    }

    [Fact]
    public async Task Handle_FailedExtraction_LeftPendingParseNotConfident()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(
            db, _userId,
            content: ParsedResumeContent.Empty,
            confidence: ParseConfidence.Failed(ParseFallbackReason.ExtractionFailed));

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        await AssertLeftPendingAsync(db, result, parsed, AutoPromoteBlockReason.ParseNotConfident);
    }

    /// <summary>The bound gate ORDER (CTO §2: highest PII priority first) is behavior, not
    /// style: a parse tripping ALL THREE gates must report the personnummer — the most
    /// sensitive blocker — to telemetry/copy, never the confidence verdict. A reorder of
    /// the three ifs would survive every single-gate test; this one catches it.</summary>
    [Fact]
    public async Task Handle_ParseTripsAllThreeGates_ReportsPersonnummerPresent_HighestPriorityFirst()
    {
        var flagged = PersonnummerScanOutcome.FromMatches(
            PersonnummerScanner.Scan($"Pnr {ValidPersonnummer} i CV."));
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(
            db, _userId,
            content: CleanParsedContent(preamble: "Driven utvecklare."),
            confidence: Degraded(),
            pnr: flagged);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        await AssertLeftPendingAsync(db, result, parsed, AutoPromoteBlockReason.PersonnummerPresent);
    }

    /// <summary>Second rung of the order: with no personnummer, the Klas-bound preamble
    /// rule outranks the confidence verdict.</summary>
    [Fact]
    public async Task Handle_PreambleAndDegraded_ReportsUnclassifiedPreamble_BeforeConfidence()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(
            db, _userId,
            content: CleanParsedContent(preamble: "Driven utvecklare."),
            confidence: Degraded());

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        await AssertLeftPendingAsync(db, result, parsed, AutoPromoteBlockReason.UnclassifiedPreamble);
    }

    // ===============================================================
    // Tier 2 — buildability through the ONE aggregate authority
    // ===============================================================

    /// <summary>A confident parse can still be un-buildable (the confidence is a section
    /// verdict, not a per-entry field guarantee): an entry with no organization fails
    /// CreateFromParsed's ValidateContent, and the honest disposition is "review", never a
    /// 400 — the user submitted nothing.</summary>
    [Fact]
    public async Task Handle_ExperienceMissingOrganization_LeftPendingIncompleteContent()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(
            db, _userId,
            content: CleanParsedContent(
                experience: [new ParsedExperience("Backend-utvecklare", null, "2019–2022", "raw")]));

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        await AssertLeftPendingAsync(db, result, parsed, AutoPromoteBlockReason.IncompleteContent);
    }

    /// <summary>The projection never truncates: an over-cap period string must reach
    /// ValidateContent verbatim and bounce the parse to review — a silent shorten would
    /// promote a CV that says something different from the file.</summary>
    [Fact]
    public async Task Handle_OverlongPeriodString_LeftPendingIncompleteContent_NeverTruncated()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(
            db, _userId,
            content: CleanParsedContent(
                experience: [new ParsedExperience("Utvecklare", "Beta AB", new string('x', 101), "raw")]));

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        await AssertLeftPendingAsync(db, result, parsed, AutoPromoteBlockReason.IncompleteContent);
    }

    /// <summary>
    /// Defense-in-depth beyond the Tier-1 artifact flag: the import scan covered the FILE's
    /// text, but the composition adds the account display name — the shared guard on the
    /// composed DTO is what catches a personnummer riding THERE. Same honest disposition.
    /// </summary>
    [Fact]
    public async Task Handle_PnrInAccountDisplayName_LeftPendingPersonnummerPresent()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(
            db, _userId, displayName: $"Anna {ValidPersonnummer}");

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        await AssertLeftPendingAsync(db, result, parsed, AutoPromoteBlockReason.PersonnummerPresent);
    }

    // ===============================================================
    // Genuine faults — auth / not-found / IDOR (Failure, never LeftPending)
    // ===============================================================

    [Fact]
    public async Task Handle_WhenUserIdIsNull_ThrowsUnauthorizedException()
    {
        var db = TestAppDbContextFactory.Create();
        var anon = Substitute.For<ICurrentUser>();
        anon.UserId.Returns((Guid?)null);
        var sut = new AutoPromoteParsedResumeCommandHandler(
            db, anon, FakeDateTimeProvider.Default, _failedAccess, _reconciler,
            _correlationId, _requestContext);

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
        var seeker = JobSeeker.Register(_userId, AccountName, FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            Command(Guid.NewGuid()), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ParsedResume.NotFound");
        _failedAccess.DidNotReceive().LogCrossUserAttempt(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Handle_WhenParsedResumeBelongsToOtherUser_ReturnsNotFound_LogsCrossUser_NoMutation()
    {
        var db = TestAppDbContextFactory.Create();
        var (otherParsed, _) = await SeedOwnedAsync(db, Guid.NewGuid());
        var self = JobSeeker.Register(_userId, "Self", FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(self);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await CreateSut(db).Handle(
            Command(otherParsed.Id.Value), TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ParsedResume.NotFound"); // identical NotFound — no oracle
        _failedAccess.Received(1).LogCrossUserAttempt(
            "ParsedResume", otherParsed.Id.Value, _userId, "AutoPromoteParsedResume");

        otherParsed.Status.ShouldBe(ParsedResumeStatus.PendingReview);
        otherParsed.DeletedAt.ShouldBeNull();
        db.Resumes.Local.ShouldBeEmpty();
        db.AuditLogEntries.Local.ShouldBeEmpty();
    }

    // ===============================================================
    // Reconciler-seed call-site pins (ADR 0093 §D5(b) tripwire contract)
    // ===============================================================

    [Fact]
    public async Task Handle_OnPromoted_RunsReviewReconcileForTheNewResume_WithNoAutoResolve()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var promoted = result.Value.ShouldBeOfType<AutoPromoteOutcome.Promoted>();
        await _reconciler.Received(1).ReconcileAsync(
            Arg.Is<Resume>(r => r.Id.Value == promoted.ResumeId),
            Arg.Is<IReadOnlyCollection<string>>(x => x == null),
            Arg.Any<CancellationToken>());
    }

    // ===============================================================
    // Reconciler-throw atomicity witness — THE Art. 22 witness (CTO bind 2026-07-17,
    // ADR 0093 §D5(b) amendment; resolves the 5a security escalation): the reconciler
    // completes or THROWS — the throw must propagate out of Handle unswallowed
    // (UnitOfWorkBehaviorTests pins the other leg: a throwing next() means the
    // unconditional save never runs), composing to resume + promote + audit discarded
    // TOGETHER. The audit add sits AFTER the reconcile, so on a throw the audit row is
    // never even tracked: a promoted CV persisted WITHOUT its Art. 22 audit row — the
    // escalated anomaly — is unproducible.
    // ===============================================================

    [Fact]
    public async Task Handle_WhenReconcilerThrows_ExceptionPropagates_NothingPersists_NoAuditRow()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);
        _reconciler.ReconcileAsync(
                Arg.Any<Resume>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(new InvalidOperationException("boom")));

        await Should.ThrowAsync<InvalidOperationException>(
            () => CreateSut(db).Handle(
                Command(parsed.Id.Value), TestContext.Current.CancellationToken).AsTask());

        // The throw precedes the audit add — no audit row is ever tracked, so the
        // rolled-back unit can never strand a promote without its audit (Art. 22).
        db.AuditLogEntries.Local.ShouldBeEmpty();

        // Consistency backstop, not the atomicity proof (this test bypasses the
        // pipeline and never saves, so these hold on both paths — the discriminating
        // pin above is the Local-empty audit assert): no Resume row, the artifact
        // still PendingReview, not soft-deleted.
        (await db.Resumes.AnyAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        var stored = await db.ParsedResumes.AsNoTracking()
            .SingleAsync(r => r.Id == parsed.Id, TestContext.Current.CancellationToken);
        stored.Status.ShouldBe(ParsedResumeStatus.PendingReview);
        stored.DeletedAt.ShouldBeNull();
    }

    // ===============================================================
    // Art. 22 audit — distinct event, Promoted branch ONLY
    // ===============================================================

    [Fact]
    public async Task Handle_OnPromoted_WritesDistinctAuditRow_InSameTransaction()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var promoted = result.Value.ShouldBeOfType<AutoPromoteOutcome.Promoted>();

        var entry = db.AuditLogEntries.Local.ShouldHaveSingleItem();
        entry.EventType.ShouldBe(AutoPromoteParsedResumeCommand.AuditEventType);
        entry.EventType.ShouldNotBe("Resume.PromotedFromParsed"); // machine ≠ human provenance
        entry.AggregateType.ShouldBe("Resume");
        entry.AggregateId.ShouldBe(promoted.ResumeId);
        entry.UserId.ShouldBe(_userId);
        entry.CorrelationId.ShouldBe(_correlation);
        entry.IpAddress.ShouldBe("203.0.113.7");
        entry.UserAgent.ShouldBe("test-agent");
        entry.OccurredAt.ShouldBe(FakeDateTimeProvider.Default.UtcNow);
    }

    // (Each LeftPending test above asserts db.AuditLogEntries.Local is empty via the shared
    // helper — a pending outcome must never leave a promote row, §5.)

    // ===============================================================
    // Name override (the 5c form slot)
    // ===============================================================

    [Fact]
    public async Task Handle_NameOverrideProvided_UsedForResumeNameAndFullName()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value, nameOverride: "  Mitt CV-namn  "),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var resume = db.Resumes.Local.ShouldHaveSingleItem();
        resume.Name.ShouldBe("Mitt CV-namn"); // trimmed
        resume.MasterVersion.Content.PersonalInfo.FullName.ShouldBe("Mitt CV-namn");
    }

    [Fact]
    public async Task Handle_WhitespaceNameOverride_FallsBackToAccountDisplayName()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value, nameOverride: "   "), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        db.Resumes.Local.ShouldHaveSingleItem().Name.ShouldBe(AccountName);
    }
}
