using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Fas 4 STEG 6 (F4-6) anti-regression — the deterministic FULL matching engine
/// respects Clean Architecture and extends the F4-5 Goodhart guard to the new
/// types (ADR 0074 row U5b; senior-cto-advisor Decision A = A2 + Pa, bindande).
/// The new BCL-only Application types (<c>FullMatchScore</c>,
/// <c>FullCandidateMatchProfile</c>) live beside the F4-5 abstractions in
/// <c>Matching/Abstractions/</c>; the second port method <c>ScoreFullAsync</c>
/// is added to the existing <c>IMatchScorer</c> and implemented on the existing
/// <c>internal sealed MatchScorer</c> in Infrastructure.
///
/// CTO Decision A (Goodhart guard extended): <c>FullMatchScore</c> carries NO
/// opaque total (<c>Value: 0-100</c>) and NO numeric aggregate — exactly the
/// embedded <c>MatchScore</c> + the three named <c>MatchDimension</c> props —
/// pinned BY SHAPE below so a sneak-total or an unwanted dimension cannot creep
/// in. The F4-5 4-prop pin on <c>MatchScore</c> stays untouched (A2 does not
/// modify the frozen type).
///
/// Mirrors MatchScorerLayerTests (the F4-5 pin file).
///
/// RED until FullMatchScore + FullCandidateMatchProfile ship in Application and
/// ScoreFullAsync ships on MatchScorer in Infrastructure.
/// </summary>
public class FullMatchScorerLayerTests
{
    // ===============================================================
    // 1. New result/input types live in the Application assembly
    // ===============================================================

    [Fact]
    public void FullMatchScore_is_in_Application_layer()
    {
        var dto = typeof(Jobbliggaren.Application.Matching.Abstractions.FullMatchScore);
        dto.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void FullCandidateMatchProfile_is_in_Application_layer()
    {
        var dto = typeof(Jobbliggaren.Application.Matching.Abstractions.FullCandidateMatchProfile);
        dto.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    [Fact]
    public void FullScoredMatch_is_in_Application_layer()
    {
        // PR-4 (#300, ADR 0084): the new FULL-scorer carrier lives beside the F4-6 abstractions
        // (BCL-only — no Domain/EF/Npgsql type crosses the port surface, parity FullMatchScore).
        var dto = typeof(Jobbliggaren.Application.Matching.Abstractions.FullScoredMatch);
        dto.Assembly.ShouldBe(
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly);
    }

    // ===============================================================
    // 2. Goodhart guard extended to F4-6, pinned BY SHAPE (CTO Decision A)
    // ===============================================================

    [Fact]
    public void FullMatchScore_carries_exactly_Fast_plus_the_three_F4_6_dimensions_no_total()
    {
        // CTO Decision A (A2): FullMatchScore EMBEDS MatchScore (the four Fast
        // dims) + exactly { SkillOverlap, MustHaveCoverage, NiceToHaveCoverage }.
        // NO top-level numeric total (Value/Score: 0-100) and NO extra/merged
        // dimension — a sneak-total or a keyword dim (deferred to F4-8/9) would
        // signal a rejected branch crept in.
        var score = typeof(Jobbliggaren.Application.Matching.Abstractions.FullMatchScore);

        var propNames = score
            .GetProperties(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name)
            .ToList();

        propNames.ShouldBe(
            ["Fast", "SkillOverlap", "MustHaveCoverage", "NiceToHaveCoverage"],
            ignoreOrder: true,
            "FullMatchScore ska bära exakt { Fast (embeddad MatchScore), " +
            "SkillOverlap, MustHaveCoverage, NiceToHaveCoverage } utan opak total " +
            "(Value) och utan extra/keyword-dim (F4-8/9, CTO Decision A/E). " +
            $"Faktiska: [{string.Join(", ", propNames)}].");
    }

    [Fact]
    public void FullScoredMatch_carries_exactly_Score_SsykIsRelated_and_MatchedSkillEvidence()
    {
        // PR-4 (#300, ADR 0084 — PR-2 bind) + #477 Low 2: the FULL-scorer carrier is exactly
        // { Score (the frozen FullMatchScore), SsykIsRelated, MatchedSkillConceptIds }. Both
        // extra members ride BESIDE the Goodhart-frozen FullMatchScore, never inside it:
        // SsykIsRelated is a CATEGORICAL bool (a ladder BRANCH — the MatchGrade.Related flat cap),
        // and MatchedSkillConceptIds is a string-list EVIDENCE payload (the covered-skill
        // concept-ids the background scan persists into UserJobAdMatch, #477 Low 2). NEITHER is a
        // magnitude — neither blends into a number. Pinned BY SHAPE so a sneak-total or an extra
        // scoring dimension still cannot creep onto the carrier; a string-list of matched ids is
        // explicitly the ALLOWED explainability shape (parity UserJobAdMatchGoodhartTests, which
        // bless "the evidence is a string list").
        var carrier = typeof(Jobbliggaren.Application.Matching.Abstractions.FullScoredMatch);

        var propNames = carrier
            .GetProperties(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name)
            .ToList();

        propNames.ShouldBe(
            ["Score", "SsykIsRelated", "MatchedSkillConceptIds"],
            ignoreOrder: true,
            "FullScoredMatch ska bära EXAKT { Score (frusen FullMatchScore), SsykIsRelated, " +
            "MatchedSkillConceptIds } — ingen extra scoring-dimension och ingen opak total. " +
            "SsykIsRelated är en KATEGORISK ladder-gren (MatchGrade.Related-cap); " +
            "MatchedSkillConceptIds är string-list-EVIDENS (matchade skill-concept-ids), ingen " +
            $"magnitud. Faktiska: [{string.Join(", ", propNames)}].");
    }

    [Fact]
    public void FullScoredMatch_Score_is_frozen_SsykIsRelated_is_bool_MatchedSkills_is_string_list()
    {
        // Composition + categorical-branch + evidence shape: Score IS the frozen FullMatchScore
        // type (DRY at the knowledge level — not a flat re-declaration), SsykIsRelated is a plain
        // bool (a ladder branch, never a numeric magnitude the Goodhart guard forbids), and
        // MatchedSkillConceptIds is IReadOnlyList<string> — an EVIDENCE payload (matched skill
        // concept-ids), NOT a numeric type a magnitude could hide behind (#477 Low 2).
        var carrier = typeof(Jobbliggaren.Application.Matching.Abstractions.FullScoredMatch);

        var score = carrier.GetProperty("Score",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        score.ShouldNotBeNull("FullScoredMatch ska ha en Score-property (embeddad FullMatchScore).");
        score!.PropertyType.ShouldBe(
            typeof(Jobbliggaren.Application.Matching.Abstractions.FullMatchScore),
            "FullScoredMatch.Score ska vara den frusna FullMatchScore-typen (komposition).");

        var isRelated = carrier.GetProperty("SsykIsRelated",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        isRelated.ShouldNotBeNull();
        isRelated!.PropertyType.ShouldBe(typeof(bool),
            "SsykIsRelated ska vara en bool (kategorisk ladder-gren, ingen magnitud).");

        var matchedSkills = carrier.GetProperty("MatchedSkillConceptIds",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        matchedSkills.ShouldNotBeNull("FullScoredMatch ska ha en MatchedSkillConceptIds-property.");
        matchedSkills!.PropertyType.ShouldBe(typeof(IReadOnlyList<string>),
            "MatchedSkillConceptIds ska vara IReadOnlyList<string> — string-list-evidens " +
            "(matchade skill-concept-ids), aldrig en numerisk typ en magnitud kan gömma sig bakom.");
    }

    [Fact]
    public void FullMatchScore_embeds_the_frozen_F4_5_MatchScore_for_Fast()
    {
        // A2 = composition: the Fast property IS the frozen F4-5 MatchScore type,
        // not a re-declared flat copy (DRY at the knowledge level — CTO Decision A).
        var fast = typeof(Jobbliggaren.Application.Matching.Abstractions.FullMatchScore)
            .GetProperty("Fast",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance);

        fast.ShouldNotBeNull("FullMatchScore ska ha en Fast-property (embeddad MatchScore).");
        fast!.PropertyType.ShouldBe(
            typeof(Jobbliggaren.Application.Matching.Abstractions.MatchScore),
            "FullMatchScore.Fast ska vara den frusna F4-5 MatchScore-typen " +
            "(komposition A2 — inte en platt om-deklaration).");
    }

    [Fact]
    public void FullMatchScore_the_three_new_dimensions_are_MatchDimension()
    {
        // The three new dims reuse the pinned { Verdict, Matched, Missing } shape
        // verbatim (DD-shape-1 — Source bärs av ad-VO:n, inte av dimensionen).
        var score = typeof(Jobbliggaren.Application.Matching.Abstractions.FullMatchScore);
        var matchDimension = typeof(Jobbliggaren.Application.Matching.Abstractions.MatchDimension);

        foreach (var name in new[] { "SkillOverlap", "MustHaveCoverage", "NiceToHaveCoverage" })
        {
            var prop = score.GetProperty(name,
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance);
            prop.ShouldNotBeNull($"FullMatchScore ska ha en {name}-property.");
            prop!.PropertyType.ShouldBe(matchDimension,
                $"FullMatchScore.{name} ska återanvända den pinnade MatchDimension-" +
                "formen (DD-shape-1).");
        }
    }

    [Fact]
    public void FullCandidateMatchProfile_carries_exactly_Fast_and_CvSkillConceptIds()
    {
        // CTO Decision B (B2, skill-only v1): the Full profile EMBEDS the frozen
        // Fast profile + exactly CvSkillConceptIds. NO CvKeywordLexemes v1
        // (deferred to F4-8/9 — no CV-free-text producer exists; omitted, not a
        // NotAssessed placeholder).
        var profile = typeof(Jobbliggaren.Application.Matching.Abstractions.FullCandidateMatchProfile);

        var propNames = profile
            .GetProperties(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name)
            .ToList();

        propNames.ShouldBe(["Fast", "CvSkillConceptIds"], ignoreOrder: true,
            "FullCandidateMatchProfile ska bära exakt { Fast (embeddad " +
            "CandidateMatchProfile), CvSkillConceptIds } — ingen CvKeywordLexemes " +
            "v1 (F4-8/9, CTO Decision B). " +
            $"Faktiska: [{string.Join(", ", propNames)}].");
    }

    [Fact]
    public void FullCandidateMatchProfile_embeds_the_frozen_Fast_profile()
    {
        var fast = typeof(Jobbliggaren.Application.Matching.Abstractions.FullCandidateMatchProfile)
            .GetProperty("Fast",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance);

        fast.ShouldNotBeNull(
            "FullCandidateMatchProfile ska ha en Fast-property (embeddad CandidateMatchProfile).");
        fast!.PropertyType.ShouldBe(
            typeof(Jobbliggaren.Application.Matching.Abstractions.CandidateMatchProfile),
            "FullCandidateMatchProfile.Fast ska vara den frusna F4-5 " +
            "CandidateMatchProfile-typen (B2 — inte en platt om-deklaration).");
    }

    [Fact]
    public void FullCandidateMatchProfile_CvSkillConceptIds_is_a_readonly_string_list()
    {
        // BCL-only carrier — IReadOnlyList<string> of taxonomy concept-ids
        // (no Domain/EF/Npgsql type on the port surface).
        var prop = typeof(Jobbliggaren.Application.Matching.Abstractions.FullCandidateMatchProfile)
            .GetProperty("CvSkillConceptIds",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance);

        prop.ShouldNotBeNull();
        prop!.PropertyType.ShouldBe(typeof(IReadOnlyList<string>),
            "CvSkillConceptIds ska vara IReadOnlyList<string> (BCL-only " +
            "concept-id-bärare).");
    }

    // ===============================================================
    // 3. The F4-5 4-prop pin on MatchScore stays GREEN (A2 rör inte den frusna typen)
    // ===============================================================

    [Fact]
    public void MatchScore_still_carries_exactly_the_four_F4_5_dimensions()
    {
        // Regression guard: A2 (compose, do not extend) must leave MatchScore at
        // its frozen four props — the F4-5 pin (MatchScorerLayerTests) must stay
        // GREEN. Asserted here too so a Full-mode change that wrongly widens
        // MatchScore fails in BOTH files.
        var score = typeof(Jobbliggaren.Application.Matching.Abstractions.MatchScore);

        var propNames = score
            .GetProperties(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract")
            .Select(p => p.Name)
            .ToList();

        propNames.ShouldBe(
            ["SsykOverlap", "TitleSimilarity", "RegionFit", "EmploymentFit"],
            ignoreOrder: true,
            "MatchScore ska FÖRBLI exakt de fyra F4-5-dimensionerna — F4-6 (A2) " +
            "embeddar den, växer den inte. " +
            $"Faktiska: [{string.Join(", ", propNames)}].");
    }

    // ===============================================================
    // 4. The new port method + types do NOT leak EF/Npgsql/NLP into Application
    //    (the new dims are read from the VO in Infrastructure; the port stays
    //    BCL + Domain-id only — parity the F4-5 dependency pins)
    // ===============================================================

    [Fact]
    public void Application_should_not_depend_on_EfCore_or_Npgsql()
    {
        // ScoreFullAsync loads the extracted_terms VO + shadow columns in
        // Infrastructure; no EF/Npgsql type may cross into Application via the new
        // port method or the new types (Clean Arch, CLAUDE.md §2.1 / ADR 0062).
        // JobAdId (the only Domain type on the port) is BCL-bound.
        var result = Types.InAssembly(
                typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Npgsql",
                "NpgsqlTypes",
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                "Microsoft.EntityFrameworkCore.Relational")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Application läcker mot Npgsql/EF-relational via F4-6-porten " +
            "(Clean Arch, CLAUDE.md §2.1): " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Application_should_not_depend_on_NLP_libraries()
    {
        // The Fast embedding's title pass uses Snowball inside the Infrastructure
        // scorer; the FULL types and the new port method must NOT drag
        // Snowball/WeCantSpell across the Application boundary.
        var result = Types.InAssembly(
                typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Snowball", "WeCantSpell.Hunspell", "WeCantSpell")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "Application läcker mot NLP-bibliotek (Snowball/WeCantSpell) via " +
            "F4-6-porten — den ska vara BCL-only: " +
            $"{string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    // ===============================================================
    // 5. The new types do NOT depend on the Domain JobAds extraction VO
    //    (FullMatchScore/FullCandidateMatchProfile are BCL-only — the ad's
    //    ExtractedTerm(s) live in Domain and are read ONLY in Infrastructure;
    //    the port surface never exposes them)
    // ===============================================================

    [Fact]
    public void Full_match_types_do_not_expose_the_Domain_ExtractedTerms_VO()
    {
        // DE-display-1 + the port-purity rule: the ad-side ExtractedTerm(s) VO is a
        // Domain persistence type read only by the Infrastructure scorer; the
        // result surfaces Display labels as plain strings. Neither new type may
        // carry an ExtractedTerm/ExtractedTerms property (it would leak the
        // Domain persistence shape across the read-model port).
        foreach (var type in new[]
        {
            typeof(Jobbliggaren.Application.Matching.Abstractions.FullMatchScore),
            typeof(Jobbliggaren.Application.Matching.Abstractions.FullCandidateMatchProfile),
        })
        {
            var leaks = type
                .GetProperties(
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance)
                .Where(p => p.PropertyType.Namespace == "Jobbliggaren.Domain.JobAds")
                .Select(p => $"{type.Name}.{p.Name}:{p.PropertyType.Name}")
                .ToList();

            leaks.ShouldBeEmpty(
                $"{type.Name} läcker en Domain.JobAds-VO över porten (förväntat: " +
                "Display-strängar, inte ExtractedTerm/ExtractedTerms): " +
                $"{string.Join(", ", leaks)}");
        }
    }
}
