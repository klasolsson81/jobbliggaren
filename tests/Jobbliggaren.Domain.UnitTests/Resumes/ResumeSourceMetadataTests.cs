using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Events;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Resumes;

// Fas 4b CV-motor v2 PR-3 (issue #652, epic #649, ADR 0096 — CTO-bind D1/D5d/D9). The new
// source-provenance + template-options + one-way-adoption surface on the Resume aggregate.
//
// Origin is set BY CONSTRUCTION (Create => Template, CreateFromParsed => Import) and is
// immutable thereafter — there is no mutator, so "promote sets källa=import" is proven at
// the CreateFromParsed factory, never at a setter. Adopt is one-way (Import-only, Conflict
// on the second call, parity with ParsedResume.Promote). ChangeTemplateOptions is the single
// mutation path for the CvTemplateOptions VO (null / null-member guarded, value-equal no-op).
//
// SPEC-STYLE pins: the assertions describe the intended contract, not a reading of the
// implementation. Result/DomainError idiom — assert result.IsFailure + result.Error.Code
// (+ .Kind where the HTTP-status mapping matters).
public class ResumeSourceMetadataTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly JobSeekerId ValidJobSeekerId = new(Guid.NewGuid());
    private const string ValidName = "Mitt CV";
    private const string ValidFullName = "Klas Olsson";

    // Rich, valid content that passes ValidateContent — drives a non-trivial denormalized
    // projection (LatestRole "Backend-utvecklare", SectionCount 4, TopSkills [C#, PostgreSQL])
    // so the Adopt non-regression pin below is meaningful.
    private static ResumeContent GapFilledContent() => new(
        new PersonalInfo("Anna Andersson", "anna@example.com", "0701234567", "Stockholm"),
        experiences: new[]
        {
            new Experience("Beta AB", "Backend-utvecklare", new DateOnly(2021, 1, 1), null, "Byggde betaltjänster."),
        },
        educations: new[]
        {
            new Education("KTH", "Civilingenjör", new DateOnly(2013, 9, 1), new DateOnly(2018, 6, 1)),
        },
        skills: new[]
        {
            new Skill("C#", 8),
            new Skill("PostgreSQL", 5),
        },
        summary: "Erfaren backend-utvecklare.");

    private static Resume CreateTemplateResume() =>
        Resume.Create(ValidJobSeekerId, ValidName, ValidFullName, Clock).Value;

    private static Resume CreateImportedResume() =>
        Resume.CreateFromParsed(
            ValidJobSeekerId, ValidName, GapFilledContent(),
            new ParsedResumeId(Guid.NewGuid()), Clock).Value;

    // ---------------------------------------------------------------
    // Origin by construction + template-option / adoption defaults
    // ---------------------------------------------------------------

    [Fact]
    public void Create_SetsOriginTemplate_AndDefaultTemplateOptions_AndNotAdopted()
    {
        var result = Resume.Create(ValidJobSeekerId, ValidName, ValidFullName, Clock);

        result.IsSuccess.ShouldBeTrue();
        var resume = result.Value;
        // Create is the in-app/template path (handoff's "mall"-CV).
        resume.Origin.ShouldBe(ResumeSourceOrigin.Template);
        // Every new Resume starts from the handoff-bound defaults.
        resume.TemplateOptions.ShouldBe(CvTemplateOptions.Default);
        // Adoption is a one-way stamp that starts unset.
        resume.AdoptedAt.ShouldBeNull();
        resume.IsAdopted.ShouldBeFalse();
    }

    [Fact]
    public void CreateFromParsed_SetsOriginImport()
    {
        // Promoting a parsed import IS the import path — "promote sets källa=import" is
        // satisfied at the factory, not by a setter (ADR 0096).
        var result = Resume.CreateFromParsed(
            ValidJobSeekerId, ValidName, GapFilledContent(),
            new ParsedResumeId(Guid.NewGuid()), Clock);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Origin.ShouldBe(ResumeSourceOrigin.Import);
        // An imported-but-not-yet-adopted CV.
        result.Value.AdoptedAt.ShouldBeNull();
        result.Value.IsAdopted.ShouldBeFalse();
    }

    // ---------------------------------------------------------------
    // Adopt — one-way adoption stamp (CTO-bind D9)
    // ---------------------------------------------------------------

    [Fact]
    public void Adopt_OnImportedResume_Succeeds()
    {
        var resume = CreateImportedResume();
        resume.ClearDomainEvents();
        // A distinct later clock makes the AdoptedAt stamp + UpdatedAt bump observable.
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(1));

        var result = resume.Adopt(laterClock);

        result.IsSuccess.ShouldBeTrue();
        resume.AdoptedAt.ShouldBe(laterClock.UtcNow);
        resume.IsAdopted.ShouldBeTrue();
        resume.UpdatedAt.ShouldBe(laterClock.UtcNow);

        var evt = resume.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<ResumeAdoptedDomainEvent>();
        evt.ResumeId.ShouldBe(resume.Id);
        evt.OccurredAt.ShouldBe(laterClock.UtcNow);
    }

    [Fact]
    public void Adopt_Twice_ReturnsConflict()
    {
        var resume = CreateImportedResume();
        var firstClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(1));
        var secondClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(2));

        resume.Adopt(firstClock).IsSuccess.ShouldBeTrue();
        var firstStamp = resume.AdoptedAt;

        var result = resume.Adopt(secondClock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.AlreadyAdopted");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        // One-way: the second call must NOT re-stamp AdoptedAt with the newer clock.
        resume.AdoptedAt.ShouldBe(firstStamp);
        resume.AdoptedAt.ShouldBe(firstClock.UtcNow);
        // And it must NOT raise a second ResumeAdoptedDomainEvent.
        resume.DomainEvents.OfType<ResumeAdoptedDomainEvent>().Count().ShouldBe(1);
    }

    [Fact]
    public void Adopt_OnTemplateOriginResume_ReturnsValidation()
    {
        // Only an imported CV can be adopted; a Template-origin CV is already app-rendered.
        var resume = CreateTemplateResume();

        var result = resume.Adopt(Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.OnlyImportedCanBeAdopted");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        resume.AdoptedAt.ShouldBeNull();
        resume.IsAdopted.ShouldBeFalse();
    }

    [Fact]
    public void Adopt_DoesNotTouchDenormalizedProjection()
    {
        // ADR 0059 / #651 non-regression: adoption is provenance-only and must never touch
        // the MatchProfileBuilder-facing projection fields.
        var resume = CreateImportedResume();
        var latestRoleBefore = resume.LatestRole;
        var sectionCountBefore = resume.SectionCount;
        var topSkillsBefore = resume.TopSkills.ToList();

        resume.Adopt(Clock).IsSuccess.ShouldBeTrue();

        resume.LatestRole.ShouldBe(latestRoleBefore);
        resume.SectionCount.ShouldBe(sectionCountBefore);
        resume.TopSkills.ShouldBe(topSkillsBefore);
        // Anchor the concrete values so a silent projection wipe is caught, not just "equal to itself".
        resume.LatestRole.ShouldBe("Backend-utvecklare");
        resume.SectionCount.ShouldBe(4);
        resume.TopSkills.ShouldBe(["C#", "PostgreSQL"]);
    }

    // ---------------------------------------------------------------
    // ChangeTemplateOptions — the single VO mutation path
    // ---------------------------------------------------------------

    [Fact]
    public void ChangeTemplateOptions_WithNewOptions_PersistsAndRaisesEvent()
    {
        var resume = CreateTemplateResume();
        resume.ClearDomainEvents();
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(1));
        // Every member differs from Default → a genuine change.
        var newOptions = CvTemplateOptions.Default with
        {
            Template = CvTemplate.MorkPanel,
            AccentColor = CvAccentColor.ForestGreen,
            FontPair = CvFontPair.Classic,
            Density = CvDensity.Compact,
            PhotoEnabled = true,
            PhotoShape = CvPhotoShape.Square,
        };

        var result = resume.ChangeTemplateOptions(newOptions, laterClock);

        result.IsSuccess.ShouldBeTrue();
        resume.TemplateOptions.ShouldBe(newOptions);
        resume.UpdatedAt.ShouldBe(laterClock.UtcNow);

        var evt = resume.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<ResumeTemplateOptionsChangedDomainEvent>();
        evt.ResumeId.ShouldBe(resume.Id);
        evt.OccurredAt.ShouldBe(laterClock.UtcNow);
    }

    [Fact]
    public void ChangeTemplateOptions_Null_ReturnsValidation()
    {
        var resume = CreateTemplateResume();

        var result = resume.ChangeTemplateOptions(null, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.TemplateOptionsRequired");
    }

    [Fact]
    public void ChangeTemplateOptions_WithNullMember_ReturnsValidation()
    {
        // A positional record has no ctor guard, so a null member is reachable — the single
        // mutation path refuses it via IsComplete.
        var resume = CreateTemplateResume();
        var incomplete = CvTemplateOptions.Default with { AccentColor = null! };

        var result = resume.ChangeTemplateOptions(incomplete, Clock);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.TemplateOptionsIncomplete");
    }

    [Fact]
    public void ChangeTemplateOptions_SameOptions_IsNoOp()
    {
        var resume = CreateTemplateResume();
        var updatedAtBefore = resume.UpdatedAt;
        resume.ClearDomainEvents();
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(5));
        // A reference-DISTINCT but value-equal instance — proves the no-op relies on value
        // equality (the current TemplateOptions is CvTemplateOptions.Default), not identity.
        var sameByValue = new CvTemplateOptions(
            CvTemplate.Klar,
            CvAccentColor.NavyBlue,
            CvFontPair.Modern,
            CvDensity.Normal,
            PhotoEnabled: false,
            CvPhotoShape.Circle);

        var result = resume.ChangeTemplateOptions(sameByValue, laterClock);

        result.IsSuccess.ShouldBeTrue();
        // No-op: no timestamp advance, no event (SetLanguage parity).
        resume.UpdatedAt.ShouldBe(updatedAtBefore);
        resume.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void ChangeTemplateOptions_DoesNotAffectOriginOrAdoption()
    {
        // Changing display options is orthogonal to provenance and adoption state.
        var resume = CreateImportedResume(); // Origin == Import, not adopted
        var laterClock = FakeDateTimeProvider.At(Clock.UtcNow.AddHours(1));
        var newOptions = CvTemplateOptions.Default with { Template = CvTemplate.Accentlinje };

        resume.ChangeTemplateOptions(newOptions, laterClock).IsSuccess.ShouldBeTrue();

        resume.Origin.ShouldBe(ResumeSourceOrigin.Import);
        resume.AdoptedAt.ShouldBeNull();
        resume.IsAdopted.ShouldBeFalse();
    }
}
