using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Domain.Resumes.Parsing.Events;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Resumes.Parsing;

// Fas 4 STEG 8 (F4-8, ADR 0074) — the ParsedResume STAGING aggregate (CTO Decision 1
// = Variant A). Its load-bearing, deliberately-LOOSER invariant: "a parse always
// materialises, even a degraded or personnummer-flagged one" — distinct from the
// strict canonical Resume (whose ValidateContent rejects a degraded parse). The
// aggregate validates only structural preconditions; content completeness is NOT a
// precondition (OQ5). Promotion is BLOCKED while a personnummer is flagged
// (Invariant 1) — owned here so F4-9 enforces it through the aggregate.
//
// SPEC-DRIVEN.
public class ParsedResumeTests
{
    private sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private static readonly DateTimeOffset Now =
        new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly IDateTimeProvider Clock = new FixedClock(Now);

    private static readonly JobSeekerId Owner = JobSeekerId.New();

    private static ParsedResumeContent ConfidentContent() =>
        new(
            new ParsedContact("Anna Andersson", "anna@example.com", "070-1234567", "Stockholm"),
            profile: "Erfaren backend-utvecklare.",
            experience:
            [
                new ParsedExperience("Backend-utvecklare", "Acme AB", "2021–2024", "Backend-utvecklare, Acme AB, 2021–2024"),
            ],
            education:
            [
                new ParsedEducation("KTH", "Civilingenjör", "2016–2021", "KTH, Civilingenjör, 2016–2021"),
            ],
            skills: ["C#", "PostgreSQL"],
            languages: ["Svenska", "Engelska"]);

    private static ParseConfidence ConfidentConfidence() =>
        ParseConfidence.FromSections(
        [
            new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident, []),
            new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident, []),
        ]);

    private static Result<ParsedResume> CreateConfident() =>
        ParsedResume.Create(
            Owner,
            "anna-cv.pdf",
            "application/pdf",
            ResumeLanguage.Sv,
            ConfidentContent(),
            "Anna Andersson\nanna@example.com\nBackend-utvecklare, Acme AB",
            ConfidentConfidence(),
            PersonnummerScanOutcome.None,
            [],
            Clock);

    // ===============================================================
    // Create — happy path (confident parse)
    // ===============================================================

    [Fact]
    public void Create_ConfidentParse_ReturnsSuccess_StatusPendingReview()
    {
        var result = CreateConfident();

        result.IsSuccess.ShouldBeTrue();
        var parsed = result.Value;
        parsed.Status.ShouldBe(ParsedResumeStatus.PendingReview);
        parsed.JobSeekerId.ShouldBe(Owner);
        parsed.DetectedLanguage.ShouldBe(ResumeLanguage.Sv);
        parsed.Confidence.Overall.ShouldBe(OverallConfidenceLevel.Confident);
        parsed.CreatedAt.ShouldBe(Now);
        parsed.UpdatedAt.ShouldBe(Now);
        parsed.DeletedAt.ShouldBeNull();
    }

    [Fact]
    public void Create_TrimsSourceMetadata()
    {
        var result = ParsedResume.Create(
            Owner, "  cv.pdf  ", "  application/pdf  ", ResumeLanguage.Sv,
            ParsedResumeContent.Empty, "raw", ParseConfidence.Failed(ParseFallbackReason.ExtractionFailed),
            PersonnummerScanOutcome.None, [], Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SourceFileName.ShouldBe("cv.pdf");
        result.Value.SourceContentType.ShouldBe("application/pdf");
    }

    [Fact]
    public void Create_CarriesOccupationProposals()
    {
        var proposals = new[]
        {
            new ProposedOccupation("q8wL_kdi_WaW", "Systemutvecklare", "Backend-utvecklare"),
        };

        var result = ParsedResume.Create(
            Owner, "cv.pdf", "application/pdf", ResumeLanguage.Sv,
            ConfidentContent(), "raw", ConfidentConfidence(),
            PersonnummerScanOutcome.None, proposals, Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OccupationProposals.Count.ShouldBe(1);
        result.Value.OccupationProposals[0].Label.ShouldBe("Systemutvecklare");
    }

    // ===============================================================
    // Create — skill proposals (ADR 0079 STEG 3). The optional trailing
    // skillProposals param (after clock) carries CV-resolved JobTech skills.
    // ===============================================================

    [Fact]
    public void Create_CarriesSkillProposals()
    {
        var skillProposals = new[]
        {
            new ProposedSkill("k1A2_b3C4_d5E", "C#"),
            new ProposedSkill("m6N7_o8P9_q0R", "PostgreSQL"),
        };

        var result = ParsedResume.Create(
            Owner, "cv.pdf", "application/pdf", ResumeLanguage.Sv,
            ConfidentContent(), "raw", ConfidentConfidence(),
            PersonnummerScanOutcome.None, [], Clock, skillProposals);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SkillProposals.Count.ShouldBe(2);
        result.Value.SkillProposals[0].ConceptId.ShouldBe("k1A2_b3C4_d5E");
        result.Value.SkillProposals[0].Label.ShouldBe("C#");
        result.Value.SkillProposals[1].Label.ShouldBe("PostgreSQL");
    }

    [Fact]
    public void Create_WithoutSkillProposals_OmittedParam_YieldsEmptySkillProposals()
    {
        // The optional trailing skillProposals param defaults to null → empty (additive, so the
        // ~30 existing Create callers stay unchanged). Omitting it must not throw.
        var result = ParsedResume.Create(
            Owner, "cv.pdf", "application/pdf", ResumeLanguage.Sv,
            ConfidentContent(), "raw", ConfidentConfidence(),
            PersonnummerScanOutcome.None, [], Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SkillProposals.ShouldBeEmpty();
    }

    [Fact]
    public void Create_SkillAndOccupationProposals_AreIndependent()
    {
        // Occupation proposals and skill proposals are carried on separate collections — a CV
        // can have one without the other, and each maps only to its own read property.
        var occupations = new[]
        {
            new ProposedOccupation("q8wL_kdi_WaW", "Systemutvecklare", "Backend-utvecklare"),
        };
        var skills = new[]
        {
            new ProposedSkill("k1A2_b3C4_d5E", "C#"),
        };

        var result = ParsedResume.Create(
            Owner, "cv.pdf", "application/pdf", ResumeLanguage.Sv,
            ConfidentContent(), "raw", ConfidentConfidence(),
            PersonnummerScanOutcome.None, occupations, Clock, skills);

        result.IsSuccess.ShouldBeTrue();
        var parsed = result.Value;
        parsed.OccupationProposals.Count.ShouldBe(1);
        parsed.OccupationProposals[0].ConceptId.ShouldBe("q8wL_kdi_WaW");
        parsed.SkillProposals.Count.ShouldBe(1);
        parsed.SkillProposals[0].ConceptId.ShouldBe("k1A2_b3C4_d5E");
    }

    [Fact]
    public void Create_OnlySkillProposals_NoOccupationProposals_OccupationProposalsEmpty()
    {
        var skills = new[]
        {
            new ProposedSkill("k1A2_b3C4_d5E", "C#"),
        };

        var result = ParsedResume.Create(
            Owner, "cv.pdf", "application/pdf", ResumeLanguage.Sv,
            ConfidentContent(), "raw", ConfidentConfidence(),
            PersonnummerScanOutcome.None, [], Clock, skills);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SkillProposals.Count.ShouldBe(1);
        result.Value.OccupationProposals.ShouldBeEmpty();
    }

    // ===============================================================
    // Create — Variant A: a DEGRADED/incomplete parse IS constructible
    // (the contrast with the strict canonical Resume).
    // ===============================================================

    [Fact]
    public void Create_DegradedEmptyContentFailedConfidence_IsConstructible()
    {
        // Empty content + Failed confidence — the strict Resume would reject this;
        // the staging aggregate accepts it (a parse always materialises, OQ5).
        var result = ParsedResume.Create(
            Owner,
            "scanned.pdf",
            "application/pdf",
            ResumeLanguage.Sv,
            ParsedResumeContent.Empty,
            rawText: string.Empty,
            ParseConfidence.Failed(ParseFallbackReason.ScannedImageNoText),
            PersonnummerScanOutcome.None,
            [],
            Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Confidence.Overall.ShouldBe(OverallConfidenceLevel.Failed);
        result.Value.Status.ShouldBe(ParsedResumeStatus.PendingReview);
    }

    // ===============================================================
    // Create — structural validation failures
    // ===============================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyOrWhitespaceFileName_ReturnsFailure(string fileName)
    {
        var result = ParsedResume.Create(
            Owner, fileName, "application/pdf", ResumeLanguage.Sv,
            ConfidentContent(), "raw", ConfidentConfidence(),
            PersonnummerScanOutcome.None, [], Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ParsedResume.SourceFileNameRequired");
    }

    [Fact]
    public void Create_OversizeFileName_ReturnsFailure()
    {
        var fileName = new string('a', 401);

        var result = ParsedResume.Create(
            Owner, fileName, "application/pdf", ResumeLanguage.Sv,
            ConfidentContent(), "raw", ConfidentConfidence(),
            PersonnummerScanOutcome.None, [], Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ParsedResume.SourceFileNameTooLong");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyContentType_ReturnsFailure(string contentType)
    {
        var result = ParsedResume.Create(
            Owner, "cv.pdf", contentType, ResumeLanguage.Sv,
            ConfidentContent(), "raw", ConfidentConfidence(),
            PersonnummerScanOutcome.None, [], Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ParsedResume.SourceContentTypeRequired");
    }

    [Fact]
    public void Create_DefaultJobSeekerId_ReturnsFailure()
    {
        var result = ParsedResume.Create(
            default, "cv.pdf", "application/pdf", ResumeLanguage.Sv,
            ConfidentContent(), "raw", ConfidentConfidence(),
            PersonnummerScanOutcome.None, [], Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ParsedResume.JobSeekerIdRequired");
    }

    // ===============================================================
    // Create — domain event (non-PII fields only)
    // ===============================================================

    [Fact]
    public void Create_RaisesParsedResumeImportedDomainEvent_WithNonPiiFields()
    {
        var result = ParsedResume.Create(
            Owner, "cv.pdf", "application/pdf", ResumeLanguage.Sv,
            ConfidentContent(), "Anna Andersson raw text",
            ConfidentConfidence(),
            PersonnummerScanOutcome.None, [], Clock);

        var parsed = result.Value;
        var evt = parsed.DomainEvents
            .OfType<ParsedResumeImportedDomainEvent>()
            .ShouldHaveSingleItem();

        evt.ParsedResumeId.ShouldBe(parsed.Id);
        evt.JobSeekerId.ShouldBe(Owner);
        evt.OverallConfidence.ShouldBe(OverallConfidenceLevel.Confident);
        evt.PersonnummerFlagged.ShouldBeFalse();
        evt.OccurredAt.ShouldBe(Now);
    }

    [Fact]
    public void Create_WithPersonnummerFlagged_EventReportsFlaggedTrue()
    {
        var flagged = PersonnummerScanOutcome.FromMatches(
            PersonnummerScanner.Scan("Pnr 811218-9876 i CV."));

        var result = ParsedResume.Create(
            Owner, "cv.pdf", "application/pdf", ResumeLanguage.Sv,
            ConfidentContent(), "raw", ConfidentConfidence(),
            flagged, [], Clock);

        var evt = result.Value.DomainEvents
            .OfType<ParsedResumeImportedDomainEvent>()
            .ShouldHaveSingleItem();
        evt.PersonnummerFlagged.ShouldBeTrue();
    }

    // ===============================================================
    // EnsureReadyForPromotion — the personnummer promote-block (Invariant 1)
    // ===============================================================

    [Fact]
    public void EnsureReadyForPromotion_PendingReviewNoPersonnummer_ReturnsSuccess()
    {
        var parsed = CreateConfident().Value;

        var result = parsed.EnsureReadyForPromotion();

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void EnsureReadyForPromotion_WhenPersonnummerFound_ReturnsFailure()
    {
        var flagged = PersonnummerScanOutcome.FromMatches(
            PersonnummerScanner.Scan("Pnr 811218-9876 i CV."));
        var parsed = ParsedResume.Create(
            Owner, "cv.pdf", "application/pdf", ResumeLanguage.Sv,
            ConfidentContent(), "raw", ConfidentConfidence(),
            flagged, [], Clock).Value;

        var result = parsed.EnsureReadyForPromotion();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ParsedResume.PersonnummerMustBeRemoved");
    }

    [Fact]
    public void EnsureReadyForPromotion_WhenNotPendingReview_ReturnsFailure()
    {
        var parsed = CreateConfident().Value;
        parsed.Discard(Clock); // → Discarded

        var result = parsed.EnsureReadyForPromotion();

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ParsedResume.NotPendingReview");
    }

    // ===============================================================
    // Promote — Fas 4 STEG A PR-2 (the promote transition). Promotion runs
    // EnsureReadyForPromotion() internally: on success the artifact transitions
    // to Promoted, is soft-deleted on promote (retained for audit/sweep, CTO DQ7),
    // and raises ParsedResumePromotedDomainEvent. RED until Promote ships.
    // ===============================================================

    private static readonly DateTimeOffset Later =
        new(2026, 6, 16, 14, 30, 0, TimeSpan.Zero);
    private static readonly IDateTimeProvider LaterClock = new FixedClock(Later);

    [Fact]
    public void Promote_PendingReviewNoPersonnummer_TransitionsToPromoted_SoftDeletes_AdvancesUpdatedAt()
    {
        var parsed = CreateConfident().Value;

        var result = parsed.Promote(LaterClock);

        result.IsSuccess.ShouldBeTrue();
        parsed.Status.ShouldBe(ParsedResumeStatus.Promoted);
        // Soft-delete on promote (CTO DQ7) — retained as Promoted for audit/sweep.
        parsed.DeletedAt.ShouldBe(Later);
        parsed.UpdatedAt.ShouldBe(Later);
    }

    [Fact]
    public void Promote_PendingReviewNoPersonnummer_RaisesParsedResumePromotedDomainEvent_WithIds()
    {
        var parsed = CreateConfident().Value;
        parsed.ClearDomainEvents();

        parsed.Promote(LaterClock);

        var evt = parsed.DomainEvents
            .OfType<ParsedResumePromotedDomainEvent>()
            .ShouldHaveSingleItem();
        evt.ParsedResumeId.ShouldBe(parsed.Id);
        evt.JobSeekerId.ShouldBe(Owner);
        evt.OccurredAt.ShouldBe(Later);
    }

    [Fact]
    public void Promote_WhenPersonnummerFlagged_ReturnsFailure_NoMutation_NoEvent()
    {
        var flagged = PersonnummerScanOutcome.FromMatches(
            PersonnummerScanner.Scan("Pnr 811218-9876 i CV."));
        var parsed = ParsedResume.Create(
            Owner, "cv.pdf", "application/pdf", ResumeLanguage.Sv,
            ConfidentContent(), "raw", ConfidentConfidence(),
            flagged, [], Clock).Value;
        parsed.ClearDomainEvents();

        var result = parsed.Promote(LaterClock);

        // The gate (EnsureReadyForPromotion) result is returned unchanged.
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ParsedResume.PersonnummerMustBeRemoved");
        // No mutation: still PendingReview, not soft-deleted, no promote event.
        parsed.Status.ShouldBe(ParsedResumeStatus.PendingReview);
        parsed.DeletedAt.ShouldBeNull();
        parsed.DomainEvents.OfType<ParsedResumePromotedDomainEvent>().ShouldBeEmpty();
    }

    [Fact]
    public void Promote_WhenAlreadyDiscarded_ReturnsNotPendingReview_NoMutation()
    {
        var parsed = CreateConfident().Value;
        parsed.Discard(Clock); // → Discarded
        parsed.ClearDomainEvents();

        var result = parsed.Promote(LaterClock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ParsedResume.NotPendingReview");
        parsed.Status.ShouldBe(ParsedResumeStatus.Discarded);
        parsed.DomainEvents.OfType<ParsedResumePromotedDomainEvent>().ShouldBeEmpty();
    }

    [Fact]
    public void Promote_IsNotIdempotent_SecondPromoteReturnsNotPendingReview()
    {
        // Idempotency contract: a second Promote after a successful one is rejected
        // by EnsureReadyForPromotion (Status is now Promoted, not PendingReview).
        var parsed = CreateConfident().Value;
        parsed.Promote(LaterClock).IsSuccess.ShouldBeTrue();
        var deletedAtAfterFirst = parsed.DeletedAt;
        parsed.ClearDomainEvents();

        var secondClock = new FixedClock(Later.AddDays(1));
        var result = parsed.Promote(secondClock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ParsedResume.NotPendingReview");
        parsed.Status.ShouldBe(ParsedResumeStatus.Promoted);
        // The second call must not overwrite the soft-delete timestamp or re-raise.
        parsed.DeletedAt.ShouldBe(deletedAtAfterFirst);
        parsed.DomainEvents.OfType<ParsedResumePromotedDomainEvent>().ShouldBeEmpty();
    }

    // ===============================================================
    // Discard — soft delete, idempotent
    // ===============================================================

    [Fact]
    public void Discard_SetsStatusDiscardedAndDeletedAt()
    {
        var parsed = CreateConfident().Value;

        parsed.Discard(Clock);

        parsed.Status.ShouldBe(ParsedResumeStatus.Discarded);
        parsed.DeletedAt.ShouldBe(Now);
        parsed.UpdatedAt.ShouldBe(Now);
    }

    [Fact]
    public void Discard_IsIdempotent()
    {
        var parsed = CreateConfident().Value;
        parsed.Discard(Clock);
        var deletedAtAfterFirst = parsed.DeletedAt;

        // A later clock; a second Discard must not change anything (early return).
        parsed.Discard(new FixedClock(Now.AddDays(1)));

        parsed.Status.ShouldBe(ParsedResumeStatus.Discarded);
        parsed.DeletedAt.ShouldBe(deletedAtAfterFirst);
    }
}
