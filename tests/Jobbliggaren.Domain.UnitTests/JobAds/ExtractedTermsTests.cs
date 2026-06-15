using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobAds;

/// <summary>
/// Fas 4 STEG 4 (F4-4, ADR 0071/0074/0075) + STEG 4b (F4-4b) — the
/// <see cref="ExtractedTerms"/> value object is the single normalization point for
/// the persisted jsonb extraction. <see cref="ExtractedTerms.From"/> validates each
/// term's invariants, deduplicates on (Lexeme, Kind, Source) keeping the highest
/// weight, sorts deterministically and caps at <see cref="ExtractedTerms.MaxTerms"/>.
/// Empty is a valid "not-yet-extracted / nothing matched" state. Malformed terms
/// throw <see cref="ArgumentException"/> (corrupt jsonb / extractor bug surfaces,
/// never silently persists).
///
/// <para>
/// <b>F4-4b additions (RED until the F4-4b production change ships):</b>
/// <list type="bullet">
/// <item>A <see cref="ExtractedTermKind.Requirement"/> term carries a non-blank
/// ConceptId, with <c>Lexeme == ConceptId</c> (concept-level overlap, same rule as
/// Skill) and <c>Source ∈ {MustHave, NiceToHave}</c> — else
/// <see cref="ArgumentException"/>.</item>
/// <item>Skill/Keyword <c>Source</c> is TIGHTENED to <c>∈ {Title, Description}</c> —
/// a Skill/Keyword with Source=MustHave/NiceToHave now throws (closes a silent bug).</item>
/// <item>The primary sort key is NO LONGER raw <c>(int)Kind</c>; it is a Kind→rank:
/// <b>Requirement (0) → Skill (1) → Keyword (2)</b> (CTO Decision 1c — employer
/// requirements are the highest-authority match signal and must survive the cap
/// before NLP-derived Skills/Keywords). Secondary keys unchanged: Weight desc →
/// Lexeme Ordinal → Source.</item>
/// </list>
/// </para>
///
/// Pure Domain — no DB, no NLP. Mirrors SearchCriteriaTests' normalization style.
/// </summary>
public class ExtractedTermsTests
{
    // ---------------------------------------------------------------
    // Term builders — a valid Skill, Keyword and Requirement by default; each
    // factory exposes the field a given test wants to perturb.
    // ---------------------------------------------------------------

    private static ExtractedTerm Skill(
        string conceptId = "1TC7_x8s_V7V",
        string display = "JavaScript",
        ExtractedTermSource source = ExtractedTermSource.Description,
        string matchedOn = "JavaScript",
        double weight = 1)
        // Skill invariant: Lexeme == ConceptId (concept-level overlap token).
        => new(conceptId, display, ExtractedTermKind.Skill, source, matchedOn, conceptId, weight);

    private static ExtractedTerm Keyword(
        string lexeme = "samordn",
        string display = "samordnare",
        ExtractedTermSource source = ExtractedTermSource.Description,
        string matchedOn = "samordnare",
        double weight = 1)
        // Keyword invariant: no ConceptId.
        => new(lexeme, display, ExtractedTermKind.Keyword, source, matchedOn, null, weight);

    // F4-4b — a valid Requirement: Lexeme == ConceptId (concept-level overlap, same
    // as Skill), Source ∈ {MustHave, NiceToHave}, MatchedOn cites the requirement
    // label. ConceptId defaults to a different concept than Skill's so a (concept,
    // Skill) and a (concept, Requirement) term are independent by default.
    private static ExtractedTerm Requirement(
        string conceptId = "Rq01_AbC_Def",
        string display = "C#",
        ExtractedTermSource source = ExtractedTermSource.MustHave,
        string matchedOn = "C#",
        double weight = 0)
        => new(conceptId, display, ExtractedTermKind.Requirement, source, matchedOn, conceptId, weight);

    // ===============================================================
    // Empty / IsEmpty
    // ===============================================================

    [Fact]
    public void Empty_HasNoTerms_AndIsEmpty()
    {
        ExtractedTerms.Empty.Terms.ShouldBeEmpty();
        ExtractedTerms.Empty.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void From_EmptySequence_ReturnsEmptySingleton()
    {
        var result = ExtractedTerms.From([]);

        result.IsEmpty.ShouldBeTrue();
        // Empty input collapses to the canonical Empty instance.
        result.ShouldBeSameAs(ExtractedTerms.Empty);
    }

    [Fact]
    public void From_NonEmptySequence_IsNotEmpty()
    {
        var result = ExtractedTerms.From([Keyword()]);

        result.IsEmpty.ShouldBeFalse();
        result.Terms.Count.ShouldBe(1);
    }

    [Fact]
    public void From_NullSequence_Throws()
    {
        Should.Throw<ArgumentNullException>(() => ExtractedTerms.From(null!));
    }

    // ===============================================================
    // Deduplication — on (Lexeme, Kind, Source), keep max weight
    // ===============================================================

    [Fact]
    public void From_DuplicateIdentity_KeepsHighestWeight()
    {
        var weak = Keyword(lexeme: "system", display: "system", matchedOn: "system", weight: 1);
        var strong = Keyword(lexeme: "system", display: "systemet", matchedOn: "systemet", weight: 5);

        var result = ExtractedTerms.From([weak, strong]);

        result.Terms.Count.ShouldBe(1, "samma (Lexeme,Kind,Source) ska dedupliceras till en term.");
        result.Terms[0].Weight.ShouldBe(5);
        result.Terms[0].Display.ShouldBe("systemet", "den starkaste (högsta vikt) förekomsten vinner.");
    }

    [Fact]
    public void From_DuplicateIdentity_OrderIndependent_KeepsHighestWeight()
    {
        // Strong first, weak second — the dedupe must still keep the strong one
        // (the keep-max-weight rule, not a last-write-wins or first-write-wins).
        var strong = Keyword(lexeme: "system", matchedOn: "systemet", weight: 5);
        var weak = Keyword(lexeme: "system", matchedOn: "system", weight: 1);

        var result = ExtractedTerms.From([strong, weak]);

        result.Terms.Count.ShouldBe(1);
        result.Terms[0].Weight.ShouldBe(5);
    }

    [Fact]
    public void From_SameLexemeDifferentKind_AreNotDeduplicated()
    {
        // Identity is (Lexeme, Kind, Source). A Skill and a Keyword that happen to
        // share a lexeme string are distinct terms. (Skill's lexeme is its
        // concept-id; we make the keyword's lexeme equal that string deliberately.)
        var skill = Skill(conceptId: "abc", display: "Skill", matchedOn: "Skill");
        var keyword = Keyword(lexeme: "abc", display: "abc", matchedOn: "abc");

        var result = ExtractedTerms.From([skill, keyword]);

        result.Terms.Count.ShouldBe(2);
    }

    [Fact]
    public void From_SameLexemeAndKindDifferentSource_AreNotDeduplicated()
    {
        var inTitle = Keyword(lexeme: "system", source: ExtractedTermSource.Title, matchedOn: "system");
        var inDesc = Keyword(lexeme: "system", source: ExtractedTermSource.Description, matchedOn: "system");

        var result = ExtractedTerms.From([inTitle, inDesc]);

        result.Terms.Count.ShouldBe(2, "olika Source ⇒ olika identitet ⇒ ingen dedupe.");
    }

    // ===============================================================
    // Deterministic ordering — Kind → Weight desc → Lexeme Ordinal → Source
    // ===============================================================

    [Fact]
    public void From_OrdersSkillsBeforeKeywords()
    {
        // The Kind→rank sort key puts Skill (rank 1) before Keyword (rank 2) — a
        // high-value skill survives the cap before a generic keyword. (Still true
        // under F4-4b's Requirement→Skill→Keyword rank; this test pins Skill<Keyword.)
        var keyword = Keyword(lexeme: "system", weight: 99);
        var skill = Skill(conceptId: "abc", weight: 1);

        var result = ExtractedTerms.From([keyword, skill]);

        result.Terms[0].Kind.ShouldBe(ExtractedTermKind.Skill);
        result.Terms[1].Kind.ShouldBe(ExtractedTermKind.Keyword);
    }

    [Fact]
    public void From_WithinKind_OrdersByWeightDescending()
    {
        var light = Keyword(lexeme: "aaa", matchedOn: "aaa", weight: 1);
        var heavy = Keyword(lexeme: "bbb", matchedOn: "bbb", weight: 9);

        var result = ExtractedTerms.From([light, heavy]);

        // Higher weight first, even though "aaa" < "bbb" Ordinally.
        result.Terms[0].Lexeme.ShouldBe("bbb");
        result.Terms[1].Lexeme.ShouldBe("aaa");
    }

    [Fact]
    public void From_EqualWeight_OrdersByLexemeOrdinal()
    {
        var z = Keyword(lexeme: "zebra", matchedOn: "zebra", weight: 3);
        var a = Keyword(lexeme: "alpha", matchedOn: "alpha", weight: 3);

        var result = ExtractedTerms.From([z, a]);

        // Equal weight → Lexeme Ordinal ascending.
        result.Terms[0].Lexeme.ShouldBe("alpha");
        result.Terms[1].Lexeme.ShouldBe("zebra");
    }

    [Fact]
    public void From_EqualWeightAndLexeme_OrdersBySource()
    {
        // Same Kind + Weight + Lexeme (distinct by Source) → Source ordinal asc.
        // Title (0) before Description (1).
        var desc = Keyword(lexeme: "system", source: ExtractedTermSource.Description, matchedOn: "system", weight: 2);
        var title = Keyword(lexeme: "system", source: ExtractedTermSource.Title, matchedOn: "system", weight: 2);

        var result = ExtractedTerms.From([desc, title]);

        result.Terms[0].Source.ShouldBe(ExtractedTermSource.Title);
        result.Terms[1].Source.ShouldBe(ExtractedTermSource.Description);
    }

    // ===============================================================
    // Cap at MaxTerms — keep the top by the sort
    // ===============================================================

    [Fact]
    public void MaxTerms_Is64()
    {
        ExtractedTerms.MaxTerms.ShouldBe(64);
    }

    [Fact]
    public void From_OverMaxTerms_CapsAtMaxTerms()
    {
        // 100 distinct keywords → capped to 64.
        var terms = Enumerable.Range(0, 100)
            .Select(i =>
            {
                var lex = $"kw{i:D3}";
                return Keyword(lexeme: lex, display: lex, matchedOn: lex, weight: 1);
            })
            .ToList();

        var result = ExtractedTerms.From(terms);

        result.Terms.Count.ShouldBe(ExtractedTerms.MaxTerms);
    }

    [Fact]
    public void From_OverMaxTerms_KeepsTheTopBySort_DroppingLowestWeight()
    {
        // 70 keywords: ten heavy (weight 100, lexemes h00..h09) + sixty light
        // (weight 1). The cap must keep all ten heavy ones and drop only light ones.
        var heavy = Enumerable.Range(0, 10)
            .Select(i => Keyword(lexeme: $"h{i:D2}", display: $"h{i:D2}", matchedOn: $"h{i:D2}", weight: 100))
            .ToList();
        var light = Enumerable.Range(0, 60)
            .Select(i => Keyword(lexeme: $"l{i:D2}", display: $"l{i:D2}", matchedOn: $"l{i:D2}", weight: 1))
            .ToList();

        var result = ExtractedTerms.From([.. light, .. heavy]); // light first to prove sort, not input order

        result.Terms.Count.ShouldBe(64);
        // Every heavy term survived (they sort first by Weight desc within Keyword).
        foreach (var h in heavy)
            result.Terms.ShouldContain(t => t.Lexeme == h.Lexeme,
                $"Tung term '{h.Lexeme}' (vikt 100) ska överleva cappen före lätta termer.");
        // The first ten are exactly the heavy ones.
        result.Terms.Take(10).ShouldAllBe(t => t.Weight == 100);
    }

    // ===============================================================
    // F4-4b — Requirement sort rank: Requirement → Skill → Keyword
    // ===============================================================

    [Fact]
    public void From_OrdersRequirementsBeforeSkillsAndKeywords()
    {
        // CTO Decision 1c — the Kind→rank sort key is Requirement (0) → Skill (1) →
        // Keyword (2). A high-weight keyword and a high-specificity skill must STILL
        // sort after an employer requirement, regardless of weight: cap-survival
        // mirrors match-authority (employer-stated > NLP-derived). Input order is
        // shuffled (keyword, skill, requirement) to prove the sort, not insertion.
        var keyword = Keyword(lexeme: "system", matchedOn: "system", weight: 99);
        var skill = Skill(conceptId: "skill-concept", weight: 50);
        var requirement = Requirement(conceptId: "req-concept", weight: 0);

        var result = ExtractedTerms.From([keyword, skill, requirement]);

        // Requirement first (rank 0), Skill second (rank 1), Keyword last (rank 2) —
        // EVEN THOUGH the requirement has the lowest weight of the three.
        result.Terms[0].Kind.ShouldBe(ExtractedTermKind.Requirement,
            "Requirement har högst auktoritet → sort-rank 0 (CTO Decision 1c).");
        result.Terms[1].Kind.ShouldBe(ExtractedTermKind.Skill);
        result.Terms[2].Kind.ShouldBe(ExtractedTermKind.Keyword);
    }

    [Fact]
    public void From_RequirementsWithinKind_StillOrderByWeightDescending()
    {
        // The Kind→rank change is the PRIMARY key only; the secondary key (Weight
        // desc) is unchanged WITHIN the Requirement group. A must_have weight 10
        // sorts before a must_have weight 0 (the JobTech intra-category weight).
        var light = Requirement(conceptId: "req-a", matchedOn: "A", weight: 0);
        var heavy = Requirement(conceptId: "req-b", matchedOn: "B", weight: 10);

        var result = ExtractedTerms.From([light, heavy]);

        result.Terms[0].Lexeme.ShouldBe("req-b", "tyngre requirement först (Weight desc inom Kind).");
        result.Terms[1].Lexeme.ShouldBe("req-a");
    }

    // ===============================================================
    // F4-4b — cap(64) survival: ALL requirements survive, keywords are cut
    // ===============================================================

    [Fact]
    public void From_OverMaxTerms_KeepsAllRequirements_DroppingKeywords()
    {
        // THE F4-4b CORRECTNESS GUARANTEE (CTO Decision 1c): given far more than 64
        // terms — a handful of requirements + many keywords — EVERY requirement
        // survives the cap and the dropped terms are all keywords. This is the bug
        // the Kind→rank change fixes: under the old raw (int)Kind sort, Requirement
        // (enum value 2) sorted LAST and would be cut before incidental keywords.
        var requirements = Enumerable.Range(0, 8)
            .Select(i => Requirement(
                conceptId: $"req-{i:D2}", display: $"Krav {i}", matchedOn: $"Krav {i}", weight: 0))
            .ToList();
        // 100 keywords, each HEAVIER than every requirement (weight 0) — proving the
        // survival is driven by Kind-rank, NOT by weight (a naive Weight-only cap
        // would keep the heavy keywords and drop the zero-weight requirements).
        var keywords = Enumerable.Range(0, 100)
            .Select(i => Keyword(lexeme: $"kw{i:D3}", display: $"kw{i:D3}", matchedOn: $"kw{i:D3}", weight: 100))
            .ToList();

        // Keywords first in input → proves it is the sort, not the input order.
        var result = ExtractedTerms.From([.. keywords, .. requirements]);

        result.Terms.Count.ShouldBe(ExtractedTerms.MaxTerms);
        // Every requirement survived the cap.
        foreach (var r in requirements)
            result.Terms.ShouldContain(t => t.Lexeme == r.Lexeme && t.Kind == ExtractedTermKind.Requirement,
                $"Requirement '{r.Lexeme}' ska överleva cappen före keywords (CTO Decision 1c).");
        // The first 8 terms are exactly the requirements (rank 0).
        result.Terms.Take(requirements.Count)
            .ShouldAllBe(t => t.Kind == ExtractedTermKind.Requirement);
        // The cut terms are all keywords — the requirement count plus the surviving
        // keyword count equals the cap, and no requirement was dropped.
        result.Terms.Count(t => t.Kind == ExtractedTermKind.Requirement)
            .ShouldBe(requirements.Count, "inga requirements får kapas bort.");
    }

    // ===============================================================
    // F4-4b — dedup keeps BOTH a (concept, Skill, Description) and a
    //         (concept, Requirement, MustHave) term (different identity)
    // ===============================================================

    [Fact]
    public void From_SameConceptAsSkillAndRequirement_AreNotDeduplicated()
    {
        // Identity is (Lexeme, Kind, Source). The SAME concept-id appearing both as a
        // description-extracted Skill AND as an employer must_have Requirement yields
        // TWO distinct terms — the description skill and the stated requirement are
        // different facts about the ad (F4-6 reads both). They share Lexeme but differ
        // in Kind (Skill vs Requirement), so the dedupe keeps both.
        const string concept = "1TC7_x8s_V7V";
        var skill = Skill(conceptId: concept, display: "JavaScript", matchedOn: "JavaScript");
        var requirement = Requirement(
            conceptId: concept, display: "JavaScript",
            source: ExtractedTermSource.MustHave, matchedOn: "JavaScript", weight: 10);

        var result = ExtractedTerms.From([skill, requirement]);

        result.Terms.Count.ShouldBe(2, "samma concept som Skill och som Requirement = två olika termer.");
        result.Terms.ShouldContain(t => t.Kind == ExtractedTermKind.Skill && t.Lexeme == concept);
        result.Terms.ShouldContain(t => t.Kind == ExtractedTermKind.Requirement && t.Lexeme == concept);
    }

    // ===============================================================
    // F4-4b — Requirement invariants (valid round-trip + the throw cases)
    // ===============================================================

    [Fact]
    public void From_ValidRequirement_RoundTripsFields()
    {
        var result = ExtractedTerms.From([Requirement()]);

        var term = result.Terms.ShouldHaveSingleItem();
        term.Kind.ShouldBe(ExtractedTermKind.Requirement);
        term.ConceptId.ShouldBe(term.Lexeme, "Requirement: Lexeme == ConceptId (concept-level overlap-token).");
        term.Source.ShouldBe(ExtractedTermSource.MustHave);
        term.MatchedOn.ShouldNotBeNullOrWhiteSpace("cited evidence (ADR 0074).");
    }

    [Fact]
    public void From_ValidNiceToHaveRequirement_IsAllowed()
    {
        // NiceToHave is the other legal Requirement Source.
        var result = ExtractedTerms.From(
            [Requirement(source: ExtractedTermSource.NiceToHave)]);

        result.Terms.ShouldHaveSingleItem().Source.ShouldBe(ExtractedTermSource.NiceToHave);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void From_RequirementWithoutConceptId_Throws(string conceptId)
    {
        // A Requirement term must carry a non-blank ConceptId (it is the overlap
        // token). Build the malformed term directly (Lexeme kept == the blank
        // ConceptId so this fails the ConceptId check, not the Lexeme check).
        var malformed = new ExtractedTerm(
            Lexeme: conceptId, Display: "C#",
            Kind: ExtractedTermKind.Requirement, Source: ExtractedTermSource.MustHave,
            MatchedOn: "C#", ConceptId: conceptId, Weight: 0);

        Should.Throw<ArgumentException>(() => ExtractedTerms.From([malformed]));
    }

    [Fact]
    public void From_RequirementConceptIdNotEqualLexeme_Throws()
    {
        // A Requirement's Lexeme must EQUAL its ConceptId (concept-level overlap,
        // same rule as Skill).
        var malformed = new ExtractedTerm(
            Lexeme: "not-the-concept-id", Display: "C#",
            Kind: ExtractedTermKind.Requirement, Source: ExtractedTermSource.MustHave,
            MatchedOn: "C#", ConceptId: "the-concept-id", Weight: 0);

        Should.Throw<ArgumentException>(() => ExtractedTerms.From([malformed]));
    }

    [Theory]
    [InlineData(ExtractedTermSource.Title)]
    [InlineData(ExtractedTermSource.Description)]
    public void From_RequirementWithNonRequirementSource_Throws(ExtractedTermSource source)
    {
        // A Requirement's Source must be MustHave or NiceToHave — never Title/Description.
        var malformed = new ExtractedTerm(
            Lexeme: "req-concept", Display: "C#",
            Kind: ExtractedTermKind.Requirement, Source: source,
            MatchedOn: "C#", ConceptId: "req-concept", Weight: 0);

        Should.Throw<ArgumentException>(() => ExtractedTerms.From([malformed]));
    }

    // ===============================================================
    // F4-4b — TIGHTENED Skill/Keyword Source ∈ {Title, Description}
    // ===============================================================

    [Theory]
    [InlineData(ExtractedTermSource.MustHave)]
    [InlineData(ExtractedTermSource.NiceToHave)]
    public void From_SkillWithRequirementSource_Throws(ExtractedTermSource source)
    {
        // F4-4b tightens the Skill invariant: a Skill's Source must be Title or
        // Description. A Skill with Source=MustHave/NiceToHave is a bug that used to
        // pass silently — now it throws.
        var malformed = new ExtractedTerm(
            Lexeme: "1TC7_x8s_V7V", Display: "JavaScript",
            Kind: ExtractedTermKind.Skill, Source: source,
            MatchedOn: "JavaScript", ConceptId: "1TC7_x8s_V7V", Weight: 1);

        Should.Throw<ArgumentException>(() => ExtractedTerms.From([malformed]));
    }

    [Theory]
    [InlineData(ExtractedTermSource.MustHave)]
    [InlineData(ExtractedTermSource.NiceToHave)]
    public void From_KeywordWithRequirementSource_Throws(ExtractedTermSource source)
    {
        // Same tightening for Keyword: Source must be Title or Description.
        var malformed = new ExtractedTerm(
            Lexeme: "system", Display: "system",
            Kind: ExtractedTermKind.Keyword, Source: source,
            MatchedOn: "system", ConceptId: null, Weight: 1);

        Should.Throw<ArgumentException>(() => ExtractedTerms.From([malformed]));
    }

    // ===============================================================
    // Invariant validation — malformed terms throw ArgumentException
    // ===============================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void From_KeywordWithBlankLexeme_Throws(string lexeme)
    {
        Should.Throw<ArgumentException>(
            () => ExtractedTerms.From([Keyword(lexeme: lexeme, matchedOn: "x")]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void From_TermWithBlankDisplay_Throws(string display)
    {
        Should.Throw<ArgumentException>(
            () => ExtractedTerms.From([Keyword(display: display)]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void From_TermWithBlankMatchedOn_Throws(string matchedOn)
    {
        // Evidence-citation invariant (ADR 0074): every term cites its source span.
        Should.Throw<ArgumentException>(
            () => ExtractedTerms.From([Keyword(matchedOn: matchedOn)]));
    }

    [Fact]
    public void From_TermWithNegativeWeight_Throws()
    {
        Should.Throw<ArgumentException>(
            () => ExtractedTerms.From([Keyword(weight: -1)]));
    }

    [Fact]
    public void From_TermWithNaNWeight_Throws()
    {
        Should.Throw<ArgumentException>(
            () => ExtractedTerms.From([Keyword(weight: double.NaN)]));
    }

    [Fact]
    public void From_TermWithInfiniteWeight_Throws()
    {
        Should.Throw<ArgumentException>(
            () => ExtractedTerms.From([Keyword(weight: double.PositiveInfinity)]));
    }

    [Fact]
    public void From_ZeroWeight_IsAllowed()
    {
        // Weight must be finite and NON-NEGATIVE → zero is the floor, not rejected.
        var result = ExtractedTerms.From([Keyword(weight: 0)]);
        result.Terms[0].Weight.ShouldBe(0);
    }

    [Fact]
    public void From_SkillWithoutConceptId_Throws()
    {
        // A Skill term must carry a ConceptId. Build the malformed term directly
        // (the Skill() helper always sets one).
        var malformed = new ExtractedTerm(
            "lex", "Display", ExtractedTermKind.Skill, ExtractedTermSource.Description,
            "matched", ConceptId: null, Weight: 1);

        Should.Throw<ArgumentException>(() => ExtractedTerms.From([malformed]));
    }

    [Fact]
    public void From_SkillWithConceptIdNotEqualToLexeme_Throws()
    {
        // A Skill's Lexeme must EQUAL its ConceptId (concept-level overlap token).
        var malformed = new ExtractedTerm(
            Lexeme: "not-the-concept-id", Display: "Display",
            Kind: ExtractedTermKind.Skill, Source: ExtractedTermSource.Description,
            MatchedOn: "matched", ConceptId: "the-concept-id", Weight: 1);

        Should.Throw<ArgumentException>(() => ExtractedTerms.From([malformed]));
    }

    [Fact]
    public void From_KeywordWithConceptId_Throws()
    {
        // A Keyword term must NOT carry a ConceptId.
        var malformed = new ExtractedTerm(
            Lexeme: "system", Display: "system",
            Kind: ExtractedTermKind.Keyword, Source: ExtractedTermSource.Description,
            MatchedOn: "system", ConceptId: "abc", Weight: 1);

        Should.Throw<ArgumentException>(() => ExtractedTerms.From([malformed]));
    }

    [Fact]
    public void From_ValidSkill_RoundTripsFields()
    {
        var result = ExtractedTerms.From([Skill()]);

        var term = result.Terms.ShouldHaveSingleItem();
        term.Kind.ShouldBe(ExtractedTermKind.Skill);
        term.ConceptId.ShouldBe(term.Lexeme, "Skill: Lexeme == ConceptId.");
        term.Display.ShouldBe("JavaScript");
        term.MatchedOn.ShouldBe("JavaScript");
    }

    // ===============================================================
    // Structural equality
    // ===============================================================

    [Fact]
    public void Equals_SameTermsInSameOrder_AreEqual()
    {
        var a = ExtractedTerms.From([Skill(), Keyword()]);
        var b = ExtractedTerms.From([Skill(), Keyword()]);

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Equals_SameTermsDifferentInputOrder_AreEqual()
    {
        // From normalizes (sorts) → the two instances have the same canonical order,
        // so they are structurally equal regardless of input order.
        var a = ExtractedTerms.From([Skill(), Keyword()]);
        var b = ExtractedTerms.From([Keyword(), Skill()]);

        a.ShouldBe(b);
    }

    [Fact]
    public void Equals_DifferentTerms_AreNotEqual()
    {
        var a = ExtractedTerms.From([Keyword(lexeme: "system", matchedOn: "system")]);
        var b = ExtractedTerms.From([Keyword(lexeme: "ekonomi", matchedOn: "ekonomi")]);

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Equals_Null_IsFalse()
    {
        ExtractedTerms.From([Keyword()]).Equals(null).ShouldBeFalse();
    }

    [Fact]
    public void Equals_EmptyVsEmpty_AreEqual()
    {
        ExtractedTerms.From([]).ShouldBe(ExtractedTerms.Empty);
    }
}
