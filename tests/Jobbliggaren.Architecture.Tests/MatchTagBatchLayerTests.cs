using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// F4-13 (ADR 0076 Decision 5; senior-cto-advisor 2026-06-19 A1/B2/C2a) anti-regression
/// for the page-scoped match-tag batch overlay. Mirrors the Goodhart pins in
/// <see cref="MatchScorerLayerTests"/> but for the F4-13 wire surface: the batch DTO + the
/// per-entry DTO live in Application, carry EXACTLY the named-category + four verdicts and
/// NO numeric/score field (ADR 0076 Decision 4 / ADR 0071 / CLAUDE.md §5 — the Goodhart
/// guard realised ON THE WIRE), and the <see cref="Jobbliggaren.Application.Matching.Grading.MatchGrade"/>
/// enum is exactly the four named rungs (no numeric band). The existing
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
    // 2. MatchGrade enum is EXACTLY the four named rungs (no numeric band)
    // ===============================================================

    [Fact]
    public void MatchGrade_is_in_Application_layer()
    {
        var grade = typeof(Jobbliggaren.Application.Matching.Grading.MatchGrade);
        grade.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void MatchGrade_is_the_locked_four_member_set()
    {
        // F4-16 golden rung (ADR 0076 Amendment (b) §1 rework-free upward extension) —
        // exactly { Basic, Good, Strong, Top }. Top ("Toppmatch", Klas-bound name) is the
        // new highest rung ABOVE Strong; the F4-13 three are unchanged. Still NO
        // numeric-band member (e.g. a "Score92"/percentage rung would be the forbidden
        // opaque total — the Goodhart intent is preserved, the ladder just grew one
        // named rung upward).
        var names = Enum.GetNames<Jobbliggaren.Application.Matching.Grading.MatchGrade>();

        names.ShouldBe(["Basic", "Good", "Strong", "Top"], ignoreOrder: true,
            "MatchGrade ska vara exakt { Basic, Good, Strong, Top } (F4-16 golden rung, " +
            "ADR 0076 Amendment (b) §1 rework-free upward extension). " +
            $"Faktiska: [{string.Join(", ", names)}].");
    }

    // ===============================================================
    // 3. JobAdMatchEntryDto carries EXACTLY the category + four verdicts, NO number
    //    (the Goodhart guard, realised on the wire — ADR 0076 Decision 4)
    // ===============================================================

    [Fact]
    public void JobAdMatchEntryDto_carries_exactly_grade_plus_seven_verdicts()
    {
        // F4-15 (ADR 0076 Decision 6) — the entry DTO widens with the THREE new FULL
        // verdicts (SkillOverlap, MustHaveCoverage, NiceToHaveCoverage) appended to the
        // F4-13 four. Still NO number on the wire (the Goodhart guard, pinned below) — the
        // grade remains the single named category, the seven dims are enum verdicts.
        var entry = typeof(Jobbliggaren.Application.Matching.Queries.GetJobAdMatchBatch.JobAdMatchEntryDto);

        var propNames = PublicInstancePropertyNames(entry);

        propNames.ShouldBe(
            [
                "Grade",
                "SsykOverlap", "TitleSimilarity", "RegionFit", "EmploymentFit",
                "SkillOverlap", "MustHaveCoverage", "NiceToHaveCoverage",
            ],
            ignoreOrder: true,
            "JobAdMatchEntryDto ska bära exakt { Grade + de fyra F4-13-verdikten + de tre " +
            "F4-15-verdikten (SkillOverlap, MustHaveCoverage, NiceToHaveCoverage) } — inget " +
            $"mer (ADR 0076 Decision 4/6). Faktiska: [{string.Join(", ", propNames)}].");
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
    // 3b. F4-16 modal detail DTOs (ADR 0076 (b) §3; CTO 2026-06-20 D3) — the modal is
    //     its OWN altitude: a single-ad DTO carrying the named grade + per-dimension
    //     {verdict, matched[], missing[]}. They live in Application and carry NO
    //     numeric/score field (the modal is the most tempting place for a "92%" to creep
    //     back — ADR 0053 Beslut 5 forbids the ring by name; the arch pin makes it
    //     structurally impossible). matched/missing are evidence string lists, not magnitude.
    // ===============================================================

    [Fact]
    public void JobAdMatchDetailDto_is_in_Application_layer()
    {
        var dto = typeof(Jobbliggaren.Application.Matching.Queries.GetJobAdMatchDetail.JobAdMatchDetailDto);
        dto.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void MatchDimensionDetailDto_is_in_Application_layer()
    {
        var dto = typeof(Jobbliggaren.Application.Matching.Queries.GetJobAdMatchDetail.MatchDimensionDetailDto);
        dto.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void JobAdMatchDetailDto_has_no_numeric_or_score_shaped_property()
    {
        // The Goodhart guard ON THE MODAL WIRE: no opaque number may leak onto the modal
        // detail. Grade is a named category, each dimension row is a verdict + two string
        // lists — none matches the forbidden numeric-name regex.
        var dto = typeof(Jobbliggaren.Application.Matching.Queries.GetJobAdMatchDetail.JobAdMatchDetailDto);

        var offending = PublicInstancePropertyNames(dto)
            .Where(name => ForbiddenNumericName.IsMatch(name))
            .ToList();

        offending.ShouldBeEmpty(
            "JobAdMatchDetailDto får INTE bära ett numeriskt/score-format fält " +
            "(Score/Value/Total/Percent/SortKey/Rank/Intensity/Points) — Goodhart-vakten " +
            "på modal-tråden (ADR 0076 Decision 4 / ADR 0053 Beslut 5). " +
            $"Otillåtna: [{string.Join(", ", offending)}].");
    }

    [Fact]
    public void MatchDimensionDetailDto_has_no_numeric_or_score_shaped_property()
    {
        // The per-dimension row carries verdict + matched[] + missing[], NO number — the
        // evidence IS the explanation, never a magnitude (CLAUDE.md §5 / ADR 0071).
        var dto = typeof(Jobbliggaren.Application.Matching.Queries.GetJobAdMatchDetail.MatchDimensionDetailDto);

        var offending = PublicInstancePropertyNames(dto)
            .Where(name => ForbiddenNumericName.IsMatch(name))
            .ToList();

        offending.ShouldBeEmpty(
            "MatchDimensionDetailDto får INTE bära ett numeriskt/score-format fält — " +
            "raden är verdict + matched[] + missing[], aldrig en siffra (Goodhart-vakten, " +
            $"ADR 0076 Decision 4). Otillåtna: [{string.Join(", ", offending)}].");
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
    public void MatchDimensionVerdict_still_is_the_locked_five_member_set()
    {
        // PR-B1 (senior-cto-advisor 2026-06-20 RE-BIND G1-b / G3.5) — the requirement-aware
        // ladder adds the 5th verdict Vacuous (ad partition empty, CV present → gate-open).
        // Still NO numeric-band member (the Goodhart guard holds).
        var names = Enum.GetNames<
            Jobbliggaren.Application.Matching.Abstractions.MatchDimensionVerdict>();

        names.ShouldBe(["Match", "Partial", "NoMatch", "NotAssessed", "Vacuous"], ignoreOrder: true,
            "MatchDimensionVerdict ska vara { Match, Partial, NoMatch, NotAssessed, " +
            $"Vacuous }} (PR-B1 RE-BIND G1-b). Faktiska: [{string.Join(", ", names)}].");
    }

    // ===============================================================
    // 5. F4-14 "Sortera efter matchning" (ADR 0076 Decision 4) — sort-nyckeln
    //    (grad-ranken) lever ENBART i ORDER BY. Den list-wire-DTO som match-sorten
    //    returnerar (JobAdDto) får INTE läcka ett score-format fält, och porten
    //    introducerar INGEN match-formad DTO (samma JobAdDto som default-sorten).
    //    Goodhart-vakten realiserad på match-sort-tråden.
    // ===============================================================

    [Fact]
    public void JobAdDto_has_no_numeric_or_score_shaped_property()
    {
        // ADR 0076 Decision 4 — F4-14:s sort-nyckel (grad-ranken) lever ENBART i
        // PerUserJobAdSearchQuery.OrderBy; den projiceras ALDRIG in i den list-
        // wire-DTO som /jobb returnerar. JobAdDto är den exakt samma DTO:n vare sig
        // sorten är PublishedAtDesc eller MatchDesc (Decision 5 — match-sorten
        // reordnar, introducerar ingen match-shaped wire-form).
        var dto = typeof(Jobbliggaren.Application.JobAds.Queries.JobAdDto);

        var offending = PublicInstancePropertyNames(dto)
            .Where(name => ForbiddenNumericName.IsMatch(name))
            .ToList();

        offending.ShouldBeEmpty(
            "JobAdDto (list-wire-DTO:n för /jobb) får INTE bära ett numeriskt/score-" +
            "format fält (Score/Value/Total/Percent/SortKey/Rank/Intensity/Points) — " +
            "F4-14:s sort-nyckel läcker aldrig in i DTO:n (ADR 0076 Decision 4). " +
            $"Otillåtna: [{string.Join(", ", offending)}].");
    }

    [Fact]
    public void IPerUserJobAdSearchQuery_SearchByMatch_returns_PagedResult_of_JobAdDto()
    {
        // ADR 0076 Decision 4/5 — match-sort-porten returnerar EXAKT samma sida som
        // default-sorten: PagedResult<JobAdDto>. Ingen match-formad DTO (med ett
        // grad-/rank-fält) introduceras på tråden; ordningen är den enda skillnaden.
        var method = typeof(Jobbliggaren.Application.JobAds.Abstractions.IPerUserJobAdSearchQuery)
            .GetMethod("SearchPerUserAsync", BindingFlags.Public | BindingFlags.Instance);

        method.ShouldNotBeNull(
            "IPerUserJobAdSearchQuery ska ha SearchPerUserAsync.");

        var returnType = method!.ReturnType;
        // ValueTask<PagedResult<JobAdDto>> — unwrap ValueTask<T> → T.
        returnType.IsGenericType.ShouldBeTrue();
        returnType.GetGenericTypeDefinition().ShouldBe(typeof(ValueTask<>));

        var paged = returnType.GetGenericArguments()[0];
        paged.IsGenericType.ShouldBeTrue();
        paged.GetGenericTypeDefinition().ShouldBe(
            typeof(Jobbliggaren.Application.Common.PagedResult<>),
            "SearchPerUserAsync ska returnera PagedResult<…> (samma sid-form som " +
            "default-sorten, ADR 0076 Decision 5).");
        paged.GetGenericArguments()[0].ShouldBe(
            typeof(Jobbliggaren.Application.JobAds.Queries.JobAdDto),
            "Sid-elementet ska vara JobAdDto — ingen match-shaped DTO på tråden " +
            "(Goodhart-vakten, ADR 0076 Decision 4).");
    }

    [Fact]
    public void IPerUserJobAdSearchQuery_is_in_Application_layer()
    {
        // Match-sort-porten är en Application-abstraktion (impl internal i
        // Infrastructure — Npgsql-bunden ORDER BY, ADR 0062 / CLAUDE.md §2.1).
        var port = typeof(Jobbliggaren.Application.JobAds.Abstractions.IPerUserJobAdSearchQuery);
        port.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    // ===============================================================
    // 6. F4-15 (ADR 0076 Decision 6) — ONE shared skill index, NO parallel
    //    resolver. The CV-side ISkillResolver reuses the SAME inverted index the
    //    ad-side extractor uses; the loader of that index (JobAdSkillTaxonomyLoader)
    //    must be referenced by EXACTLY ONE Infrastructure type — the shared
    //    SkillTaxonomyIndex — never a second resolver that builds its own index.
    // ===============================================================

    [Fact]
    public void ISkillResolver_is_in_Application_layer()
    {
        // The CV-side resolver port is a BCL-only Application abstraction (string in,
        // concept-ids out); the impl is internal in Infrastructure (embedded taxonomy +
        // Snowball NLP), parity IJobAdKeywordExtractor.
        var port = typeof(Jobbliggaren.Application.Matching.Abstractions.ISkillResolver);
        port.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void Only_the_shared_SkillTaxonomyIndex_references_the_skill_taxonomy_loader()
    {
        // NO parallel skill index (ADR 0076 Decision 6): JobAdSkillTaxonomyLoader.Load is
        // the single entry point to the embedded skill vocabulary. Exactly ONE type may
        // depend on it — the shared SkillTaxonomyIndex (which both the ad-side extractor
        // and the CV-side resolver reuse). If a SECOND type (e.g. a parallel resolver
        // index) referenced the loader, the two indices could silently diverge.
        const string loaderFullName =
            "Jobbliggaren.Infrastructure.Taxonomy.JobAdSkillTaxonomyLoader";

        var infrastructureAsm = typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;

        var referencingTypes = Types.InAssembly(infrastructureAsm)
            .That()
            .HaveDependencyOn(loaderFullName)
            .GetTypes()
            // Exclude the loader itself + compiler-generated closures/nested helpers.
            .Where(t => t.FullName != loaderFullName && !t.Name.Contains('<', StringComparison.Ordinal))
            .Select(t => t.Name)
            .ToList();

        referencingTypes.ShouldBe(["SkillTaxonomyIndex"], ignoreOrder: true,
            "Exakt EN typ (SkillTaxonomyIndex) får referera JobAdSkillTaxonomyLoader — " +
            "ingen parallell skill-resolver-index (ADR 0076 Decision 6). Faktiska: " +
            $"[{string.Join(", ", referencingTypes)}].");
    }

    [Fact]
    public void SkillResolver_does_not_reference_the_skill_taxonomy_loader_directly()
    {
        // Sharper: the resolver must go THROUGH the shared index, never build its own —
        // so it must NOT reference the loader at all.
        var infrastructureAsm = typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;

        var result = Types.InAssembly(infrastructureAsm)
            .That()
            .HaveName("SkillResolver")
            .ShouldNot()
            .HaveDependencyOn("Jobbliggaren.Infrastructure.Taxonomy.JobAdSkillTaxonomyLoader")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "SkillResolver ska gå genom den delade SkillTaxonomyIndex, aldrig ladda " +
            $"taxonomin själv (ADR 0076 Decision 6): {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void SkillResolver_and_SkillTaxonomyIndex_are_internal_to_Infrastructure()
    {
        // Parity JobAdKeywordExtractor: the impls are internal sealed (ACL-isolation,
        // ADR 0043). Proven BY NAME so RED requires the types to exist AND be non-public.
        var infrastructureAsm = typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly;

        var skillTypes = infrastructureAsm.GetTypes()
            .Where(t => t.Namespace == "Jobbliggaren.Infrastructure.Taxonomy"
                        && (t.Name == "SkillResolver" || t.Name == "SkillTaxonomyIndex"))
            .ToList();

        skillTypes.ShouldContain(t => t.Name == "SkillResolver",
            "SkillResolver saknas i Jobbliggaren.Infrastructure.Taxonomy (F4-15 RED).");
        skillTypes.ShouldContain(t => t.Name == "SkillTaxonomyIndex",
            "SkillTaxonomyIndex saknas i Jobbliggaren.Infrastructure.Taxonomy (F4-15 RED).");

        var publicSkillTypes = skillTypes
            .Where(t => t.IsPublic || (t.IsNested && t.IsNestedPublic))
            .Select(t => t.FullName)
            .ToList();

        publicSkillTypes.ShouldBeEmpty(
            "SkillResolver + SkillTaxonomyIndex ska vara internal (ACL-isolation, ADR 0043). " +
            $"Public: {string.Join(", ", publicSkillTypes!)}");
    }
}
