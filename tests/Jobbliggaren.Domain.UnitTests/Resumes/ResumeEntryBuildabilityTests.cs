using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Resumes;

/// <summary>
/// Direct coverage of the per-entry surface #1060 D3(β-2) extracted from
/// <c>Resume.ValidateContent</c> (CTO-bind §3.1). These tests drive the PUBLIC readers, which is
/// what <c>ResumeTests</c> cannot do — it reaches the same arms only through
/// <c>UpdateMasterContent</c>, so nothing there says the extracted surface is reachable from
/// outside the aggregate at all. That reachability IS β-2's deliverable, and β-3's router is its
/// consumer.
///
/// <para><b>What these tests are NOT.</b> They are not the decomposition's falsifier. That is two
/// other measurements, and both live elsewhere on purpose: the layout corpus moving no MEASURED
/// value (β-2's baseline diff was not empty — it repaired prose this refactor made false — so the
/// proof is strip-those-regions-and-compare), and the requirement that mutating an arm HERE
/// reddens a <c>ValidateContent</c> test THERE, which is what proves one home rather than two.
/// Every arm below is already exercised through <c>UpdateMasterContent</c> in
/// <c>ResumeTests</c> and <c>HonestDateAbsenceTests</c> — the two <c>RawPeriod</c> caps live in
/// the latter, which is why naming only the former would send a reader to a file holding most of
/// them and no more. So that second falsifier is armed on all of them; if these were the only
/// tests of the extracted rule, a second copy of it inside <c>ValidateContent</c> would stay
/// green and the decomposition would be undetectably wrong.</para>
///
/// <para><b>Premise (CLAUDE.md §5 <c>Tests:</c>).</b> Every state constructed here is one
/// <c>src/</c> produces, and there are exactly two producers.</para>
///
/// <para><b>The parse path.</b> <c>AutoPromoteContentMapper</c> projects
/// <c>e.Organization ?? string.Empty</c> and <c>e.Title ?? string.Empty</c>, with
/// <c>e.Institution ?? string.Empty</c> and <c>e.Degree ?? string.Empty</c> alongside for the
/// education mirror — so a parse that found no employer arrives with an EMPTY label, the shape
/// <c>AutoPromoteGateTests</c> and <c>AutoPromoteParsedResumeCommandHandlerTests</c> already drive
/// end to end. That same mapper passes <c>e.Period</c> to <c>RawPeriod</c> UNTRUNCATED by written
/// policy — "an over-long period is for the buildability gate to reject, not for this projection
/// to silently shorten" — so the over-long <c>RawPeriod</c> is produced, not invented.</para>
///
/// <para><b>The write path, which produces the rest.</b> The master-content and promote endpoints
/// bind <c>ResumeContentDto</c> off the wire with no per-field attributes;
/// <c>UpdateMasterContentCommandValidator</c> caps only <c>FullName</c> and <c>Summary</c>, and
/// <c>PromoteParsedResumeCommandValidator</c> only <c>Name</c>, <c>FullName</c> and
/// <c>Summary</c> — neither names <c>Content.Experiences</c> anywhere, which is why the claim
/// below spans both endpoints rather than the one it used to cite;
/// <c>ResumeContentMapper.ToDomain</c> then builds the
/// entry VERBATIM under its own docblock — "Pure projection — no validation (the aggregate's
/// ValidateContent owns that)". So the whitespace-only label, the over-long prose, the inverted
/// date pair and the over-long period all reach this reader exactly as written below. These arms
/// are the ONLY .NET gate on them.</para>
///
/// <para><b>Deliberately NOT cited: the structured editor.</b> Its Zod schema caps description at
/// 2 000 and <c>rawPeriod</c> at 100 and refuses <c>endDate &lt; startDate</c>, and it runs that
/// schema twice — in the browser and again inside the <c>"use server"</c> action. The editor is
/// the actor that PREVENTS these shapes, never one that produces them; an earlier revision named
/// it as the producer, which is a citation the tree refutes.</para>
/// </summary>
public class ResumeEntryBuildabilityTests
{
    private const string Valid = "Backend-utvecklare";

    private static Experience ValidExperience(
        string company = "Klarna AB",
        string role = Valid,
        DateOnly? start = null,
        DateOnly? end = null,
        string? description = null,
        string? rawPeriod = null) =>
        new(company, role, start, end, description, rawPeriod);

    private static Education ValidEducation(
        string institution = "Chalmers tekniska högskola",
        string degree = "Civilingenjör",
        DateOnly? start = null,
        DateOnly? end = null,
        string? rawPeriod = null) =>
        new(institution, degree, start, end, rawPeriod);

    // ---------------------------------------------------------------
    // Experience — the seven arms, in the aggregate's own order
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ExperienceWithBlankCompany_ReturnsCompanyRequired(string company)
    {
        var result = ResumeEntryBuildability.Validate(ValidExperience(company: company));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.ExperienceCompanyRequired");
    }

    [Fact]
    public void Validate_ExperienceWithOverLongCompany_ReturnsCompanyTooLong()
    {
        var result = ResumeEntryBuildability.Validate(
            ValidExperience(company: new string('a', 201)));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.ExperienceCompanyTooLong");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ExperienceWithBlankRole_ReturnsRoleRequired(string role)
    {
        var result = ResumeEntryBuildability.Validate(ValidExperience(role: role));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.ExperienceRoleRequired");
    }

    [Fact]
    public void Validate_ExperienceWithOverLongRole_ReturnsRoleTooLong()
    {
        var result = ResumeEntryBuildability.Validate(ValidExperience(role: new string('a', 201)));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.ExperienceRoleTooLong");
    }

    [Fact]
    public void Validate_ExperienceWithOverLongDescription_ReturnsDescriptionTooLong()
    {
        var result = ResumeEntryBuildability.Validate(
            ValidExperience(description: new string('a', 2_001)));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.ExperienceDescriptionTooLong");
    }

    [Fact]
    public void Validate_ExperienceWithEndBeforeStart_ReturnsDatesInvalid()
    {
        var result = ResumeEntryBuildability.Validate(ValidExperience(
            start: new DateOnly(2024, 6, 1), end: new DateOnly(2024, 1, 1)));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.ExperienceDatesInvalid");
    }

    [Fact]
    public void Validate_ExperienceWithOverLongRawPeriod_ReturnsRawPeriodTooLong()
    {
        var result = ResumeEntryBuildability.Validate(
            ValidExperience(rawPeriod: new string('a', 101)));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.ExperienceRawPeriodTooLong");
    }

    // ---------------------------------------------------------------
    // Education — the six arms, in the aggregate's own order
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EducationWithBlankInstitution_ReturnsInstitutionRequired(string institution)
    {
        var result = ResumeEntryBuildability.Validate(ValidEducation(institution: institution));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.EducationInstitutionRequired");
    }

    [Fact]
    public void Validate_EducationWithOverLongInstitution_ReturnsInstitutionTooLong()
    {
        var result = ResumeEntryBuildability.Validate(
            ValidEducation(institution: new string('a', 201)));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.EducationInstitutionTooLong");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EducationWithBlankDegree_ReturnsDegreeRequired(string degree)
    {
        var result = ResumeEntryBuildability.Validate(ValidEducation(degree: degree));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.EducationDegreeRequired");
    }

    [Fact]
    public void Validate_EducationWithOverLongDegree_ReturnsDegreeTooLong()
    {
        var result = ResumeEntryBuildability.Validate(ValidEducation(degree: new string('a', 201)));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.EducationDegreeTooLong");
    }

    [Fact]
    public void Validate_EducationWithEndBeforeStart_ReturnsDatesInvalid()
    {
        var result = ResumeEntryBuildability.Validate(ValidEducation(
            start: new DateOnly(2020, 9, 1), end: new DateOnly(2018, 6, 1)));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.EducationDatesInvalid");
    }

    [Fact]
    public void Validate_EducationWithOverLongRawPeriod_ReturnsRawPeriodTooLong()
    {
        var result = ResumeEntryBuildability.Validate(
            ValidEducation(rawPeriod: new string('a', 101)));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Resume.EducationRawPeriodTooLong");
    }

    // ---------------------------------------------------------------
    // The boundaries themselves, and the arms that must NOT fire
    // ---------------------------------------------------------------

    /// <summary>
    /// The caps are inclusive, and an entry AT the bound builds. Without this the over-long tests
    /// above are satisfied by any rule at least as strict — including an off-by-one that refuses
    /// a legal 200-character employer name.
    /// </summary>
    [Fact]
    public void Validate_ExperienceAtEveryBound_ReturnsSuccess()
    {
        var result = ResumeEntryBuildability.Validate(ValidExperience(
            company: new string('a', 200),
            role: new string('a', 200),
            description: new string('a', 2_000),
            rawPeriod: new string('a', 100)));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Validate_EducationAtEveryBound_ReturnsSuccess()
    {
        var result = ResumeEntryBuildability.Validate(ValidEducation(
            institution: new string('a', 200),
            degree: new string('a', 200),
            rawPeriod: new string('a', 100)));

        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// Honest date absence (CTO-bind 5a-pre): the date arm fires only when BOTH dates are
    /// present. A lone end date VALIDATES — "examen 2020" is a real thing a CV says — so a rule
    /// that compared against a default date would refuse a document the product accepts. This is
    /// also the shape auto-promote emits on every entry it builds: null start, null end, the
    /// verbatim period on RawPeriod.
    /// </summary>
    [Fact]
    public void Validate_EntriesWithOnlyOneDate_ReturnsSuccess()
    {
        ResumeEntryBuildability.Validate(ValidExperience(end: new DateOnly(2020, 1, 1)))
            .IsSuccess.ShouldBeTrue();
        ResumeEntryBuildability.Validate(ValidExperience(start: new DateOnly(2020, 1, 1)))
            .IsSuccess.ShouldBeTrue();
        ResumeEntryBuildability.Validate(ValidEducation(end: new DateOnly(2020, 1, 1)))
            .IsSuccess.ShouldBeTrue();
        ResumeEntryBuildability.Validate(ValidEducation(start: new DateOnly(2020, 1, 1)))
            .IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// Equal dates are not "end before start". The boundary the inverted-pair tests above stop
    /// at: a one-month contract that starts and ends in the same month is legal.
    /// </summary>
    [Fact]
    public void Validate_EntriesWithEqualDates_ReturnsSuccess()
    {
        var same = new DateOnly(2022, 3, 1);

        ResumeEntryBuildability.Validate(ValidExperience(start: same, end: same))
            .IsSuccess.ShouldBeTrue();
        ResumeEntryBuildability.Validate(ValidEducation(start: same, end: same))
            .IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// Order is load-bearing and is what makes the returned code a usable answer rather than an
    /// arbitrary one of several true ones. Company is asked before Role, so an entry missing BOTH
    /// reports the company — which is precisely how PR 2 read the corpus's blocking rows: getting
    /// <c>ExperienceRoleRequired</c> back was positive evidence that the company had survived.
    /// </summary>
    [Fact]
    public void Validate_ExperienceMissingBothLabels_ReportsCompanyFirst()
    {
        var result = ResumeEntryBuildability.Validate(
            ValidExperience(company: string.Empty, role: string.Empty));

        result.Error.Code.ShouldBe("Resume.ExperienceCompanyRequired");
    }

    [Fact]
    public void Validate_EducationMissingBothLabels_ReportsInstitutionFirst()
    {
        var result = ResumeEntryBuildability.Validate(
            ValidEducation(institution: string.Empty, degree: string.Empty));

        result.Error.Code.ShouldBe("Resume.EducationInstitutionRequired");
    }

    /// <summary>
    /// The exact shape the auto-promote path hands this reader on every entry it builds: labels
    /// from the parse, structured dates honestly absent, no description (the mapper always emits
    /// null) and the verbatim period on <c>RawPeriod</c>. Nothing else here asserts that
    /// combination — the bound tests set <c>RawPeriod</c> at exactly 100 and the date tests never
    /// set it at all — so without this, a new rule IN THE EXPERIENCE READER that rejected an entry
    /// with no structured dates and no description would move no test in this file. (The
    /// education half is weaker and says so: <c>Validate_EducationAtEveryBound_ReturnsSuccess</c>
    /// already carries null dates, and Education has no Description at all.)
    ///
    /// <para>Deliberately not the other direction: this test hand-builds the shape rather than
    /// calling <c>AutoPromoteContentMapper</c>, so a change to the PROJECTION cannot reach it. The
    /// mapper→reader coupling is covered elsewhere, by the Application tests that drive
    /// <c>ParsedExperience</c> through the projection into <c>CreateFromParsed</c>.</para>
    /// </summary>
    [Fact]
    public void Validate_TheShapeAutoPromoteProjects_ReturnsSuccess()
    {
        ResumeEntryBuildability.Validate(new Experience(
            "Klarna AB", Valid, StartDate: null, EndDate: null, Description: null,
            RawPeriod: "2021 - 2026")).IsSuccess.ShouldBeTrue();

        ResumeEntryBuildability.Validate(new Education(
            "Chalmers tekniska högskola", "Civilingenjör", StartDate: null, EndDate: null,
            RawPeriod: "2016 - 2021")).IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// What the aggregate does with the reader's answer: it returns the WHOLE
    /// <see cref="DomainError"/> verbatim — code, Swedish message and <see cref="ErrorKind"/> —
    /// rather than re-wrapping it in one of its own. <c>DomainError</c> is a sealed record, so one
    /// structural comparison pins that propagation with no hardcoded Swedish. No test anywhere
    /// asserts one of these per-entry messages (measured), so a <c>ValidateContent</c> that
    /// preserved the code and substituted the message — <c>DomainError.Validation(err.Code,
    /// "Ogiltigt innehåll.")</c> — would move nothing else. This is that one guard.
    ///
    /// <para><b>What the COMPARISON does not do, precisely.</b> It cannot detect a reworded
    /// message, nor on its own a factory swapped from <c>Validation</c> to <c>Conflict</c>:
    /// <c>ValidateContent</c> returns this reader's own <c>Result</c>, so both sides read the same
    /// literals and move together. That guarantee was claimed for the comparison and is WITHDRAWN.
    /// The <see cref="ErrorKind"/> assertion below recovers the factory half — it pins
    /// <c>Kind</c> as a VALUE, so swapping THIS arm's factory reddens even though the comparison
    /// stays green. It is one arm of thirteen: a swap on any other still moves nothing. And it
    /// pins the <c>Kind</c> the central mapper reads for 400, not the 400 itself. That step IS
    /// pinned, just not from here and not for this family:
    /// <c>ResumesEndpointsTests.PUT_master_with_personnummer_in_summary_returns_400_and_does_not_persist</c>
    /// drives a <c>Validation</c>-kind error through <c>ToProblemResult</c> and asserts the 400
    /// together with the code-as-title, which is what excludes any other 400 source. Adjudicator,
    /// so this is checkable: flip <c>ErrorKind.Validation ⇒ Status400BadRequest</c> in
    /// <c>DomainErrorResults</c> and that test reddens. What is genuinely absent is narrower —
    /// the mapper has no unit test, and no integration test touches any of these thirteen codes
    /// (measured, zero files). An earlier revision said "no test in the repo pins" it, which was
    /// a repo-wide negative with no adjudicator, and false.
    /// The MESSAGE stays deliberately unpinned: it is user-facing copy (CLAUDE.md §10) and a
    /// literal here would be the localization-fragile assertion §5 warns against — and it would
    /// not catch the rewording anyway, for the reason above.</para>
    ///
    /// <para>It also catches DRIFT, not duplication: an identical second copy inside
    /// <c>ValidateContent</c> stays green here. Only the mutation falsifier distinguishes one home
    /// from two, and this does not replace it.</para>
    /// </summary>
    [Fact]
    public void Validate_ReturnsTheSameErrorTheAggregateReturns_CodeMessageAndKind()
    {
        var unbuildable = ValidExperience(company: string.Empty);
        var resume = Resume.Create(
            new JobSeekerId(Guid.NewGuid()), "Mitt CV", "Klas Olsson",
            FakeDateTimeProvider.Default).Value;

        var viaAggregate = resume.UpdateMasterContent(
            new ResumeContent(
                new PersonalInfo("Klas Olsson", null, null, null),
                experiences: new[] { unbuildable }),
            FakeDateTimeProvider.Default);

        // Without this line the test is GREEN when both sides succeed — which is exactly what the
        // register's M1 cell (arm deleted) produces: `Result.Error` does not throw on success, it
        // returns DomainError.None, and None equals None. Measured, not supposed.
        viaAggregate.IsFailure.ShouldBeTrue();
        viaAggregate.Error.ShouldBe(ResumeEntryBuildability.Validate(unbuildable).Error);
        viaAggregate.Error.Kind.ShouldBe(ErrorKind.Validation);
    }
}
