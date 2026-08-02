using Jobbliggaren.Domain.Resumes;
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
/// other measurements, and both live elsewhere on purpose: an EMPTY layout-corpus baseline diff
/// (nothing the instrument measures moved), and the requirement that mutating an arm HERE reddens
/// a <c>ValidateContent</c> test THERE — which is what proves one home rather than two. Every one
/// of the thirteen arms below is already exercised through <c>UpdateMasterContent</c> in
/// <c>ResumeTests</c>, so that second falsifier is armed on all of them; if these were the only
/// tests of the extracted rule, a second copy of it inside <c>ValidateContent</c> would stay
/// green and the decomposition would be undetectably wrong.</para>
///
/// <para><b>Premise (CLAUDE.md §5 <c>Tests:</c>).</b> Every state constructed here is one
/// <c>src/</c> produces. The blank label fields are what <c>AutoPromoteContentMapper</c> emits —
/// it projects <c>e.Organization ?? string.Empty</c> and <c>e.Title ?? string.Empty</c>, so a
/// parse that found no employer arrives with exactly this shape. The over-long
/// <c>RawPeriod</c> is likewise produced rather than invented: that same mapper passes
/// <c>e.Period</c> UNTRUNCATED by written policy — "an over-long period is for the buildability
/// gate to reject, not for this projection to silently shorten". The over-long prose and the
/// inverted date pair come from the structured editor, whose client caps mirror these bounds with
/// the Domain as the authority.</para>
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
}
