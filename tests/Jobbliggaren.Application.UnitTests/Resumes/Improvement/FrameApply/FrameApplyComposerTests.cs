using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.FrameApply;
using Jobbliggaren.Application.Resumes.Review;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.Resumes;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Improvement.FrameApply;

/// <summary>
/// Fas 4b PR-7 (#656, ADR 0093 §D2) — the pure, side-effect-free composer shared by the preview
/// query and the apply command. <see cref="FrameApplyComposer.ResolveFinding"/> proves a finding is
/// actionable (Fail/Warn with a text-span), unchanged (server fingerprint match) and locatable (a
/// single content line CONTAINS the cited quote, deterministic search order); its counterpart
/// <see cref="FrameApplyComposer.ApplyToContent"/> swaps exactly that line, returning a NEW immutable
/// <see cref="ResumeContent"/>. The fingerprint oracle is the REAL server-side
/// <see cref="FindingTargetFingerprint.Compute"/> (never a client-forged digest — ADR 0074 Inv. 2).
///
/// SPEC-DRIVEN — RED until LocatedFinding + FrameApplyComposer ship in
/// Jobbliggaren.Application.Resumes.Improvement.FrameApply.
/// </summary>
public class FrameApplyComposerTests
{
    private static readonly RubricVersion Version = RubricVersion.Parse("1.2.0");

    private static CvCriterionVerdict Fail(
        string criterionId, string quote, RubricCategory category = RubricCategory.Content) =>
        CvCriterionVerdict.Assessed(criterionId, category, CriterionVerdict.Fail,
            [new TextSpanEvidence(new TextSpan(TextSpan.NotLocated, quote.Length, quote), null)]);

    private static CvReviewResult ResultWith(params CvCriterionVerdict[] verdicts) =>
        new(Version, RenderProfile.Ats, [], verdicts, [], verdicts.Length, verdicts.Length);

    private static string Fingerprint(CvCriterionVerdict verdict) =>
        FindingTargetFingerprint.Compute(Version, verdict);

    private static ResumeContent WithSummary(string summary) =>
        new(new PersonalInfo("Klas Olsson", null, null, null), summary: summary);

    private static Experience Experience(string description) =>
        new("Acme AB", "Utvecklare", new DateOnly(2021, 1, 1), null, description);

    // ===============================================================
    // ResolveFinding — locate in the deterministic search order
    // ===============================================================

    [Fact]
    public void ResolveFinding_ShouldLocateTheSummaryLine_WhenTheQuoteIsInTheSummary()
    {
        const string line = "Ansvarig för kundtjänst och support i butiken";
        var content = WithSummary(line);
        var verdict = Fail("A2", line);

        var result = FrameApplyComposer.ResolveFinding(
            ResultWith(verdict), "A2", Fingerprint(verdict), content);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Line.ShouldBe(line);
        result.Value.Fingerprint.ShouldBe(Fingerprint(verdict));
        result.Value.Verdict.CriterionId.ShouldBe("A2");
    }

    [Fact]
    public void ResolveFinding_ShouldLocateOneDescriptionLine_WhenTheDescriptionIsMultiLine()
    {
        const string target = "Ansvarig för support i butiken.";
        var content = new ResumeContent(
            new PersonalInfo("Klas Olsson", null, null, null),
            experiences: [Experience($"Rad ett.\n{target}\nRad tre.")]);
        var verdict = Fail("A2", target);

        var result = FrameApplyComposer.ResolveFinding(
            ResultWith(verdict), "A2", Fingerprint(verdict), content);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Line.ShouldBe(target);
    }

    [Fact]
    public void ResolveFinding_ShouldLocateTheContainingLine_WhenTheQuoteIsASingleWord()
    {
        // A C3-style verdict may cite a single word; the located Before is the whole line that
        // contains it (ordinal substring), so the frame rewrites the sentence, not the word.
        const string line = "Ansvarig för kundtjänst och support";
        var content = WithSummary(line);
        var verdict = Fail("C3", "Ansvarig", RubricCategory.Language);

        var result = FrameApplyComposer.ResolveFinding(
            ResultWith(verdict), "C3", Fingerprint(verdict), content);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Line.ShouldBe(line);
    }

    // ===============================================================
    // ResolveFinding — Conflict / not-actionable guards
    // ===============================================================

    [Fact]
    public void ResolveFinding_ShouldReturnFindingChanged_WhenTheClientFingerprintDoesNotMatch()
    {
        const string line = "Ansvarig för kundtjänst och support i butiken";
        var content = WithSummary(line);
        var verdict = Fail("A2", line);

        var result = FrameApplyComposer.ResolveFinding(
            ResultWith(verdict), "A2", clientFingerprint: new string('0', 64), content);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FindingChanged");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public void ResolveFinding_ShouldReturnFindingNotActionable_WhenTheVerdictIsPass()
    {
        const string line = "En stark, mätbar rad.";
        var content = WithSummary(line);
        var pass = CvCriterionVerdict.Assessed("A2", RubricCategory.Content, CriterionVerdict.Pass,
            [new TextSpanEvidence(new TextSpan(0, line.Length, line), null)]);

        var result = FrameApplyComposer.ResolveFinding(
            ResultWith(pass), "A2", Fingerprint(pass), content);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FindingNotActionable");
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public void ResolveFinding_ShouldReturnFindingNotActionable_WhenTheEvidenceIsStructuralOnly()
    {
        // A structural verdict (e.g. B4 "1 personnummer hittat") has no text span to rewrite.
        var content = WithSummary("Valfri text.");
        var structural = CvCriterionVerdict.Assessed("B4", RubricCategory.Structure, CriterionVerdict.Fail,
            [new StructuralEvidence("1 personnummer hittat")]);

        var result = FrameApplyComposer.ResolveFinding(
            ResultWith(structural), "B4", Fingerprint(structural), content);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FindingNotActionable");
    }

    [Fact]
    public void ResolveFinding_ShouldReturnFindingLineNotFound_WhenTheQuoteIsAbsentFromTheContent()
    {
        var content = WithSummary("Helt annan text.");
        var verdict = Fail("A2", "Ansvarig för kundtjänst");

        var result = FrameApplyComposer.ResolveFinding(
            ResultWith(verdict), "A2", Fingerprint(verdict), content);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.FindingLineNotFound");
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    // ===============================================================
    // ApplyToContent — swap exactly one line, immutably
    // ===============================================================

    [Fact]
    public void ApplyToContent_ShouldReplaceTheSummaryLine_AndLeaveOtherFieldsUntouched()
    {
        var content = new ResumeContent(
            new PersonalInfo("Klas Olsson", null, null, null),
            experiences: [Experience("Orörd erfarenhet.")],
            summary: "Gammal rad");

        var result = FrameApplyComposer.ApplyToContent(content, "Gammal rad", "Ny rad");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Summary.ShouldBe("Ny rad");
        result.Value.Experiences[0].Description.ShouldBe("Orörd erfarenhet.");
        result.Value.ShouldNotBeSameAs(content, "records are immutable — apply returns a NEW content.");
        content.Summary.ShouldBe("Gammal rad", "the original content must be left unmutated.");
    }

    [Fact]
    public void ApplyToContent_ShouldReplaceOnlyTheMatchingDescriptionLine_WhenMultiLine()
    {
        var content = new ResumeContent(
            new PersonalInfo("Klas Olsson", null, null, null),
            experiences: [Experience("Rad A\nGammal rad\nRad C")]);

        var result = FrameApplyComposer.ApplyToContent(content, "Gammal rad", "Ny rad");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Experiences[0].Description.ShouldBe("Rad A\nNy rad\nRad C");
    }

    [Fact]
    public void ApplyToContent_ShouldReplaceTheFirstOccurrenceInSearchOrder_WhenSummaryAndExperienceCollide()
    {
        // Summary is searched before experience descriptions — the Summary occurrence is the one
        // replaced; the identical experience line is left intact (deterministic search order).
        var content = new ResumeContent(
            new PersonalInfo("Klas Olsson", null, null, null),
            experiences: [Experience("Dubblett")],
            summary: "Dubblett");

        var result = FrameApplyComposer.ApplyToContent(content, "Dubblett", "Ändrad");

        result.IsSuccess.ShouldBeTrue();
        result.Value.Summary.ShouldBe("Ändrad");
        result.Value.Experiences[0].Description.ShouldBe("Dubblett");
    }

    [Fact]
    public void ApplyToContent_ShouldReturnConflict_WhenTheBeforeLineIsNotPresent()
    {
        var content = WithSummary("Finns här");

        var result = FrameApplyComposer.ApplyToContent(content, "Finns inte", "X");

        result.IsFailure.ShouldBeTrue();
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public void ApplyToContent_ShouldReplaceASectionEntryLine_AndLeaveSiblingLinesUntouched()
    {
        // Code review Minor 4: the dynamic-sections arm of the apply walk (the third and
        // last search surface) — the matching entry line is swapped, siblings and the
        // entry title stay untouched, and the original content is unmutated.
        var content = new ResumeContent(
            new PersonalInfo("Klas Olsson", null, null, null),
            sections:
            [
                new ResumeSection("Kurser",
                [
                    new SectionEntry("HLR", ["Grundkurs", "Gammal rad", "Repetition"]),
                ]),
            ]);

        var result = FrameApplyComposer.ApplyToContent(content, "Gammal rad", "Ny rad");

        result.IsSuccess.ShouldBeTrue();
        var entry = result.Value.Sections.ShouldHaveSingleItem().Entries.ShouldHaveSingleItem();
        entry.Title.ShouldBe("HLR");
        entry.Lines.ShouldBe(["Grundkurs", "Ny rad", "Repetition"]);
        content.Sections[0].Entries[0].Lines.ShouldBe(["Grundkurs", "Gammal rad", "Repetition"],
            "the original content must be left unmutated.");
    }

    [Fact]
    public void ResolveFinding_ShouldLocateASectionEntryLine_WhenTheQuoteLivesInADynamicSection()
    {
        // Code review Minor 4: the location walk's Sections arm mirrors the apply walk.
        const string line = "Ansvarig för utbildningsinsatser";
        var content = new ResumeContent(
            new PersonalInfo("Klas Olsson", null, null, null),
            sections: [new ResumeSection("Övrigt", [new SectionEntry("Utbildning", [line])])]);
        var verdict = Fail("C3", "Ansvarig", RubricCategory.Language);

        var result = FrameApplyComposer.ResolveFinding(
            ResultWith(verdict), "C3", Fingerprint(verdict), content);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Line.ShouldBe(line);
    }
}
