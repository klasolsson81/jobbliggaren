using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.KnowledgeBank;

/// <summary>
/// Reason-relocation STEG (ADR 0071) — N-1 tolerance for the NEW per-criterion
/// <c>notAssessedReason</c> field. A pre-relocation (N-1) rubric document that PRE-DATES
/// the field must still deserialise + map through the REAL loader without throwing, and a
/// criterion authored WITHOUT the field must map to <see cref="RubricCriterion.NotAssessedReason"/>
/// == null — mirroring the existing missing-<c>assessability</c> tolerance in
/// <see cref="RubricBackcompatTests"/>.
///
/// The fixture is embedded in THIS assembly:
///   LogicalName = Jobbliggaren.Application.UnitTests.KnowledgeBank.Fixtures.rubric.v1.0.0-no-reason-synthetic.json
///
/// It is shaped as a v1.0.0 asset (assessability + bands + criticalFailIds present) but
/// OMITS every criterion's <c>notAssessedReason</c>; A5 is authored not_assessed_v1 with no
/// reason. The code-side civic fallback (engine-level) is exercised separately in
/// CvReviewEngineTests — here we pin the LOADER half of the contract: missing field → null,
/// no throw.
///
/// RED until <see cref="RubricCriterion"/> gains the trailing optional
/// <c>string? NotAssessedReason = null</c> param and <c>RubricFile.CriterionFile</c> gains the
/// trailing defaulted <c>notAssessedReason</c> JSON member that <c>MapCriterion</c> passes through.
/// </summary>
public class RubricNotAssessedReasonBackcompatTests
{
    private const string FixtureResourceName =
        "Jobbliggaren.Application.UnitTests.KnowledgeBank.Fixtures.rubric.v1.0.0-no-reason-synthetic.json";

    private static Stream OpenFixture()
    {
        var asm = typeof(RubricNotAssessedReasonBackcompatTests).Assembly;
        return asm.GetManifestResourceStream(FixtureResourceName)
            ?? throw new InvalidOperationException(
                $"Synthetic N-1 (no-reason) fixture saknas: {FixtureResourceName}. " +
                "Verifiera <EmbeddedResource> + <LogicalName> i " +
                "Jobbliggaren.Application.UnitTests.csproj.");
    }

    [Fact]
    public void GetRubric_ShouldNotThrow_WhenNotAssessedReasonFieldAbsent()
    {
        // 1) An N-1 document that pre-dates notAssessedReason is READ, not rejected
        //    (default System.Text.Json tolerance + trailing-optional contract param).
        using var stream = OpenFixture();

        var act = () => RubricLoader.LoadFrom(stream);

        var rubric = act.ShouldNotThrow();
        rubric.Criteria.ShouldNotBeEmpty();
    }

    [Fact]
    public void GetRubric_ShouldMapMissingNotAssessedReasonToNull_WhenAbsent()
    {
        // 2) A criterion authored WITHOUT notAssessedReason maps to null — the engine's
        //    code-side civic fallback (tested in CvReviewEngineTests) is what covers null
        //    at review time. The loader never invents a reason.
        using var stream = OpenFixture();

        var rubric = RubricLoader.LoadFrom(stream);

        // A5 is the pinned not_assessed_v1 criterion in the fixture, deliberately authored
        // with no reason → NotAssessedReason must be null (N-1 tolerance), not "".
        rubric.Criteria.Single(c => c.Id == "A5").NotAssessedReason.ShouldBeNull();

        // Every criterion in this N-1 fixture lacks the field → all null.
        rubric.Criteria.ShouldAllBe(c => c.NotAssessedReason == null);
    }
}
