using System.Reflection;
using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// F4-13 (ADR 0076 Decision 5; senior-cto-advisor 2026-06-19 A1/B2/C2a) anti-regression
/// for the page-scoped match-tag batch overlay. Mirrors the Goodhart pins in
/// <see cref="MatchScorerLayerTests"/> but for the F4-13 wire surface: the batch DTO + the
/// per-entry DTO live in Application, carry EXACTLY the named-category + four verdicts and
/// NO numeric/score field (ADR 0076 Decision 4 / ADR 0071 / CLAUDE.md §5 — the Goodhart
/// guard realised ON THE WIRE), and the <see cref="Jobbliggaren.Application.Matching.Grading.MatchGrade"/>
/// enum is exactly the three named rungs (no numeric band). The existing
/// MatchScore/MatchDimension/MatchDimensionVerdict shape pins stay in MatchScorerLayerTests.
/// </summary>
public class MatchTagBatchLayerTests
{
    // The Goodhart tripwire: no public property on the wire DTOs may carry a name that
    // reads as an opaque numeric total / sort key (ADR 0076 Decision 4). A category
    // ("Grade") and per-dimension verdicts are allowed; anything score-shaped is not.
    private static readonly Regex ForbiddenNumericName =
        new(@"Score|Value|Total|Percent|SortKey|Rank|Intensity|Points",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static List<string> PublicInstancePropertyNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract") // compiler-generated on records
            .Select(p => p.Name)
            .ToList();

    // ===============================================================
    // 1. The wire DTOs live in the Application assembly
    // ===============================================================

    [Fact]
    public void JobAdMatchBatchDto_is_in_Application_layer()
    {
        var dto = typeof(Jobbliggaren.Application.Matching.Queries.GetJobAdMatchBatch.JobAdMatchBatchDto);
        dto.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void JobAdMatchEntryDto_is_in_Application_layer()
    {
        var dto = typeof(Jobbliggaren.Application.Matching.Queries.GetJobAdMatchBatch.JobAdMatchEntryDto);
        dto.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void IMatchProfileBuilder_is_in_Application_layer()
    {
        // The SSOT preference→profile collaborator is an Application abstraction (it
        // touches only IAppDbContext + ICurrentUser — no Npgsql secret crosses it).
        var port = typeof(Jobbliggaren.Application.Matching.Abstractions.IMatchProfileBuilder);
        port.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    // ===============================================================
    // 2. MatchGrade enum is EXACTLY the three named rungs (no numeric band)
    // ===============================================================

    [Fact]
    public void MatchGrade_is_in_Application_layer()
    {
        var grade = typeof(Jobbliggaren.Application.Matching.Grading.MatchGrade);
        grade.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void MatchGrade_is_the_locked_three_member_set()
    {
        // ADR 0076 Decision 4 — exactly { Basic, Good, Strong }. A numeric-band member
        // (e.g. a "Score92"/percentage rung) would be the forbidden opaque total.
        var names = Enum.GetNames<Jobbliggaren.Application.Matching.Grading.MatchGrade>();

        names.ShouldBe(["Basic", "Good", "Strong"], ignoreOrder: true,
            "MatchGrade ska vara exakt { Basic, Good, Strong } (ADR 0076 Decision 4). " +
            $"Faktiska: [{string.Join(", ", names)}].");
    }

    // ===============================================================
    // 3. JobAdMatchEntryDto carries EXACTLY the category + four verdicts, NO number
    //    (the Goodhart guard, realised on the wire — ADR 0076 Decision 4)
    // ===============================================================

    [Fact]
    public void JobAdMatchEntryDto_carries_exactly_grade_plus_four_verdicts()
    {
        var entry = typeof(Jobbliggaren.Application.Matching.Queries.GetJobAdMatchBatch.JobAdMatchEntryDto);

        var propNames = PublicInstancePropertyNames(entry);

        propNames.ShouldBe(
            ["Grade", "SsykOverlap", "TitleSimilarity", "RegionFit", "EmploymentFit"],
            ignoreOrder: true,
            "JobAdMatchEntryDto ska bära exakt { Grade, SsykOverlap, TitleSimilarity, " +
            "RegionFit, EmploymentFit } — den namngivna kategorin + de fyra " +
            "dimensions-verdikten, inget mer (ADR 0076 Decision 4). Faktiska: " +
            $"[{string.Join(", ", propNames)}].");
    }

    [Fact]
    public void JobAdMatchEntryDto_has_no_numeric_or_score_shaped_property()
    {
        // The Goodhart guard ON THE WIRE: no opaque number may leak onto the match
        // overlay. Grade is a named category, the four dims are enum verdicts — none
        // matches the forbidden numeric-name regex.
        var entry = typeof(Jobbliggaren.Application.Matching.Queries.GetJobAdMatchBatch.JobAdMatchEntryDto);

        var offending = PublicInstancePropertyNames(entry)
            .Where(name => ForbiddenNumericName.IsMatch(name))
            .ToList();

        offending.ShouldBeEmpty(
            "JobAdMatchEntryDto får INTE bära ett numeriskt/score-format fält " +
            "(Score/Value/Total/Percent/SortKey/Rank/Intensity/Points) — Goodhart-" +
            $"vakten på tråden (ADR 0076 Decision 4). Otillåtna: [{string.Join(", ", offending)}].");
    }

    [Fact]
    public void JobAdMatchBatchDto_has_no_numeric_or_score_shaped_property()
    {
        // The batch wrapper carries only the Entries map — likewise no opaque total.
        var dto = typeof(Jobbliggaren.Application.Matching.Queries.GetJobAdMatchBatch.JobAdMatchBatchDto);

        var offending = PublicInstancePropertyNames(dto)
            .Where(name => ForbiddenNumericName.IsMatch(name))
            .ToList();

        offending.ShouldBeEmpty(
            "JobAdMatchBatchDto får INTE bära ett numeriskt/score-format fält " +
            $"(Goodhart-vakten på tråden, ADR 0076 Decision 4). Otillåtna: [{string.Join(", ", offending)}].");
    }

    [Fact]
    public void JobAdMatchEntryDto_Grade_is_the_named_MatchGrade_category_not_a_number()
    {
        // Belt-and-braces: the tag field is the named enum, never an int/double/decimal.
        var entry = typeof(Jobbliggaren.Application.Matching.Queries.GetJobAdMatchBatch.JobAdMatchEntryDto);
        var grade = entry.GetProperty("Grade", BindingFlags.Public | BindingFlags.Instance);

        grade.ShouldNotBeNull("JobAdMatchEntryDto ska ha en Grade-property.");
        grade!.PropertyType.ShouldBe(
            typeof(Jobbliggaren.Application.Matching.Grading.MatchGrade),
            "Grade ska vara den namngivna MatchGrade-kategorin, aldrig en numerisk typ.");
        grade.PropertyType.IsEnum.ShouldBeTrue();
    }

    // ===============================================================
    // 4. Confirm the existing F4-5 shape pins are untouched (we don't modify them —
    //    this just fails loud if the upstream MatchScore/MatchDimension shape drifts,
    //    since the F4-13 grade ladder reads exactly those verdicts).
    // ===============================================================

    [Fact]
    public void MatchScore_still_carries_exactly_the_four_F4_5_dimensions()
    {
        var score = typeof(Jobbliggaren.Application.Matching.Abstractions.MatchScore);

        var propNames = PublicInstancePropertyNames(score);

        propNames.ShouldBe(
            ["SsykOverlap", "TitleSimilarity", "RegionFit", "EmploymentFit"],
            ignoreOrder: true,
            "MatchScore-formen (som grade-stegen läser) ska vara oförändrad — exakt de " +
            $"fyra F4-5-dimensionerna. Faktiska: [{string.Join(", ", propNames)}].");
    }

    [Fact]
    public void MatchDimensionVerdict_still_is_the_locked_four_member_set()
    {
        var names = Enum.GetNames<
            Jobbliggaren.Application.Matching.Abstractions.MatchDimensionVerdict>();

        names.ShouldBe(["Match", "Partial", "NoMatch", "NotAssessed"], ignoreOrder: true,
            "MatchDimensionVerdict ska vara oförändrad { Match, Partial, NoMatch, " +
            $"NotAssessed }}. Faktiska: [{string.Join(", ", names)}].");
    }
}
