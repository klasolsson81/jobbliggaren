using Jobbliggaren.Domain.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.JobAds;

/// <summary>
/// Fas 4 STEG 4 (F4-4, ADR 0071/0074/0075) — the <see cref="ExtractedTerms"/>
/// value object is the single normalization point for the persisted jsonb
/// extraction. <see cref="ExtractedTerms.From"/> validates each term's invariants,
/// deduplicates on (Lexeme, Kind, Source) keeping the highest weight, sorts
/// deterministically (Kind → Weight desc → Lexeme Ordinal → Source) and caps at
/// <see cref="ExtractedTerms.MaxTerms"/>. Empty is a valid "not-yet-extracted /
/// nothing matched" state. Malformed terms throw <see cref="ArgumentException"/>
/// (corrupt jsonb / extractor bug surfaces, never silently persists).
///
/// Pure Domain — no DB, no NLP. Mirrors SearchCriteriaTests' normalization style.
/// </summary>
public class ExtractedTermsTests
{
    // ---------------------------------------------------------------
    // Term builders — a valid Skill and a valid Keyword by default; each
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
        // ExtractedTermKind declaration order is the primary sort key:
        // Skill (0) before Keyword (1) — a high-value skill survives the cap first.
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
