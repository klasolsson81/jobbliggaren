using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

// Fas 4 STEG 8 (F4-8, NO AI/LLM) — HeadingDrivenResumeSegmenter is the pure
// string-algorithm port impl (internal; visible via InternalsVisibleTo). The split
// from ICvTextExtractor exists precisely so the segmentation logic is unit-testable on
// a plain string with no binary PDF/DOCX fixture (CLAUDE.md §2.4). SPEC-DRIVEN tests of
// the documented behaviour: Swedish heading detection → Confident sections, English CV
// → En, headingless text → Degraded + NoSectionsDetected, determinism, and a degraded
// (heading-present-but-empty) block.
public class HeadingDrivenResumeSegmenterTests
{
    private readonly HeadingDrivenResumeSegmenter _sut = new();

    private const string SwedishCv =
        """
        Anna Andersson
        anna.andersson@example.com
        070-123 45 67

        Profil
        Erfaren backend-utvecklare med fokus på betaltjänster.

        Arbetslivserfarenhet
        Backend-utvecklare — Acme AB
        2021 - 2024
        Byggde betaltjänster i .NET.

        Senior-utvecklare — Globex AB
        2024 - nuvarande

        Utbildning
        Civilingenjör — KTH
        2016 - 2021

        Kompetenser
        C#, PostgreSQL, Docker

        Språk
        Svenska, Engelska
        """;

    private const string EnglishCv =
        """
        John Smith
        john.smith@example.com
        +44 20 7946 0958

        Profile
        Experienced backend developer with a focus on payment services.

        Work Experience
        Backend Developer at Acme Ltd
        2021 - 2024
        Developed and managed payment services and was responsible for the platform.

        Education
        MSc Computer Science from Imperial College
        2016 - 2021

        Skills
        C#, PostgreSQL, Docker
        """;

    [Fact]
    public void Segment_SwedishCvWithHeadings_OverallConfident()
    {
        var result = _sut.Segment(SwedishCv);

        result.Confidence.Overall.ShouldBe(OverallConfidenceLevel.Confident);
        result.Confidence.Fallback.ShouldBe(ParseFallbackReason.None);
        result.DetectedLanguage.ShouldBe(ResumeLanguage.Sv);
    }

    [Fact]
    public void Segment_SwedishCv_ExtractsContactEmailAndPhone()
    {
        var result = _sut.Segment(SwedishCv);

        result.Content.Contact.FullName.ShouldBe("Anna Andersson");
        result.Content.Contact.Email.ShouldBe("anna.andersson@example.com");
        result.Content.Contact.Phone.ShouldNotBeNull();
    }

    [Fact]
    public void Segment_SwedishCv_ContactExperienceEducationConfident()
    {
        var result = _sut.Segment(SwedishCv);

        LevelOf(result, ParsedSectionKind.Contact).ShouldBe(SectionConfidenceLevel.Confident);
        LevelOf(result, ParsedSectionKind.Experience).ShouldBe(SectionConfidenceLevel.Confident);
        LevelOf(result, ParsedSectionKind.Education).ShouldBe(SectionConfidenceLevel.Confident);

        result.Content.Experience.Count.ShouldBeGreaterThanOrEqualTo(1);
        result.Content.Education.Count.ShouldBeGreaterThanOrEqualTo(1);
        result.Content.Skills.ShouldContain("C#");
        result.Content.Languages.ShouldContain("Svenska");
    }

    [Fact]
    public void Segment_EnglishCv_DetectsEnglishLanguage()
    {
        var result = _sut.Segment(EnglishCv);

        result.DetectedLanguage.ShouldBe(ResumeLanguage.En);
    }

    [Fact]
    public void Segment_BareTextNoHeadings_DegradedWithNoSectionsDetected()
    {
        const string bare =
            "Jag är en utvecklare som har jobbat med olika projekt under många år.";

        var result = _sut.Segment(bare);

        result.Confidence.Overall.ShouldBe(OverallConfidenceLevel.Degraded);
        result.Confidence.Fallback.ShouldBe(ParseFallbackReason.NoSectionsDetected);
    }

    [Fact]
    public void Segment_HeadingPresentButEmptyBlock_SectionDegraded()
    {
        // "Kompetenser" heading is present with no entries under it (next heading
        // follows immediately) ⇒ that section is Degraded (heading found, no content).
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Kompetenser

            Språk
            Svenska
            """;

        var result = _sut.Segment(cv);

        LevelOf(result, ParsedSectionKind.Skills).ShouldBe(SectionConfidenceLevel.Degraded);
    }

    [Fact]
    public void Segment_IsDeterministic_SameInputEqualVerdict()
    {
        var first = _sut.Segment(SwedishCv);
        var second = _sut.Segment(SwedishCv);

        first.Confidence.Overall.ShouldBe(second.Confidence.Overall);
        first.DetectedLanguage.ShouldBe(second.DetectedLanguage);
        first.Content.Experience.Count.ShouldBe(second.Content.Experience.Count);
        first.Content.Education.Count.ShouldBe(second.Content.Education.Count);
        first.Content.Skills.Count.ShouldBe(second.Content.Skills.Count);

        for (var i = 0; i < first.Confidence.Sections.Count; i++)
        {
            first.Confidence.Sections[i].Kind.ShouldBe(second.Confidence.Sections[i].Kind);
            first.Confidence.Sections[i].Level.ShouldBe(second.Confidence.Sections[i].Level);
        }
    }

    [Fact]
    public void Segment_ExperienceHeaderWithInlinePeriod_DoesNotBleedDateIntoFields()
    {
        // Regression (reported layout-split bug): a header that packs the period on the same
        // line as the role/company ("Plasman — Operatör 2005 – nu") previously put the trailing
        // date into the organization slot ("Operatör 2005 – nu"). The date must be stripped from
        // the title/organization fields and recovered as the Period instead. The slot ORDER
        // (role vs company) is intentionally NOT corrected — the user edits it in the gap-fill.
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Plasman — Operatör 2005 – nu
            Körde maskiner.
            """;

        var result = _sut.Segment(cv);

        var exp = result.Content.Experience.ShouldHaveSingleItem();
        // ShouldBe pins the exact values, proving no date bled into either field.
        exp.Title.ShouldBe("Plasman");
        exp.Organization.ShouldBe("Operatör");
        // "nu" is now a recognised present-token, so the whole range is captured as the period.
        exp.Period.ShouldNotBeNull();
        exp.Period.ShouldContain("2005");
        exp.Period.ShouldContain("nu");
    }

    [Fact]
    public void Segment_ExperienceHeaderWithTrailingYear_StripsYearFromFields()
    {
        // A single trailing year ("… Utvecklare 2019") is also stripped from the split fields
        // (and recovered as the period). A leading/internal year is left alone (it is likely
        // part of a name), so only the trailing run is removed.
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Acme AB — Utvecklare 2019
            Byggde saker.
            """;

        var result = _sut.Segment(cv);

        var exp = result.Content.Experience.ShouldHaveSingleItem();
        // ShouldBe pins the exact value, proving the trailing year was stripped from the field.
        exp.Organization.ShouldBe("Utvecklare");
        exp.Period.ShouldBe("2019");
    }

    [Fact]
    public void Segment_EducationHeaderWithInlinePeriod_DoesNotBleedDateIntoFields()
    {
        // EDUCATION symmetry: ParseEducations runs the SAME SplitTitleOrganization as
        // experience, so the trailing-date strip must apply identically — an education entry
        // that packs the period on the header line ("KTH — Civilingenjör 2005 – nu") must not
        // bleed the date into degree/institution. Guards against a future refactor that special-
        // cases only the experience path. Mapping: title slot → Degree, org slot → Institution.
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Utbildning
            KTH — Civilingenjör 2005 – nu
            Läste teknik.
            """;

        var result = _sut.Segment(cv);

        var edu = result.Content.Education.ShouldHaveSingleItem();
        // ShouldBe pins the exact values, proving no date bled into either field.
        edu.Degree.ShouldBe("KTH");
        edu.Institution.ShouldBe("Civilingenjör");
        edu.Period.ShouldNotBeNull();
        edu.Period.ShouldContain("2005");
        edu.Period.ShouldContain("nu");
    }

    [Fact]
    public void Segment_ExperienceHeaderThatIsOnlyADate_FallsBackToSecondLineForOrganization()
    {
        // Degenerate edge of the new strip: a header line that is ONLY a date range would be
        // consumed entirely by StripTrailingPeriod, leaving an empty title. The split must then
        // degrade gracefully — no empty/garbage field — falling back to the second line as the
        // organization (the existing "Title / Company / Dates" fallback path). Proves the strip
        // does not produce a stray empty field when it over-consumes the whole line.
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            2005 – 2010
            Acme AB
            Körde maskiner.
            """;

        var result = _sut.Segment(cv);

        var exp = result.Content.Experience.ShouldHaveSingleItem();
        // Title is null (the date-only first line collapsed to empty → NullIfEmpty), and the
        // organization falls back to the second line. ShouldBe pins both, proving no date bled in.
        exp.Title.ShouldBeNull();
        exp.Organization.ShouldBe("Acme AB");
        exp.Period.ShouldNotBeNull();
        exp.Period.ShouldContain("2005");
        exp.Period.ShouldContain("2010");
    }

    [Fact]
    public void Segment_ExperienceHeaderWithPeriodOnSeparateLine_KeepsFieldsClean()
    {
        // No regression: "Role — Company\nYYYY-YYYY" (period on its own line) has no date on the
        // header line, so the trailing-period strip is a no-op and the fields stay clean.
        var result = _sut.Segment(SwedishCv);

        foreach (var exp in result.Content.Experience)
        {
            exp.Organization?.ShouldNotContain("2021");
            exp.Organization?.ShouldNotContain("2024");
            exp.Title?.ShouldNotContain("2021");
        }

        result.Content.Experience.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    // ===============================================================
    // #428 finding 1 — a CV-title banner at the top must not be extracted as the name
    // ===============================================================

    [Fact]
    public void Segment_CvTitleBannerAboveRealName_ExtractsRealName_NotBanner()
    {
        // #428 F1 repro: a document-title banner ("Curriculum Vitae") on the first line,
        // followed by the real name, was returned as FullName. DetectName must skip the
        // banner (versioned lexicon reject-list, §5) and return the real name.
        const string cv =
            """
            Curriculum Vitae
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Backend-utvecklare — Acme AB
            2021 - 2024
            """;

        var result = _sut.Segment(cv);

        result.Content.Contact.FullName.ShouldBe("Anna Andersson");
    }

    [Fact]
    public void Segment_CvTitleBannerWithoutRealName_YieldsNoName_AndContactNotConfident()
    {
        // #428 F1: a banner-only preamble (no real name) was mis-read as the name, which
        // inflated ContactConfidence to Confident (hasName=true) on a nameless CV. After the
        // fix FullName is null and — with only an email — the contact section is Degraded, not
        // Confident. Proves the fix propagates to ContactConfidence.
        const string cv =
            """
            Meritförteckning
            anna@example.com

            Profil
            Erfaren utvecklare.
            """;

        var result = _sut.Segment(cv);

        result.Content.Contact.FullName.ShouldBeNull();
        LevelOf(result, ParsedSectionKind.Contact).ShouldBe(SectionConfidenceLevel.Degraded);
    }

    [Fact]
    public void Segment_SingleTokenName_IsStillExtracted()
    {
        // #428 F1: the fix is a banner reject-list, NOT a "require >=2 alphabetic tokens"
        // heuristic — a legitimate single-token name (mononym / first-name-only CV) must still
        // be extracted, never traded away to avoid a banner.
        const string cv =
            """
            Zlatan
            zlatan@example.com

            Arbetslivserfarenhet
            Anfallare — Klubben
            2001 - 2020
            """;

        var result = _sut.Segment(cv);

        result.Content.Contact.FullName.ShouldBe("Zlatan");
    }

    // ===============================================================
    // #428 finding 2 — a bare year is only a period signal on the header line
    // ===============================================================

    [Fact]
    public void Segment_IncidentalYearInDescription_IsNotExtractedAsPeriod()
    {
        // #428 F2 repro: an entry with NO date line but a year in a description bullet
        // ("Migrerade den gamla 1998-stordatorn") reported "1998" as the Period. The bare-year
        // fallback is now scoped to the header line, so an incidental year is ignored.
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Backend-utvecklare — Acme AB
            Migrerade den gamla 1998-stordatorn till .NET.
            """;

        var result = _sut.Segment(cv);

        var exp = result.Content.Experience.ShouldHaveSingleItem();
        exp.Period.ShouldBeNull();
    }

    [Fact]
    public void Segment_DateRangeOnSeparateLine_IsStillExtractedAsPeriod()
    {
        // #428 F2 no-regression: a full DATE RANGE on its own (non-header) line is unambiguous
        // and must still be extracted — DateRange matching stays full-text; only the weaker
        // bare-year signal is restricted to the header line.
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Backend-utvecklare — Acme AB
            2018 - 2022
            Byggde saker.
            """;

        var result = _sut.Segment(cv);

        var exp = result.Content.Experience.ShouldHaveSingleItem();
        exp.Period.ShouldNotBeNull();
        exp.Period.ShouldContain("2018");
        exp.Period.ShouldContain("2022");
    }

    [Fact]
    public void Segment_EducationBareYearOnNonHeaderLine_IsNotExtractedAsPeriod_ByDesign()
    {
        // #428 F2 documented edge (EXPECTED, not a bug): a bare year on a NON-header line is an
        // ambiguous signal (graduation year vs incidental year), so the deterministic engine
        // (ADR 0071) reports NO period rather than risk a wrong one — honest-absent over
        // confidently-wrong. The user supplies it via the propose-and-approve gap-fill (ADR 0040).
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Utbildning
            KTH
            Civilingenjör
            2015
            """;

        var result = _sut.Segment(cv);

        var edu = result.Content.Education.ShouldHaveSingleItem();
        edu.Period.ShouldBeNull();
    }

    [Fact]
    public void Segment_ExperienceWithIsoYearMonthRange_ProducesPeriodThePeriodParserCanConsume()
    {
        // #420 drift guard: the segmenter's DateRangeRegex extracts a period whose START carries the
        // ISO 8601 YYYY-MM granularity ("2020-06"), but PeriodParser used to reject that — the first
        // ASCII hyphen (the month separator) was mistaken for the range split, so a fully machine-
        // readable ~4-year span silently vanished (CLAUDE.md §5 silent-drop) and B6 raised a false
        // reformat flag. This round-trip pins the contract that diverged: whatever Period the
        // segmenter extracts MUST be consumable by the PeriodParser the downstream engine feeds it
        // to, so the two regexes cannot drift apart again. (Note: DateRangeRegex's alternation order
        // truncates the range END to a bare year — "2020-06 – 2024" — a separate, non-§5 cosmetic
        // quirk out of #420 scope; the load-bearing part is that the ISO-month START now parses and
        // the year span is correct. The direct "2020-06 – 2024-03" case is covered in
        // PeriodParserYearSpanTests, so this test is robust whether or not that quirk is later fixed.)
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Sjuksköterska, Region Skåne
            2020-06 – 2024-03
            Vårdade patienter.
            """;

        var result = _sut.Segment(cv);

        var exp = result.Content.Experience.ShouldHaveSingleItem();
        exp.Period.ShouldNotBeNull();
        exp.Period.ShouldContain("2020-06"); // the ISO-month start that used to break the parser

        var parsed = PeriodParser.TryParseYearSpan(exp.Period, currentYear: 2026, out var start, out var end);

        parsed.ShouldBeTrue("den ISO-period segmenteraren extraherar måste kunna tolkas av PeriodParser (#420).");
        start.ShouldBe(2020);
        end.ShouldBe(2024);
    }

    // ── #252: skill-section heading + separator coverage ───────────────
    // A live first-run CV reported zero extracted skills. Root cause: the skill-section
    // headings the CV used ("Tekniska kompetenser", "Nyckelord") were absent from the
    // lexicon, so the whole skills block was never extracted; and middot/bullet/pipe
    // keyword runs were not tokenised. These guard both fixes.

    [Theory]
    [InlineData("Tekniska kompetenser")]
    [InlineData("Nyckelord")]
    [InlineData("Kärnkompetenser")]
    [InlineData("IT-kompetenser")]
    [InlineData("Kompetenser:")]              // trailing colon is stripped by NormalizeHeading
    [InlineData("Tekniska kompetenser:")]
    public void Segment_RealWorldSkillHeading_RecognisedAsSkillsSectionWithEntries(string heading)
    {
        var cv =
            $"""
            Erik Eriksson
            erik@example.com

            {heading}
            C#, PostgreSQL, Docker
            """;

        var result = _sut.Segment(cv);

        LevelOf(result, ParsedSectionKind.Skills).ShouldBe(SectionConfidenceLevel.Confident,
            $"Rubriken '{heading}' ska kännas igen som en kompetenssektion (#252).");
        result.Content.Skills.ShouldContain("C#");
        result.Content.Skills.ShouldContain("PostgreSQL");
    }

    [Theory]
    [InlineData("C# · PostgreSQL · Docker · Git")]   // middot U+00B7
    [InlineData("C# • PostgreSQL • Docker • Git")]   // bullet U+2022
    [InlineData("C# | PostgreSQL | Docker | Git")]   // pipe
    public void Segment_MiddotBulletOrPipeSeparatedSkills_SplitIntoDiscreteTokens(string run)
    {
        // A keyword run separated by middot/bullet/pipe (the "NYCKELORD: A · B · C" CV form)
        // must tokenise into discrete skills, not survive as one un-resolvable blob (#252).
        var cv =
            $"""
            Erik Eriksson
            erik@example.com

            Kompetenser
            {run}
            """;

        var result = _sut.Segment(cv);

        result.Content.Skills.ShouldBe(["C#", "PostgreSQL", "Docker", "Git"],
            "Middot/bullet/pipe-separerade kompetenser ska splittas till diskreta tokens (#252).");
    }

    [Fact]
    public void Segment_MixedCommaMiddotPipeSeparatorsInOneRun_AllSplit()
    {
        // Real CV keyword lines mix separators ("NYCKELORD: A, B · C | D"). The regex change
        // must tokenise a run that mixes comma + middot + pipe within one line — pins that the
        // separators are not mutually exclusive (#252).
        const string cv =
            """
            Erik Eriksson
            erik@example.com

            Nyckelord
            C#, PostgreSQL · Docker | Git
            """;

        var result = _sut.Segment(cv);

        result.Content.Skills.ShouldBe(["C#", "PostgreSQL", "Docker", "Git"]);
    }

    [Fact]
    public void Segment_SpaceSeparatedSkillRun_KeptAsOneTokenNotShredded()
    {
        // Space is deliberately NOT a separator — a multi-word skill ("ASP.NET Core") must not be
        // shredded. The space-run stays one token (it still resolves downstream via lexeme-bag
        // containment); this pins the intended boundary of the #252 fix so a future change that
        // adds space-splitting is caught.
        const string cv =
            """
            Erik Eriksson
            erik@example.com

            Kompetenser
            ASP.NET Core Entity Framework
            """;

        var result = _sut.Segment(cv);

        result.Content.Skills.ShouldHaveSingleItem().ShouldBe("ASP.NET Core Entity Framework");
    }

    // ── #421 (#252-class): inline "heading: content" on the SAME line ──────
    // A heading that carries its content inline after the colon ("Kompetenser: C#, …") — a
    // common one-line-per-section CV layout — must be recognised as the section, with the
    // right-hand remainder as its first content line. Previously NormalizeHeading only stripped
    // a TRAILING colon, so the inline form registered no heading and the whole section was lost.

    [Fact]
    public void Segment_InlineSkillHeadingColonContent_ExtractsSkillsConfident()
    {
        const string cv =
            """
            Erik Eriksson
            erik@example.com

            Kompetenser: C#, PostgreSQL, Docker
            """;

        var result = _sut.Segment(cv);

        LevelOf(result, ParsedSectionKind.Skills).ShouldBe(SectionConfidenceLevel.Confident);
        result.Content.Skills.ShouldBe(["C#", "PostgreSQL", "Docker"]);
    }

    [Fact]
    public void Segment_InlineProfileHeadingColonContent_CapturesSummaryText()
    {
        const string cv =
            """
            Erik Eriksson
            erik@example.com

            Profil: Erfaren backend-utvecklare med fokus på betaltjänster.
            """;

        var result = _sut.Segment(cv);

        LevelOf(result, ParsedSectionKind.Profile).ShouldBe(SectionConfidenceLevel.Confident);
        result.Content.Profile.ShouldBe("Erfaren backend-utvecklare med fokus på betaltjänster.");
    }

    [Fact]
    public void Segment_InlineEducationHeadingColonContent_ExtractsEducationEntry()
    {
        // The remainder is parsed as the section's content, so the same " — " title/organization
        // split runs: for education, title slot → Degree, org slot → Institution.
        const string cv =
            """
            Erik Eriksson
            erik@example.com

            Utbildning: Civilingenjör — KTH
            """;

        var result = _sut.Segment(cv);

        LevelOf(result, ParsedSectionKind.Education).ShouldBe(SectionConfidenceLevel.Confident);
        var edu = result.Content.Education.ShouldHaveSingleItem();
        edu.Degree.ShouldBe("Civilingenjör");
        edu.Institution.ShouldBe("KTH");
    }

    [Fact]
    public void Segment_InlineHeadingWithSecondColonInContent_SplitsOnFirstColonOnly()
    {
        // Bounded to the FIRST colon only: a second colon belongs to the content and must not
        // trigger another heading split. "Kompetenser: Verktyg: Docker, Git" → a Skills section
        // whose content keeps "Verktyg: Docker" intact (comma-split), never a nested heading.
        const string cv =
            """
            Erik Eriksson
            erik@example.com

            Kompetenser: Verktyg: Docker, Git
            """;

        var result = _sut.Segment(cv);

        LevelOf(result, ParsedSectionKind.Skills).ShouldBe(SectionConfidenceLevel.Confident);
        result.Content.Skills.ShouldBe(["Verktyg: Docker", "Git"]);
    }

    [Fact]
    public void Segment_NonHeadingColonLine_NotSplitIntoSpuriousSection()
    {
        // Non-regression: a colon line whose left part is NOT a known heading ("Ansvarig för: …")
        // must pass through untouched — it stays inside its section as ordinary content, never a
        // spurious heading and never fragmented at the colon.
        const string cv =
            """
            Erik Eriksson
            erik@example.com

            Profil
            Ansvarig för: budget, personal och rekrytering.
            """;

        var result = _sut.Segment(cv);

        LevelOf(result, ParsedSectionKind.Profile).ShouldBe(SectionConfidenceLevel.Confident);
        result.Content.Profile.ShouldNotBeNull();
        result.Content.Profile.ShouldContain("Ansvarig för: budget");
    }

    // ── #421 section-boundary gate (senior-cto-advisor 2026-07-01) ─────────
    // The inline-heading split fires ONLY at a section boundary — the document's first line, or a
    // line preceded by a blank line. A prose line whose first word is a heading token
    // ("Erfarenhet: …", "Språk: …") sitting directly under a heading is that heading's content, not
    // a new section: it must NOT hijack/truncate the section into a phantom one (the mirror risk of
    // the silent-drop fix, §5). Position, not content shape, is the distinguisher (the wanted inline
    // "Profil: <prose>" is content-shape-identical to unwanted prose). Adjacency without a blank
    // line is a deliberate, safe miss (the line stays as content, never mis-attributed).

    [Fact]
    public void Segment_ProseWithInlineHeadingWordDirectlyUnderHeading_DoesNotTruncateOrSpawnPhantom()
    {
        // "Erfarenhet: över 10 år …" as the first line under the Profil heading (no blank line
        // between) is profile prose, not a section start: no phantom Experience, the whole profile
        // text is retained, and no stray year is pulled into an experience period.
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Profil
            Erfarenhet: över 10 år inom IT och ledarskap.
            Trivs bäst i team.
            """;

        var result = _sut.Segment(cv);

        result.Content.Experience.ShouldBeEmpty();
        LevelOf(result, ParsedSectionKind.Experience).ShouldBe(SectionConfidenceLevel.NotFound);
        result.Content.Profile.ShouldNotBeNull();
        result.Content.Profile.ShouldContain("Erfarenhet: över 10 år");
        result.Content.Profile.ShouldContain("Trivs bäst i team.");
    }

    [Fact]
    public void Segment_InlineLanguageWordProseDirectlyUnderHeading_NoPhantomLanguagesSection()
    {
        // Sibling case with a LIST-section predecessor (Arbetslivserfarenhet): "Språk: flytande
        // svenska …" directly under it must not spawn a phantom Languages section. Pins that the
        // gate closes this regardless of the preceding heading's kind — a "prose-section only"
        // exception would have missed a list-section predecessor.
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Språk: flytande svenska och engelska.
            """;

        var result = _sut.Segment(cv);

        result.Content.Languages.ShouldBeEmpty();
        LevelOf(result, ParsedSectionKind.Languages).ShouldBe(SectionConfidenceLevel.NotFound);
    }

    [Fact]
    public void Segment_InlineHeadingImmediatelyAfterHeadingNoBlankLine_NotTreatedAsSectionStart()
    {
        // Accepted trade-off: an inline heading on the line directly after another heading, with NO
        // blank line between, is NOT detected as a new section (it stays as the first heading's
        // content). Adjacency without a blank line is rare in real CVs and the failure mode is the
        // safe one — no phantom section, no mis-attribution. The blank-line-separated form (the
        // common case) is covered by the inline tests above.
        const string cv =
            """
            Erik Eriksson
            erik@example.com

            Kontakt
            Kompetenser: C#, PostgreSQL
            """;

        var result = _sut.Segment(cv);

        LevelOf(result, ParsedSectionKind.Skills).ShouldBe(SectionConfidenceLevel.NotFound);
        result.Content.Skills.ShouldBeEmpty();
    }

    private static SectionConfidenceLevel LevelOf(
        Application.Resumes.Abstractions.ResumeSegmentationResult result, ParsedSectionKind kind)
    {
        foreach (var section in result.Confidence.Sections)
        {
            if (section.Kind == kind)
                return section.Level;
        }

        return SectionConfidenceLevel.NotFound;
    }
}
