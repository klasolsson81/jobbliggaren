using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Resumes.Commands.AutoPromoteParsedResume;
using Jobbliggaren.Application.Resumes.Common;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Commands.AutoPromoteParsedResume;

// CV-pivot PR 5a (CTO-bind 2026-07-17) — the "spara direkt" auto-promote of a CLEAN
// PendingReview ParsedResume, verbatim, no synthesis. The handler orchestrates: auth →
// owner {Id, DisplayName} projection → owner-scoped tracked load (IDOR fail-closed, parity
// PromoteParsedResume) → TWO policy gates (pnr → extraction failure; #1060 retired the
// preamble gate and narrowed confidence to Failed) → verbatim
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
        Infrastructure.Persistence.AppDbContext db,
        ILogger<AutoPromoteParsedResumeCommandHandler>? logger = null) =>
        new(db, _currentUser, FakeDateTimeProvider.Default, _failedAccess, _reconciler,
            _correlationId, _requestContext,
            logger ?? NullLogger<AutoPromoteParsedResumeCommandHandler>.Instance);

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
        PersonnummerScanOutcome? pnr = null,
        string sourceFileName = "anna-cv.pdf") =>
        ParsedResume.Create(
            owner, sourceFileName, "application/pdf", ResumeLanguage.Sv,
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
        string displayName = AccountName,
        string sourceFileName = "anna-cv.pdf")
    {
        var seeker = JobSeeker.Register(userId, displayName, FakeDateTimeProvider.Default).Value;
        db.JobSeekers.Add(seeker);
        var parsed = BuildParsed(seeker.Id, content, confidence, pnr, sourceFileName);
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
    /// The Klas-bound name rule: the canonical CV carries the ACCOUNT holder's name as
    /// PersonalInfo.FullName — never the name the FILE claims. If this goes red, an
    /// uploaded document has started deciding who the user is.
    ///
    /// #1060 split this from the LABEL. Until then one string fed both, so naming the CV
    /// "Backend-CV 2026" printed that where the person's name belongs, and accepting the
    /// suggested account name labelled every import identically. They are also in different
    /// data-protection classes — Resume.Name is a plaintext column that surfaces in lists
    /// (list + detail DTOs, both owner-scoped), PersonalInfo.FullName rides the DEK-encrypted
    /// shadow.
    /// </summary>
    [Fact]
    public async Task Handle_PersonNameIsTheAccountDisplayName_NeverTheParsedContactName()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var resume = db.Resumes.Local.ShouldHaveSingleItem();
        resume.MasterVersion.Content.PersonalInfo.FullName.ShouldBe(AccountName);
        resume.MasterVersion.Content.PersonalInfo.FullName.ShouldNotBe(ParsedContactName);
    }

    /// <summary>
    /// With no user-typed label the CV gets a GENERATED, non-PII name — not the account name
    /// and not the file name (CTO-bind D5-REBIND-2). The account name would put the person's
    /// name back into the plaintext column for every user who never edits it; the file name
    /// was refused for `Resume` by ADR 0096 D-B (PII-near) and would additionally outlive the
    /// staging-retention rule written for `SourceFileName`.
    /// </summary>
    [Fact]
    public async Task Handle_NoNameOverride_GeneratesANonPersonalDatedLabel()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId); // fixture file: "anna-cv.pdf"

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var resume = db.Resumes.Local.ShouldHaveSingleItem();
        resume.Name.ShouldBe(
            $"Importerat CV {FakeDateTimeProvider.Default.UtcNow:yyyy-MM-dd}");
        resume.Name.ShouldNotBe(AccountName);
        resume.Name.ShouldNotContain("anna-cv"); // never the file name either
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
    /// #1060 D1.2 — the RETIRED preamble gate, pinned by its inverse. Text above the first
    /// heading is the most common Swedish CV layout, and blocking on it enforced nothing
    /// ADR 0109 forbids: §1 forbids MINTING section identity, not promoting. Nothing is
    /// minted here — the mapper still never maps <c>Preamble</c> into <c>Summary</c> or a
    /// <c>Section</c> (pinned separately below) — and nothing is dropped, because the
    /// carrier is read back past the soft-delete on the promoted CV's review surface.
    ///
    /// <para>If this goes red the gate is back, and with it the P1 Klas filed: his own CV
    /// came back "Kräver åtgärd" and <c>CV-varianter</c> said "Inga CV ännu".</para>
    /// </summary>
    [Fact]
    public async Task Handle_PreambleCarryingParse_Promotes_TheGateIsRetired()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(
            db, _userId, content: CleanParsedContent(preamble: "Driven utvecklare nära produktionen."));

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeOfType<AutoPromoteOutcome.Promoted>();
        parsed.Status.ShouldBe(ParsedResumeStatus.Promoted);
    }

    /// <summary>
    /// #1060 D1.2 — the prohibition the retired gate was standing in for, enforced where it
    /// belongs. The preamble promotes PAST the mapper, never THROUGH it: an un-headed region
    /// must not become the canonical CV's <c>Summary</c> (that mints "this IS your profile",
    /// ADR 0109 §1's one absolute prohibition) and must not become a <c>Section</c> (which
    /// would need a heading the user never wrote, and would render in <c>/render</c> and the
    /// ATS view — a section the user did not write, in a document she sends to employers).
    ///
    /// <para>This is the assertion that makes retiring the gate safe. Without it, the gate's
    /// removal and a one-line mapper change would silently become adoption.</para>
    /// </summary>
    [Fact]
    public async Task Handle_PreambleCarryingParse_NeverAdoptsItAsSummaryOrSection()
    {
        const string preamble = "Driven utvecklare nära produktionen.";
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(
            db, _userId, content: CleanParsedContent(preamble: preamble));

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var content = db.Resumes.Local.ShouldHaveSingleItem().MasterVersion.Content;

        // It IS carried — under its own name. Asserting only the two negatives below would
        // pass just as happily on a promote that dropped the text entirely, which is the
        // failure this whole PR exists to prevent and which a mutation run confirmed no other
        // test in this file catches.
        content.Preamble.ShouldBe(preamble);

        // The file's OWN "Profil" block still lands in Summary, verbatim — the preamble does not.
        content.Summary.ShouldBe("Erfaren backend-utvecklare.");
        content.Sections.ShouldAllBe(s => s.Heading != preamble);
        content.Sections.SelectMany(s => s.Entries).SelectMany(e => e.Lines)
            .ShouldNotContain(preamble);
    }

    /// <summary>
    /// #1060 D1.3 — the confidence gate NARROWED to <c>Failed</c>, and this reverses the 5a
    /// bind's R3. A <c>Degraded</c> parse means the parser found something and is honest that
    /// the document was messy; under ADR 0112 the reviewer IS the product, so that is exactly
    /// the CV the reviewer has most to say about, and blocking it gave the least product to
    /// the user who needs it most. R3's PII ground does not reach this: <c>Personnummer.Found</c>
    /// and the DQ6 guard are the PII controls, they are unconditional, and both still run.
    /// </summary>
    [Fact]
    public async Task Handle_DegradedParse_Promotes_TheGateNarrowedToFailed()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId, confidence: Degraded());

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeOfType<AutoPromoteOutcome.Promoted>();
        parsed.Status.ShouldBe(ParsedResumeStatus.Promoted);
    }

    /// <summary>The arm that must NOT be narrowed away: extraction produced nothing usable,
    /// so promoting would build a canonical CV out of the account display name and nothing
    /// else — a CV that says LESS than the file did, the same dishonesty class as dropping
    /// (ADR 0109 §3). Narrowing this too would delete a working signal.</summary>
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
    /// style: a parse tripping BOTH surviving gates must report the personnummer — the most
    /// sensitive blocker — to telemetry/copy, never the confidence verdict. A reorder of the
    /// two ifs would survive every single-gate test; this one catches it.</summary>
    [Fact]
    public async Task Handle_ParseTripsBothGates_ReportsPersonnummerPresent_HighestPriorityFirst()
    {
        var flagged = PersonnummerScanOutcome.FromMatches(
            PersonnummerScanner.Scan($"Pnr {ValidPersonnummer} i CV."));
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(
            db, _userId,
            content: ParsedResumeContent.Empty,
            confidence: ParseConfidence.Failed(ParseFallbackReason.ExtractionFailed),
            pnr: flagged);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        await AssertLeftPendingAsync(db, result, parsed, AutoPromoteBlockReason.PersonnummerPresent);
    }

    /// <summary>
    /// #1060's own reproduction, and the reason B's two gate changes are ONE PR. This is the
    /// shape corpus rows 10 (`pdf-nonsequential-decorative`) and 11 (`pdf-headingless`) carry:
    /// a preamble AND a Degraded parse. Retiring the preamble gate alone leaves them blocked
    /// one rung lower on <c>ParseNotConfident</c>, so the P1 would read as unfixed on exactly
    /// the documents it was filed about. Both changes are jointly load-bearing here.
    /// </summary>
    [Fact]
    public async Task Handle_PreambleAndDegraded_Promotes_BothGateChangesAreLoadBearing()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(
            db, _userId,
            content: CleanParsedContent(preamble: "Driven utvecklare."),
            confidence: Degraded());

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeOfType<AutoPromoteOutcome.Promoted>();
        parsed.Status.ShouldBe(ParsedResumeStatus.Promoted);
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

    /// <summary>
    /// The STRUCTURED-PROPERTY pin for <c>{BlockDetail}</c> (#1060 D3(β) PR 2). It lives here
    /// rather than in <c>StructuredPropertyNameContractTests</c> because this is where the
    /// writer is testable with real fixtures; that class's docblock names it.
    ///
    /// <para><b>What breaks without it.</b> MEL takes a property's name from the placeholder
    /// TOKEN, and the consumer — <c>Jobbliggaren.QA.Corpus</c>'s <c>CvChainProbe</c> — looks
    /// <c>BlockDetail</c> up by that exact string, because <c>AutoPromoteGateVerdict</c> is
    /// <c>internal</c> and the corpus is not in <c>InternalsVisibleTo</c>. A rename does not
    /// break its build: the lookup misses, the column prints <c>—</c> on every row, and the
    /// layout baseline reads as "no domain code exists" while the handler is emitting one. A
    /// false clean produced by a silent lookup miss is the class PR 1 was opened for.</para>
    /// </summary>
    [Fact]
    public async Task Handle_LeftPending_EmitsTheBlockDetailPropertyTheCorpusReadsByName()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(
            db, _userId,
            content: CleanParsedContent(
                experience: [new ParsedExperience("Backend-utvecklare", null, "2019–2022", "raw")]));
        var recorder = new RecordingLogger<AutoPromoteParsedResumeCommandHandler>();

        await CreateSut(db, recorder).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        // `Latest` is `Records[^1]`, and the corpus reader indexes the same way. Both rest on
        // this handler emitting exactly ONE line per LeftPending; nothing said so until here.
        recorder.Records.Count.ShouldBe(1);

        var properties = recorder.Latest.Properties;
        properties.Select(p => p.Key).ShouldContain(
            "BlockDetail",
            "Jobbliggaren.QA.Corpus/Harness/CvChainProbe.cs reads @Properties['BlockDetail'] by "
            + "exactly this spelling and cannot see the verdict type. If the placeholder was "
            + "renamed, the corpus's Domain-code column now prints an em-dash on every row "
            + "instead of going red — fix the reader in the same commit, or restore the token. "
            + "(MEL takes the property name from the {Placeholder} token, not the prose.)");
        properties.Single(p => p.Key == "BlockDetail").Value
            .ShouldBe("Resume.ExperienceCompanyRequired");
    }

    /// <summary>The same property on a POLICY block. It is present and null — not absent — so a
    /// reader can tell "this block was not a Domain refusal" from "the instrument lost the
    /// value", and so the corpus's em-dash means the first of those.</summary>
    [Fact]
    public async Task Handle_PolicyBlock_EmitsBlockDetailAsNullRatherThanOmittingIt()
    {
        var db = TestAppDbContextFactory.Create();
        var flagged = PersonnummerScanOutcome.FromMatches(
            PersonnummerScanner.Scan($"Pnr {ValidPersonnummer} i CV."));
        var (parsed, _) = await SeedOwnedAsync(db, _userId, pnr: flagged);
        var recorder = new RecordingLogger<AutoPromoteParsedResumeCommandHandler>();

        var result = await CreateSut(db, recorder).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        // Assert the arm was actually reached first. Without this the null below is satisfied by
        // any outcome that logs nothing relevant — including a future refactor where this fixture
        // stops blocking at all, which would make the test agree with itself about nothing.
        result.Value.ShouldBeOfType<AutoPromoteOutcome.LeftPending>()
            .Reason.ShouldBe(AutoPromoteBlockReason.PersonnummerPresent);

        recorder.Latest.Properties.Select(p => p.Key).ShouldContain("BlockDetail");
        recorder.Latest.Properties.Single(p => p.Key == "BlockDetail").Value.ShouldBeNull();
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
    /// composed DTO is what catches a personnummer riding THERE.
    ///
    /// <para>Its OWN token since #1060 PR C (CTO-bind D2). The file is clean on this path, so
    /// reporting it as <c>PersonnummerPresent</c> drove copy telling the user to remove a number
    /// from a file that has none — a mis-reported verdict on the product's highest-priority PII
    /// rule, and a loop with no exit (the fix is under Inställningar).</para>
    /// </summary>
    [Fact]
    public async Task Handle_PnrInAccountDisplayName_LeftPendingPersonnummerInAccountName()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(
            db, _userId, displayName: $"Anna {ValidPersonnummer}");

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        await AssertLeftPendingAsync(
            db, result, parsed, AutoPromoteBlockReason.PersonnummerInAccountName);
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
            _correlationId, _requestContext,
            NullLogger<AutoPromoteParsedResumeCommandHandler>.Instance);

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
            Arg.Is<Resume>(r => r != null && r.Id.Value == promoted.ResumeId),
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

    /// <summary>
    /// The form field sets the LABEL only. This is the defect #1060 reports, inverted into a
    /// pin: a user who labels the CV "Backend-CV 2026" must not end up with that printed
    /// where her name belongs.
    /// </summary>
    [Fact]
    public async Task Handle_NameOverrideSetsTheLabelOnly_PersonNameStaysTheAccountName()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value, nameOverride: "  Backend-CV 2026  "),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var resume = db.Resumes.Local.ShouldHaveSingleItem();
        resume.Name.ShouldBe("Backend-CV 2026"); // trimmed
        resume.MasterVersion.Content.PersonalInfo.FullName.ShouldBe(AccountName);
    }

    [Fact]
    public async Task Handle_WhitespaceNameOverride_FallsBackToTheGeneratedLabel()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value, nameOverride: "   "), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        db.Resumes.Local.ShouldHaveSingleItem().Name.ShouldBe(
            $"Importerat CV {FakeDateTimeProvider.Default.UtcNow:yyyy-MM-dd}");
    }

    /// <summary>
    /// A personnummer in the FILE NAME must NOT block promote. The rule is written down in
    /// `PersonnummerScanOutcome` ("a filename-only detection does NOT set Found, so it does
    /// NOT block promotion — the filename never reaches the canonical Resume") and B4's
    /// Warn-instead-of-Fail rests on it. An earlier draft of #1060 derived the label from the
    /// file name and silently falsified both halves; this pins that it cannot come back.
    /// </summary>
    [Fact]
    public async Task Handle_PersonnummerOnlyInTheFileName_StillPromotes_LabelNeverCarriesIt()
    {
        var db = TestAppDbContextFactory.Create();
        // The oracle must run the shape production emits: ImportResumeCommandHandler computes
        // FoundInFileName from the ORIGINAL filename before ParsedResume.Create masks it, so a
        // "CV_811218-9876.pdf" upload really does arrive with the flag SET and the body clean.
        // With the default None the "StillPromotes" half would assert non-blocking against an
        // outcome that cannot block, and would stay green if the flag ever gated promote.
        var (parsed, _) = await SeedOwnedAsync(
            db, _userId,
            pnr: PersonnummerScanOutcome.FromMatches([], foundInFileName: true),
            sourceFileName: $"CV_{ValidPersonnummer}.pdf");

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeOfType<AutoPromoteOutcome.Promoted>();
        var resume = db.Resumes.Local.ShouldHaveSingleItem();
        resume.Name.ShouldBe(
            $"Importerat CV {FakeDateTimeProvider.Default.UtcNow:yyyy-MM-dd}");
    }

    /// <summary>
    /// A personnummer typed into the LABEL is reported as PersonnummerPresent, not as
    /// IncompleteContent. Resume.ValidateName refuses it either way, but as a buildability
    /// failure — which would mis-report the reason to the user (§5: a verdict is never
    /// mis-reported). The DERIVED label cannot trip this (ParsedResume.Create masks the file
    /// name), so this covers the user-typed path specifically.
    /// </summary>
    [Fact]
    public async Task Handle_PersonnummerInTheLabel_ReportsPersonnummerPresent_NotIncompleteContent()
    {
        var db = TestAppDbContextFactory.Create();
        var (parsed, _) = await SeedOwnedAsync(db, _userId);

        var result = await CreateSut(db).Handle(
            Command(parsed.Id.Value, nameOverride: $"CV {ValidPersonnummer}"),
            TestContext.Current.CancellationToken);

        await AssertLeftPendingAsync(
            db, result, parsed, AutoPromoteBlockReason.PersonnummerPresent);
    }
}
