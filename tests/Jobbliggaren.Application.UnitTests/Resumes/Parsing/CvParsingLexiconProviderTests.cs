using System.Text;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// Fas 4b 8b.4a — <c>ICvParsingLexicon</c>, the port that lets a section-RECOMMENDATION asset ask the
/// RECOGNITION lexicon "which canonical section is this heading?" without forking its synonyms.
/// </summary>
public class CvParsingLexiconProviderTests
{
    private readonly CvParsingLexiconProvider _sut = new(CvParsingLexiconFixture.Load());
    private readonly HeadingDrivenResumeSegmenter _segmenter = CvParsingLexiconFixture.Segmenter();

    public static TheoryData<string, string> ShippedSynonyms()
    {
        var data = new TheoryData<string, string>();
        foreach (var (synonym, sectionId) in CvParsingLexiconFixture.ShippedPairs())
            data.Add(synonym, sectionId);

        return data;
    }

    /// <summary>
    /// The behavioural contract, over every shipped synonym: a CV whose profile is followed by that
    /// heading must (a) still SPLIT — the body must not be swallowed into the profile (the #815 bug
    /// this vocabulary exists to fix) — and (b) resolve to the sectionId the lexicon files it under.
    ///
    /// <para>This theory draws its ROWS from the asset, so it proves asset↔segmenter agreement but is
    /// blind to a token silently LOST — delete one and it simply runs one row fewer, and stays green.
    /// <see cref="V3Vocabulary_IsFullyPreserved"/> is the non-circular half that closes that hole.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ShippedSynonyms))]
    public void EveryFreeSectionSynonym_TerminatesThePrecedingSection(
        string synonym, string expectedSectionId)
    {
        // The heading as a real CV would carry it — the user's own casing, not the normalised form.
        var heading = synonym.ToUpperInvariant();
        var cv =
            $"""
            Profil
            Erfaren utvecklare med bakgrund inom offentlig sektor.

            {heading}
            Byggde en tjänst för ärendehantering.
            """;

        var result = _segmenter.Segment(cv);

        // (a) The heading TERMINATED the profile — the body did not get swallowed (#815).
        var profile = result.Content.Profile.ShouldNotBeNull();
        profile.ShouldNotContain(
            "ärendehantering",
            Case.Insensitive,
            $"Rubriken '{heading}' avslutade inte profilen — dess innehåll svaldes (#815-buggen).");

        // The free section exists, and carries the user's own heading verbatim (never the id).
        var section = result.Content.Sections.ShouldHaveSingleItem();
        section.Heading.ShouldBe(heading);

        // (b) RECOGNITION resolves it to the canonical id the RECOMMENDATION asset will key on.
        _sut.TryResolveFreeSectionId(section.Heading).ShouldBe(expectedSectionId);
    }

    /// <summary>
    /// The non-circular no-regression pin. A SUBSET assertion on purpose: a later version may ADD
    /// synonyms — that is how the vocabulary is meant to grow, and v5 does exactly that — but may
    /// never DROP one, because every dropped token is a heading that silently stops terminating its
    /// section and starts being swallowed again (#815).
    /// </summary>
    [Fact]
    public void V3Vocabulary_IsFullyPreserved()
    {
        // The 38 free-section headings as shipped in lexicon v3, transcribed here so the assertion
        // does not read the file it is guarding.
        string[] v3 =
        [
            "projekt", "projektportfölj", "projektportfolj", "utvalda projekt", "egna projekt",
            "referenser", "certifieringar", "certifikat", "kurser", "vidareutbildning",
            "fortbildning", "uppdrag", "förtroendeuppdrag", "fortroendeuppdrag", "ideella uppdrag",
            "publikationer", "utmärkelser", "utmarkelser", "priser", "stipendier", "intressen",
            "fritidsintressen", "övrigt", "ovrigt", "projects", "selected projects", "references",
            "certifications", "certificates", "courses", "assignments", "publications", "awards",
            "honours", "honors", "interests", "hobbies", "volunteering",
        ];

        v3.Length.ShouldBe(38);

        var lost = v3.Where(token => _sut.TryResolveFreeSectionId(token) is null).ToList();

        lost.ShouldBeEmpty(
            $"Dessa v3-rubriker känns inte längre igen: {string.Join(", ", lost)}. " +
            "Varje tappad token är en rubrik som tyst slutar avsluta sin sektion — #815-buggen igen.");
    }

    /// <summary>
    /// v5's reason for existing. These headings were UNRECOGNISED before 8b.4a — so a CV that carried
    /// one had its body swallowed by the preceding section (#815), AND the recommendation engine could
    /// not see the section in order to stop suggesting it. Both halves are asserted: the section
    /// splits, and it resolves.
    /// </summary>
    [Theory]
    [InlineData("Legitimation och intyg", "legitimation")]
    [InlineData("Lärarlegitimation", "legitimation")]
    [InlineData("Yrkeslegitimation", "legitimation")]
    [InlineData("Körkort", "korkort")]
    [InlineData("Kurser och certifikat", "kurser")]
    [InlineData("Ideellt engagemang", "ideellt")]
    public void SectionsTheEngineMustSuggest_AreRecognisedAndResolve(string heading, string expectedId)
    {
        var cv =
            $"""
            Profil
            Undersköterska med tio års erfarenhet.

            {heading}
            Delegering för läkemedelshantering, HLR 2025.
            """;

        var result = _segmenter.Segment(cv);

        var profile = result.Content.Profile.ShouldNotBeNull();
        profile.ShouldNotContain(
            "Delegering",
            Case.Insensitive,
            $"'{heading}' kändes inte igen som rubrik — dess innehåll svaldes av profilen (#815).");

        result.Content.Sections.ShouldHaveSingleItem().Heading.ShouldBe(heading);
        _sut.TryResolveFreeSectionId(heading).ShouldBe(expectedId);
    }

    [Theory]
    // The user's own casing and punctuation — the port normalises, the caller must not.
    [InlineData("PROJEKT", "projekt")]
    [InlineData("projekt", "projekt")]
    [InlineData("  Utvalda projekt:  ", "projekt")]
    [InlineData("Selected Projects", "projekt")]
    [InlineData("Certifieringar", "certifikat")]
    [InlineData("Referenser.", "referenser")]
    public void TryResolveFreeSectionId_ResolvesAHeadingAsTheUserWroteIt(string heading, string expected) =>
        _sut.TryResolveFreeSectionId(heading).ShouldBe(expected);

    /// <summary>
    /// A TYPED heading is not a free section. Returning an id here would make a recommendation asset
    /// believe "Erfarenhet" is a suggestible free section — and the two id-spaces would collide.
    /// </summary>
    [Theory]
    [InlineData("Erfarenhet")]
    [InlineData("Utbildning")]
    [InlineData("Kompetenser")]
    [InlineData("Språk")]
    public void TryResolveFreeSectionId_ReturnsNullForATypedHeading(string heading) =>
        _sut.TryResolveFreeSectionId(heading).ShouldBeNull();

    [Theory]
    [InlineData("Min egen rubrik")]
    [InlineData("")]
    [InlineData("   ")]
    // Never a substring match: "Kurser" must not be found inside a longer, unrelated heading.
    [InlineData("Kurser i franska jag gått privat")]
    public void TryResolveFreeSectionId_ReturnsNullForAnUnknownHeading(string heading) =>
        _sut.TryResolveFreeSectionId(heading).ShouldBeNull();

    [Fact]
    public void TryResolveFreeSectionId_ThrowsOnNull() =>
        Should.Throw<ArgumentNullException>(() => _sut.TryResolveFreeSectionId(null!));

    /// <summary>
    /// The port must expose the SAME id-space the resolver returns. A <c>FreeSectionIds</c> set that
    /// omitted an id the resolver can produce would let the asset's contract test ("every id it names
    /// exists") pass while the asset named an id it could never match — the fail-loud signal that
    /// REPLACES a version pin would itself be leaky.
    /// </summary>
    [Fact]
    public void FreeSectionIds_ContainsExactlyTheIdsTheResolverCanReturn()
    {
        _sut.FreeSectionIds.ShouldNotBeEmpty();

        var resolvable = CvParsingLexiconFixture.ShippedPairs()
            .Select(pair => pair.SectionId)
            .Distinct()
            .ToList();

        resolvable.ShouldNotBeEmpty();
        foreach (var sectionId in resolvable)
            _sut.FreeSectionIds.ShouldContain(sectionId);

        _sut.FreeSectionIds.Count.ShouldBe(resolvable.Count);
    }

    // ── The loader's fail-loud rules (the LoadFrom test seam) ─────────────────────────────
    //
    // These are the guarantees that let PR-2 trust an id. They are exercised against SYNTHETIC
    // lexicons, so the shipped asset never has to be broken to prove the loader refuses to load a
    // broken one.

    private static MemoryStream Json(string json) => new(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Load_Throws_WhenOneSynonymIsClaimedByTwoSectionIds()
    {
        var ex = Should.Throw<InvalidOperationException>(() => CvParsingLexiconLoader.LoadFrom(Json(
            """
            { "version": 9,
              "headings": { "experience": ["erfarenhet"] },
              "languageHints": { "sv": ["och"] },
              "freeSections": { "kurser": ["kurser"], "certifikat": ["kurser"] } }
            """)));

        ex.Message.ShouldContain("kurser");
        ex.Message.ShouldContain("two sections");
    }

    [Fact]
    public void Load_Throws_WhenAFreeSynonymIsAlsoATypedHeading()
    {
        var ex = Should.Throw<InvalidOperationException>(() => CvParsingLexiconLoader.LoadFrom(Json(
            """
            { "version": 9,
              "headings": { "experience": ["erfarenhet"] },
              "languageHints": { "sv": ["och"] },
              "freeSections": { "projekt": ["erfarenhet"] } }
            """)));

        ex.Message.ShouldContain("BOTH typed and free");
    }

    /// <summary>An unknown typed key used to be SKIPPED — so a typo ("experiance") made every
    /// "Erfarenhet" heading quietly stop being recognised.</summary>
    [Fact]
    public void Load_Throws_OnAnUnknownTypedHeadingKey()
    {
        var ex = Should.Throw<InvalidOperationException>(() => CvParsingLexiconLoader.LoadFrom(Json(
            """
            { "version": 9, "headings": { "experiance": ["erfarenhet"] },
              "languageHints": { "sv": ["och"] }, "freeSections": { "projekt": ["projekt"] } }
            """)));

        ex.Message.ShouldContain("experiance");
    }

    [Theory]
    // A missing block is null after deserialisation — without the guard it is a bare
    // NullReferenceException deep in the build, with no message.
    [InlineData("""{ "version": 9, "languageHints": { "sv": ["och"] }, "freeSections": { "p": ["p"] } }""")]
    [InlineData("""{ "version": 9, "headings": { "experience": ["e"] }, "freeSections": { "p": ["p"] } }""")]
    [InlineData("""{ "version": 9, "headings": { "experience": ["e"] }, "languageHints": { "sv": ["och"] } }""")]
    [InlineData("""{ "headings": { "experience": ["e"] }, "languageHints": { "sv": ["och"] }, "freeSections": { "p": ["p"] } }""")]
    public void Load_Throws_OnAMissingBlock(string json) =>
        Should.Throw<InvalidOperationException>(() => CvParsingLexiconLoader.LoadFrom(Json(json)));

    // ── displayForms (#893, lexicon v6): the two invariants that reconstruct the no-synthesis ──────
    // guarantee D6 gives up by leaving FromStructuralOp. Exercised over synthetic lexicons, so the
    // shipped asset never has to be broken to prove the loader refuses a broken one.

    /// <summary>
    /// INV-1: a display form must key on a KNOWN synonym (typed or free). A dangling key names a
    /// heading nothing recognises — the form could never be proposed, so it is a silent authoring
    /// mistake. Fail loud, parity the unknown-typed-key throw.
    /// </summary>
    [Fact]
    public void Load_Throws_WhenADisplayFormKeyIsNotAKnownSynonym()
    {
        var ex = Should.Throw<InvalidOperationException>(() => CvParsingLexiconLoader.LoadFrom(Json(
            """
            { "version": 9,
              "headings": { "skills": ["it-kompetenser"] },
              "languageHints": { "sv": ["och"] },
              "freeSections": { "projekt": ["projekt"] },
              "displayForms": { "webbutveckling": "Webbutveckling" } }
            """)));

        ex.Message.ShouldContain("webbutveckling");
        ex.Message.ShouldContain("not a known");
    }

    /// <summary>
    /// INV-2: a display form must be a pure RE-CASING of its synonym — it may differ from the
    /// (already normalized) key ONLY by letter case. A remap ("it-kompetenser" → "Kompetenser")
    /// rewrites the user's word into a different one, which is synthesis (ADR 0074 / ADR 0108 §5 /
    /// CLAUDE.md §5). This is the mechanical guard that makes "never a synonym remap" a shape
    /// invariant rather than a comment.
    /// </summary>
    [Fact]
    public void Load_Throws_WhenADisplayFormIsARemapNotARecasing()
    {
        var ex = Should.Throw<InvalidOperationException>(() => CvParsingLexiconLoader.LoadFrom(Json(
            """
            { "version": 9,
              "headings": { "skills": ["it-kompetenser", "kompetenser"] },
              "languageHints": { "sv": ["och"] },
              "freeSections": { "projekt": ["projekt"] },
              "displayForms": { "it-kompetenser": "Kompetenser" } }
            """)));

        ex.Message.ShouldContain("it-kompetenser");
        ex.Message.ShouldContain("re-casing");
    }

    /// <summary>
    /// INV-2 is TIGHTER than "NormalizeHeading(form) == key" (which would also strip a trailing
    /// ':'/'.' and collapse whitespace). A display form that merely ADDS a trailing colon is not a
    /// re-casing — proposing it would add punctuation the user did not write — so it throws too.
    /// (Under the looser NormalizeHeading-equality check this passed; the tightened OrdinalIgnoreCase
    /// check catches it. code-reviewer/dotnet-architect Minor, CTO in-block bind.)
    /// </summary>
    [Fact]
    public void Load_Throws_WhenADisplayFormAddsATrailingSeparator()
    {
        var ex = Should.Throw<InvalidOperationException>(() => CvParsingLexiconLoader.LoadFrom(Json(
            """
            { "version": 9,
              "headings": { "skills": ["it-kompetenser"] },
              "languageHints": { "sv": ["och"] },
              "freeSections": { "projekt": ["projekt"] },
              "displayForms": { "it-kompetenser": "IT-kompetenser:" } }
            """)));

        ex.Message.ShouldContain("it-kompetenser");
        ex.Message.ShouldContain("re-casing");
    }

    /// <summary>
    /// INV-3: one key, one canonical form. Two raw keys that normalize to the same key with DIFFERENT
    /// forms are an order-dependent silent last-one-wins — the exact failure the freeById "claimed by
    /// BOTH" throw guards, now for display forms too. (An identical duplicate is idempotent, allowed.)
    /// </summary>
    [Fact]
    public void Load_Throws_WhenTwoRawKeysCollideOnADifferentDisplayForm()
    {
        var ex = Should.Throw<InvalidOperationException>(() => CvParsingLexiconLoader.LoadFrom(Json(
            """
            { "version": 9,
              "headings": { "skills": ["it-kompetenser"] },
              "languageHints": { "sv": ["och"] },
              "freeSections": { "projekt": ["projekt"] },
              "displayForms": { "it-kompetenser": "IT-kompetenser", "IT-KOMPETENSER": "IT-Kompetenser" } }
            """)));

        ex.Message.ShouldContain("it-kompetenser");
        ex.Message.ShouldContain("BOTH");
    }

    /// <summary>A valid display form (a re-casing of a known synonym) loads and is retrievable by the
    /// normalised synonym key — the exact lookup D6 does.</summary>
    [Fact]
    public void Load_ExposesTheDisplayForm_ForASynonymThatCarriesARecasing()
    {
        var lexicon = CvParsingLexiconLoader.LoadFrom(Json(
            """
            { "version": 9,
              "headings": { "skills": ["it-kompetenser"] },
              "languageHints": { "sv": ["och"] },
              "freeSections": { "projekt": ["projekt"] },
              "displayForms": { "it-kompetenser": "IT-kompetenser" } }
            """));

        lexicon.DisplayFormByHeading["it-kompetenser"].ShouldBe("IT-kompetenser");
    }

    /// <summary>displayForms is OPTIONAL — a lexicon without the block loads to an empty map, never a
    /// null (D6's TryGetValue must not NRE on an older-shaped asset).</summary>
    [Fact]
    public void Load_LeavesDisplayFormsEmpty_WhenTheBlockIsAbsent()
    {
        var lexicon = CvParsingLexiconLoader.LoadFrom(Json(
            """
            { "version": 9,
              "headings": { "skills": ["kompetenser"] },
              "languageHints": { "sv": ["och"] },
              "freeSections": { "projekt": ["projekt"] } }
            """));

        lexicon.DisplayFormByHeading.ShouldBeEmpty();
    }
}
