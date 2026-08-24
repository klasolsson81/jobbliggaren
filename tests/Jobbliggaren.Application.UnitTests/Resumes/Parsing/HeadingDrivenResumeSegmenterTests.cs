using Jobbliggaren.Application.Resumes.Common;
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
    private readonly HeadingDrivenResumeSegmenter _sut = CvParsingLexiconFixture.Segmenter();

    // ── #815 (Klas live-review) — contact extraction ────────────────────────────────
    //
    // A sidebar/two-column CV linearizes with the contact block AFTER the body: the text
    // extractor emits raw content-stream order, so a left rail drawn late in the PDF lands
    // last. Every fixture above happens to put the phone on line 3, ahead of any date — which
    // is exactly why this class was green while the parser was wrong. This fixture reproduces
    // the real reading order.
    private const string SidebarOrderCv =
        """
        Arbetslivserfarenhet
        Operatör — Verkstaden AB, Göteborg
        2005 - nu
        Skötte produktionslinan.

        Utbildning
        Gymnasieingenjör — Lindholmen
        2001 - 2004

        Kontakt
        Anna Andersson
        anna.andersson@example.com
        070-123 45 67
        Göteborg
        """;

    [Fact]
    public void Segment_ContactAfterBody_ExtractsThePhoneNotTheFirstDateRange()
    {
        var result = _sut.Segment(SidebarOrderCv);

        // Today PhoneRegex is @"\+?\d[\d\s()\-]{5,}\d" — "any digit run with separators" — and
        // FirstPhone takes the FIRST match in document order. "2005 - nu" has too few digits,
        // but "2001 - 2004" is eight digits and wins over the actual phone number. The user then
        // sees a date range where their mobile should be, which reads as "phone not found".
        result.Content.Contact.Phone.ShouldBe("070-123 45 67");
    }

    [Theory]
    [InlineData("2021 - 2024")]
    [InlineData("2019-2023")]
    [InlineData("2016 – 2021")] // en-dash, what Word/Canva autocorrect produce
    public void Segment_DateRangeIsNeverExtractedAsAPhoneNumber(string period)
    {
        var cv =
            $"""
            Arbetslivserfarenhet
            Backend-utvecklare — Acme AB
            {period}

            Kontakt
            Anna Andersson
            anna.andersson@example.com
            """;

        var result = _sut.Segment(cv);

        // A period is not a phone number. Honest-absent beats a confidently wrong value.
        result.Content.Contact.Phone.ShouldBeNull();
    }

    [Theory]
    [InlineData("070-123 45 67", "070-123 45 67")]
    [InlineData("070–123 45 67", "070–123 45 67")] // en-dash: today the leading 070 is silently dropped
    [InlineData("+46 70 123 45 67", "+46 70 123 45 67")]
    [InlineData("0701234567", "0701234567")]
    public void Segment_SwedishMobileFormats_AreExtractedInFull(string written, string expected)
    {
        var cv =
            $"""
            Anna Andersson
            anna.andersson@example.com
            {written}

            Profil
            Erfaren utvecklare.
            """;

        var result = _sut.Segment(cv);

        result.Content.Contact.Phone.ShouldBe(expected);
    }

    [Fact]
    public void Segment_DateRangeFollowedByEmployerText_IsNotMistakenForTheName()
    {
        // The pin survives; its RATIONALE was rewritten by #898, because the guard it was written
        // about no longer exists (comment truth-sync — a test whose comment describes deleted code
        // teaches the next reader to trust a guard that is gone).
        //
        // History: IsNameLike rejected a line if LooksLikePhone(line) was true, which caught date
        // ranges only by ACCIDENT (the old sloppy phone regex matched any digit run). #815 tightened
        // the phone pattern and re-grounded the rejection on the DATE shape; #898 then replaced the
        // whole heuristic with ContactPatterns.TryPersonName, which refuses this line on its DIGITS
        // (and would refuse it on the token band too — it has four).
        //
        // What is still worth pinning is the OUTCOME: a period-and-employer line above the name must
        // never become the name. Neither PhoneRegex nor DatePatterns participates in name detection
        // any more, so touching either cannot break this test — the digit rule can, and
        // ContactPatternsPersonNameTests pins that directly.
        var cv =
            """
            2021 - 2024 Volvo AB
            Anna Andersson
            anna.andersson@example.com

            Profil
            Erfaren utvecklare.
            """;

        var result = _sut.Segment(cv);

        result.Content.Contact.FullName.ShouldBe("Anna Andersson");
    }

    // ── #815 — Ort (location) ───────────────────────────────────────────────────────
    //
    // Location was never extracted at all: ParsedContact was constructed with
    // `Location: null` hardcoded. So HasLocation was false for 100 % of imports ever made,
    // every parsed-CV review carried a false "ort saknas", and the Slutför guide always
    // asked for a city the CV already stated.

    [Theory]
    [InlineData("Ort: Göteborg")]
    [InlineData("Bostadsort: Göteborg")]
    [InlineData("Stad: Göteborg")]
    [InlineData("Location: Göteborg")]
    public void Segment_LabelledLocation_ExtractsTheCity(string labelled)
    {
        var cv =
            $"""
            Anna Andersson
            anna.andersson@example.com
            {labelled}

            Profil
            Erfaren utvecklare.
            """;

        var result = _sut.Segment(cv);

        result.Content.Contact.Location.ShouldBe("Göteborg");
    }

    [Fact]
    public void Segment_PostalCodeLine_ExtractsTheCityAfterTheCode()
    {
        var cv =
            """
            Anna Andersson
            Storgatan 1
            412 58 Göteborg
            anna.andersson@example.com

            Profil
            Erfaren utvecklare.
            """;

        var result = _sut.Segment(cv);

        result.Content.Contact.Location.ShouldBe("Göteborg");
    }

    [Fact]
    public void Segment_BareCityInTheContactBlock_ExtractsItFromTheMunicipalityLexicon()
    {
        // Klas's CV: "Göteborg" stands alone in the contact rail, with no label and no postal
        // code. The kommun vocabulary comes from the versioned taxonomy snapshot (ADR 0043) —
        // never a hand-written city list in C# (§5).
        var result = _sut.Segment(SidebarOrderCv);

        result.Content.Contact.Location.ShouldBe("Göteborg");
    }

    [Fact]
    public void Segment_CityOnlyInsideAnExperienceEntry_DoesNotBecomeThePersonsLocation()
    {
        // THE HONESTY GUARD. "Operatör — Verkstaden AB, Göteborg" states the EMPLOYER's city.
        // Inferring that the person lives there is a fabrication, and this engine never
        // synthesises what the user did not write (ADR 0071). Honest-absent beats a confident
        // guess. The bare-city rung therefore only ever looks inside contact scope.
        var cv =
            """
            Anna Andersson
            anna.andersson@example.com

            Arbetslivserfarenhet
            Operatör — Verkstaden AB, Göteborg
            2005 - 2010
            """;

        var result = _sut.Segment(cv);

        result.Content.Contact.Location.ShouldBeNull();
    }

    [Fact]
    public void Segment_LocationFound_DoesNotSilentlyRegradeContactConfidence()
    {
        // The confidence formula is hasName && (hasEmail || hasPhone). Folding Location into it
        // would re-grade every historical parse the moment this shipped. Evidence may grow; the
        // LEVEL must not move.
        const string withoutLocation =
            """
            Anna Andersson
            anna.andersson@example.com

            Profil
            Erfaren utvecklare.
            """;
        const string withLocation =
            """
            Anna Andersson
            anna.andersson@example.com
            Ort: Göteborg

            Profil
            Erfaren utvecklare.
            """;

        var before = LevelOf(_sut.Segment(withoutLocation), ParsedSectionKind.Contact);
        var after = LevelOf(_sut.Segment(withLocation), ParsedSectionKind.Contact);

        after.ShouldBe(before);
    }

    // ── #815 fynd 3 — fria sektioner (CTO-bind A′) ───────────────────────────────────
    //
    // Rubriker vi INTE typar ("Projekt", "Referenser") terminerade ingenting: en sektion
    // löpte till nästa IGENKÄND rubrik, så "PROFIL ... PROJEKT ..." svalde hela projekt-
    // listan in i sammanfattningen. Klas såg profil + projekt som en enda textmassa.

    private const string CvWithProjectsAndReferences =
        """
        Anna Andersson
        anna.andersson@example.com

        Profil
        Erfaren backend-utvecklare med fokus på betaltjänster.

        PROJEKT
        Betalplattform
        Byggde en betaltjänst i .NET.

        Bokningssystem
        Ansvarade för API:et.

        Referenser
        Lämnas på begäran.

        Kompetenser
        C#, PostgreSQL
        """;

    [Fact]
    public void Segment_UnknownHeading_TerminatesTheProfile_NoMoreSpaghetti()
    {
        var result = _sut.Segment(CvWithProjectsAndReferences);

        // Profilen får INTE svälja projektlistan.
        var profile = result.Content.Profile.ShouldNotBeNull();
        profile.ShouldBe("Erfaren backend-utvecklare med fokus på betaltjänster.");
        profile.ShouldNotContain("Betalplattform");
        profile.ShouldNotContain("Lämnas på begäran");
    }

    [Fact]
    public void Segment_TwoFreeSections_StayTwoSectionsWithTheirOwnVerbatimHeadings()
    {
        // ANTI-KOLLISIONSTESTET. Detta är testet som gör den avvisade designen
        // (ParsedSectionKind.Other) omöjlig att smyga tillbaka: med sektionerna keyade på
        // en enda "Other"-kind hade PROJEKT och Referenser konkatenerats till ETT block —
        // spagettin igen, ett lager ner — och rubrikerna användaren skrev hade kastats bort.
        var result = _sut.Segment(CvWithProjectsAndReferences);

        result.Content.Sections.Count.ShouldBe(2);

        // Rubriken är ANVÄNDARENS text, ordagrant. "PROJEKT" är inte "projekt".
        result.Content.Sections[0].Heading.ShouldBe("PROJEKT");
        result.Content.Sections[1].Heading.ShouldBe("Referenser");

        // Dokumentordning bevarad.
        result.Content.Sections[0].Entries.Count.ShouldBe(2);
        result.Content.Sections[0].Entries[0].Title.ShouldBe("Betalplattform");
        result.Content.Sections[0].Entries[1].Title.ShouldBe("Bokningssystem");
        result.Content.Sections[1].Entries[0].Lines.ShouldContain("Lämnas på begäran.");
    }

    [Fact]
    public void Segment_FreeSectionDoesNotLeakIntoTheTypedSections()
    {
        var result = _sut.Segment(CvWithProjectsAndReferences);

        // Kompetenser efter de fria sektionerna ska fortfarande hittas typat.
        result.Content.Skills.ShouldContain("C#");
        // Och projekttexten ska inte ha hamnat i erfarenhet.
        result.Content.Experience.ShouldBeEmpty();
    }

    [Fact]
    public void Segment_LabelShapedFreeToken_DoesNotHijackARealSection()
    {
        // Fria rubriker känns igen ENBART som hel rad, aldrig i inline-form ("Kurs: ...").
        // Varje post i Utbildning inleds efter en tom rad, så postens första rad passerar alltid
        // inline-splittens boundary-port. En etikettformad fri token hade därför TERMINERAT
        // Utbildning och degraderat resterande poster till fri-sektionstext — motorn hade uppfunnit
        // en sektionsgräns användaren inte skrev. Innehållet stannar i stället kvar där det står:
        // förlustfritt, synligt, redigerbart.
        const string cv =
            """
            Anna Andersson

            Utbildning
            Civilingenjör — KTH
            2016 - 2021

            Kurs: Databaser 7,5 hp
            Fördjupning i relationsdatabaser.
            """;

        var result = _sut.Segment(cv);

        result.Content.Sections.ShouldBeEmpty();
        result.Content.Education.Count.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void Segment_BulletedFreeSection_DoesNotInventATitle()
    {
        const string cv =
            """
            Anna Andersson

            Intressen
            - Segling
            - Schack
            """;

        var result = _sut.Segment(cv);

        var entry = result.Content.Sections[0].Entries[0];
        // Parsern befordrar ALDRIG en punktlista-rad till en rubrik den inte skrivit.
        entry.Title.ShouldBeNull();
        entry.Lines.Count.ShouldBe(2);
    }

    [Fact]
    public void Segment_NoFreeSections_YieldsEmptyList_NotNull()
    {
        var result = _sut.Segment(SwedishCv);

        result.Content.Sections.ShouldBeEmpty();
    }

    [Fact]
    public void Segment_FreeSectionHeading_DoesNotDisturbParseConfidence()
    {
        // De sex typade sektionerna behåller sitt konfidenskontrakt: en fri sektion får
        // varken skeva det dokument-övergripande verdiktet eller dyka upp som en sektion.
        var withFree = _sut.Segment(CvWithProjectsAndReferences);

        // Exakt de sex typade sektionerna — en fri sektion får inte dyka upp som en
        // konfidenspost och skeva det dokument-övergripande verdiktet.
        withFree.Confidence.Sections.Count.ShouldBe(6);
        withFree.Confidence.Sections
            .Select(s => s.Kind)
            .ShouldBe(
                [
                    ParsedSectionKind.Contact,
                    ParsedSectionKind.Profile,
                    ParsedSectionKind.Experience,
                    ParsedSectionKind.Education,
                    ParsedSectionKind.Skills,
                    ParsedSectionKind.Languages,
                ],
                ignoreOrder: true);
    }

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
        //
        // STILL TRUE AFTER #1060 β-1 AND β-3, and it is the boundary those fixes stop at. β-1 lets
        // the separator split read the SECOND line when the first is period-only — but only the
        // split. Here the second line ("Acme AB") carries no separator, so no split happens and
        // this fallback is still what runs. Moving the fallback too was measured and refused: it
        // would have made "Körde maskiner." the organization.
        //
        // β-3 then NARROWED that same fallback — a line carrying no fields may not become one —
        // which moved the boundary without relocating it. This fixture is unaffected because
        // "Acme AB" is a field; the case where the narrowing bites is
        // Segment_HeaderLineCarryingNoSeparator_*, and the control that proves it is a narrowing
        // rather than a removal is …StillTakesAFieldBearingSecondLineAsTheOrganization.
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

    // ===============================================================
    // #1060 β-1 — a period-only header line must not decide the split
    // ===============================================================

    [Fact]
    public void Segment_PeriodOnlyHeaderLine_SplitsTheNextLineIntoRoleAndCompany()
    {
        // The two-column Word template: the PERIOD cell renders before the ROLE cell, so the
        // entry's first line is a bare date range. StripTrailingPeriod consumes it whole, the
        // separator loop cannot match on "" — and before β-1 the fallback handed the entire
        // "Roll - Företag" line to Organization while Title stayed null, so the CV was refused
        // on Resume.ExperienceRoleRequired with the role sitting in the file all along.
        //
        // ShouldBe on BOTH slots, not just Title: the defect was an INVERTED assignment, and a
        // test that only pinned "Title is not null" would pass on a split that put the whole
        // line in Title instead.
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            2005 – 2010
            Operatör - Acme AB
            Körde maskiner.
            """;

        var result = _sut.Segment(cv);

        var exp = result.Content.Experience.ShouldHaveSingleItem();
        exp.Title.ShouldBe("Operatör");
        exp.Organization.ShouldBe("Acme AB");
        exp.Period.ShouldNotBeNull();
        exp.Period.ShouldContain("2005");
        exp.Period.ShouldContain("2010");
    }

    [Fact]
    public void Segment_PeriodOnlyHeaderLine_SplitsTheNextLineForEducationToo()
    {
        // EDUCATION symmetry, and it is not decoration: ParseEducations calls the SAME
        // SplitTitleOrganization, so the label-first defect refused BOTH sections — measured on
        // the corpus, where every education entry came back with a null Degree behind an
        // experience failure that returned first. A fix that special-cased the experience path
        // would leave Resume.EducationDegreeRequired firing the moment the experience arm passed.
        // Mapping: title slot → Degree, org slot → Institution.
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Utbildning
            2005 – 2010
            Civilingenjör - Chalmers
            Läste teknik.
            """;

        var result = _sut.Segment(cv);

        var edu = result.Content.Education.ShouldHaveSingleItem();
        edu.Degree.ShouldBe("Civilingenjör");
        edu.Institution.ShouldBe("Chalmers");
        edu.Period.ShouldNotBeNull();
        edu.Period.ShouldContain("2005");
        edu.Period.ShouldContain("2010");
    }

    [Fact]
    public void Segment_PeriodOnlyHeaderLine_DoesNotLetTheSecondLinesDateBleedIntoTheFields()
    {
        // The relocated split runs StripTrailingPeriod on the second line too. Without that, the
        // same bleed the original strip exists to prevent is reintroduced one line down by the fix
        // itself.
        //
        // WHERE it actually breaks, corrected after code-reviewer and test-writer both measured it:
        // NOT "the date ends up in the company name". TitleOrgSeparators tries " – " (en dash)
        // BEFORE " - " (hyphen), so the unstripped line splits at the date range itself, giving
        // Title = "Operatör - Acme AB 2005" and Organization = "2010". The assert falls either way
        // — but a comment that mispredicts its own failure is the thing that makes a green run
        // unreadable later, so the prediction is fixed rather than the test.
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            2005 – 2010
            Operatör - Acme AB 2005 – 2010
            Körde maskiner.
            """;

        var result = _sut.Segment(cv);

        var exp = result.Content.Experience.ShouldHaveSingleItem();
        exp.Title.ShouldBe("Operatör");
        exp.Organization.ShouldBe("Acme AB");
    }

    [Fact]
    public void Segment_RoleFirstHeaderLine_IsUnchangedByTheRelocatedSplit()
    {
        // The control. When the first line DOES carry fields, splitSource is that line and the
        // method is byte-identical to its pre-β-1 self. This is the arm that would redden if the
        // relocation were made unconditional — the mutation worth fearing, because it would
        // silently start reading line two on every well-formed CV in the product. Not the only
        // arm that would redden: eight do. It is named the control because it is the one whose
        // SUBJECT is that the relocated path leaves the ordinary path alone.
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Operatör - Acme AB
            2005 – 2010
            Körde maskiner.
            """;

        var result = _sut.Segment(cv);

        var exp = result.Content.Experience.ShouldHaveSingleItem();
        exp.Title.ShouldBe("Operatör");
        exp.Organization.ShouldBe("Acme AB");
    }

    [Fact]
    public void Segment_EntryThatIsOnlyAPeriodLine_DegradesHonestly_AndDoesNotThrow()
    {
        // The crash guard, pinned. `entry.Lines.Count >= 2` reads redundant beside `first.Length
        // == 0` and is not: an entry whose ONLY line is a bare period reaches the relocation with
        // first == "", and without the count test `Lines[1]` throws. `Segment` is called unguarded
        // from ImportResumeCommandHandler, so that is an HTTP 500 on CV import.
        //
        // Nothing in the test tree reached this before (test-writer swept tests/**/*.cs for a
        // period-only line delimited by blanks on both sides: zero matches), and the corpus cannot
        // author it — its renderer always puts the period adjacent to the role line inside one
        // cell. So the guard was load-bearing and unpinned at once.
        //
        // Producible by production: SplitEntries yields a one-line entry from any non-blank line
        // with blanks on both sides, which is what a Word document with an EMPTY PARAGRAPH either
        // side of its date line extracts to. Named precisely: ExtractDocx reads w:t, text nodes
        // and the </w:p> EndElement — never w:spacing — so the producer is the empty paragraph,
        // not paragraph spacing. Both halves are asserted — that it does not throw, AND that it
        // degrades to honest absence rather than to some invented field.
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet

            2005 – 2010

            Utbildning
            Civilingenjör - Chalmers
            """;

        var result = _sut.Segment(cv);

        var exp = result.Content.Experience.ShouldHaveSingleItem();
        exp.Title.ShouldBeNull();
        exp.Organization.ShouldBeNull();
        exp.Period.ShouldNotBeNull();
        exp.Period.ShouldContain("2005");
    }

    // ===============================================================
    // #1060 β-3 — a line that carries no fields must not BECOME a field
    // ===============================================================

    /// <summary>
    /// The mirror of β-1. There a field-less line must not DECIDE the split; here it must not
    /// BECOME a field. Before β-3 the fallback took Lines[1] whenever Lines[0] carried no
    /// separator glyph, so a block naming a role and a period and NO employer promoted with the
    /// DATE RANGE as the employer name. The engine did not drop a field, it ASSERTED one the
    /// source never made, in a document the user sends to employers.
    ///
    /// <para><b>Producer (CLAUDE.md §5).</b> An experience entry whose FIRST line carries no
    /// separator from <c>TitleOrgSeparators</c> and whose SECOND line is nothing but a date range
    /// — freelance or self-directed work, an ordinary thing for a CV to say. The same document is
    /// authored as real DOCX BYTES by
    /// <c>OpenXmlCvRenderer.RoleFirstWithBlanksAndUnattributedBlock</c> and driven through the
    /// whole chain by the corpus arm <c>docx-irreducible-unattributed-experience</c>.</para>
    ///
    /// <para><b>Why the corpus arm is not enough, and why this test exists.</b> That corpus is
    /// observe-only in the strongest sense: its report is written to a GITIGNORED artifact and no
    /// test compares it to the committed baseline — <c>LayoutCorpusReportTests</c> says so in its
    /// own words. Reverting the guard moves the baseline and nothing else. This is the assertion
    /// that reddens.</para>
    ///
    /// <para>BOTH slots are asserted, not only the organization: the defect was a FABRICATION, so
    /// a test pinning only "Organization is not the date" would pass on a fix that moved the
    /// fabricated value into Title instead.</para>
    /// </summary>
    [Fact]
    public void Segment_HeaderLineCarryingNoSeparator_DoesNotTakeTheDateLineAsTheOrganization()
    {
        const string cv = """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Frilansande systemutvecklare
            2005 – 2010
            Uppdrag åt mindre uppdragsgivare.
            """;

        var result = _sut.Segment(cv);

        var exp = result.Content.Experience.ShouldHaveSingleItem();
        exp.Title.ShouldBe("Frilansande systemutvecklare");
        exp.Organization.ShouldBeNull();
        // The date is still RECOVERED, only refused as an ORGANIZATION. Without this the guard
        // could be "fixed" by discarding the line, losing the period the CV does carry.
        exp.Period.ShouldNotBeNull();
        exp.Period.ShouldContain("2005");
        exp.Period.ShouldContain("2010");
    }

    /// <summary>
    /// EDUCATION symmetry, and it is not decoration — β-1 recorded the same rule twice for the
    /// same reason. <c>ParseEducations</c> calls the SAME <c>SplitTitleOrganization</c> and swaps
    /// the tuple at the call site, so the org slot is INSTITUTION: β-3's narrowing changes both
    /// sections at once, and a regression that special-cased the experience path would leave the
    /// education half fabricating institutions. The refusal also arrives as a DIFFERENT Domain
    /// code — <c>Resume.EducationInstitutionRequired</c>, not the arm's
    /// <c>ExperienceCompanyRequired</c> — and moves review criterion A10 from Pass to Warn.
    ///
    /// <para>Not hypothetical: this file's own <c>EnglishCv</c> fixture already carries the shape
    /// ("MSc Computer Science from Imperial College" / "2016 - 2021"), where Degree and
    /// Institution are asserted by nothing, so the fabrication has been sitting in the test tree
    /// unmeasured.</para>
    /// </summary>
    [Fact]
    public void Segment_HeaderLineCarryingNoSeparator_DoesNotTakeTheDateLineAsTheInstitution()
    {
        const string cv = """
            Anna Andersson
            anna@example.com

            Utbildning
            Civilingenjör i datateknik
            2016 – 2021
            """;

        var result = _sut.Segment(cv);

        var edu = result.Content.Education.ShouldHaveSingleItem();
        edu.Degree.ShouldBe("Civilingenjör i datateknik");
        edu.Institution.ShouldBeNull();
        edu.Period.ShouldNotBeNull();
        edu.Period.ShouldContain("2016");
        edu.Period.ShouldContain("2021");
    }

    /// <summary>
    /// THE CONTROL THE GUARD SITS ON, unpinned anywhere before β-3. The common
    /// "Title / Company / Dates" layout: Lines[0] carries no separator, Lines[1] carries a real
    /// field. β-3 must leave it byte-identical.
    ///
    /// <para>A different path from
    /// <c>Segment_ExperienceHeaderThatIsOnlyADate_FallsBackToSecondLineForOrganization</c>: there
    /// Lines[0] is a bare date and β-1's relocation runs first. Here Lines[0] is a plain role and
    /// no relocation happens, so that test does not cover this arm. It is what reddens on the two
    /// plausible mis-implementations — a predicate applied to <c>first</c> instead of the org
    /// candidate, or an inverted emptiness test. It does NOT distinguish narrowing from wholesale
    /// removal: <c>Segment_ExperienceHeaderThatIsOnlyADate_FallsBackToSecondLineForOrganization</c>
    /// already asserts <c>Organization.ShouldBe("Acme AB")</c> through this same fallback, so
    /// deleting it outright reddens there. What this adds is the guard's OWN arm, on a path where
    /// that test has <c>Title == null</c> and therefore says nothing about this one.</para>
    /// </summary>
    [Fact]
    public void Segment_HeaderLineCarryingNoSeparator_StillTakesAFieldBearingSecondLineAsTheOrganization()
    {
        const string cv = """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Systemutvecklare
            Acme AB
            2005 – 2010
            Byggde saker.
            """;

        var result = _sut.Segment(cv);

        var exp = result.Content.Experience.ShouldHaveSingleItem();
        exp.Title.ShouldBe("Systemutvecklare");
        exp.Organization.ShouldBe("Acme AB");
    }

    /// <summary>
    /// ACCEPTED AND KNOWN, pinned so it is not mistaken for the arm's population. The guard asks
    /// only "does Lines[1] carry a field"; it cannot ask whether the employer sits on Lines[2].
    /// So this layout — role, then period, then employer — BLOCKS, with the employer physically
    /// present in the document.
    ///
    /// <para>Accepted: an honest refusal the user can act on beats a CV asserting she worked at
    /// "2005 – 2010". Recorded because the guard's scope is WIDER than the corpus arm that
    /// measures it, and a reader who takes the arm as the whole population would be wrong. The
    /// remedy is not to relocate the fallback to Lines[2] — on the arm's own block that line is a
    /// description bullet, which β-1 measured and refused.</para>
    ///
    /// <para><b>The name says BLOCKS; this test stops one layer short of it.</b> It asserts the
    /// segmenter's half — no organization comes back — and the Domain half is pinned elsewhere,
    /// named here per CLAUDE.md §5: <c>ResumeEntryBuildabilityTests</c>'
    /// <c>Validate_ExperienceWithBlankCompany_ReturnsCompanyRequired</c> and its education twin
    /// <c>Validate_EducationWithBlankInstitution_ReturnsInstitutionRequired</c>.</para>
    /// </summary>
    [Fact]
    public void Segment_HeaderLineCarryingNoSeparator_YieldsNoOrganizationEvenWhenTheEmployerIsOnTheThirdLine()
    {
        const string cv = """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Systemutvecklare
            2005 – 2010
            Acme AB
            """;

        var result = _sut.Segment(cv);

        var exp = result.Content.Experience.ShouldHaveSingleItem();
        exp.Title.ShouldBe("Systemutvecklare");
        exp.Organization.ShouldBeNull();
    }

    /// <summary>
    /// THE GUARD'S NEGATIVE POPULATION, pinned so the gap is measurable rather than merely
    /// admitted in a comment. β-3's guard fires only where <c>StripTrailingPeriod</c> reduces the
    /// candidate to empty, which requires a <c>DatePatterns</c> match running to the END of the
    /// line. A date line the patterns do not model — or one carrying anything after the match —
    /// is not reduced, so it still becomes the organization and the fabrication survives.
    ///
    /// <para>These are NOT regressions and not a β-3 defect: the behaviour is identical before
    /// and after. They are pinned as ACCEPTED-AND-KNOWN, and because a comment claiming "the
    /// guard catches date-only lines" would be false about exactly these. The month form is the
    /// most consequential — plausibly the commonest Swedish CV shape after YYYY–YYYY — and it
    /// fabricates most often. It is NOT the only one that leaves the period unrecovered — three
    /// of the four do; only "2020 – 2024 (heltid)" recovers one, because <c>ExtractPeriod</c>
    /// runs unanchored where <c>StripTrailingPeriod</c> requires end-of-line.</para>
    ///
    /// <para><b>The trigger that reddens this test is a DatePatterns WIDENING</b> — modelling month
    /// names, trailing qualifiers, keyword-less open ends and <c>YYYY/MM</c> — not the predicate
    /// PROMOTION that was deferred beside <c>ReviewText</c>'s residual. That promotion factors
    /// today's model into one home and inherits its blind spot: <c>PeriodParser</c> refuses all four
    /// of these too. Naming the promotion as the trigger would leave this green while the deferral
    /// claimed the gap was closed — which is the defect this test exists to make impossible.</para>
    ///
    /// <para><b>THE WIDENING LANDED (#1060 road 3, commit 2) AND THIS TEST MOVED SIDES — THREE OF THE
    /// FOUR, PERMANENTLY.</b> Every paragraph above is the record of what was true before it, kept
    /// verbatim because the prediction it makes is the one that came true for three of the four: the
    /// predicate PROMOTION left all four green (4/4, data unchanged), and the date-model WIDENING is
    /// what reddened them — which is exactly what the trigger was written to distinguish. <b>The
    /// fourth, <c>YYYY/MM</c>, moved back to its ORIGINAL side in round 5</b> (decision D′,
    /// senior-cto-advisor round-5 bind) <b>and then forward again in ADR 0136</b>: D′ took the slash
    /// point out of <c>DateRange</c> because the läsår collision made the shared grammar unsafe, and
    /// ADR 0136 gave the LINE question its own grammar so the row is recognised without the value
    /// grammar moving — see
    /// <c>Segment_DateLineTheYearFirstSlashForm_IsNotTakenAsTheOrganization</c>
    /// below, which pins that row rather than silently dropping it.</para>
    ///
    /// <para>What it pins now, for the surviving three, is β-3's rule reaching its intended
    /// population: <i>a line that carries no field must not BECOME a field</i>. Before the widening
    /// these fabricated an employer the source never wrote, with <c>ParseConfidence</c> = Confident,
    /// in a document the user sends to employers — on the auto-promote path, which has no approve
    /// step. The period is now recovered as well, for the one form <c>DateRange</c> models as a
    /// point; the other two are recognised at the LINE level only, so the organization is correctly
    /// null while the period stays absent (honest-absent over confidently-wrong, ADR 0071).</para>
    /// </summary>
    [Theory]
    [InlineData("jan 2020 – dec 2024", "jan 2020 – dec 2024")]
    [InlineData("2020 – 2024 (heltid)", "2020 – 2024")]
    // The open-ended form WITHOUT a keyword, and it was the sharpest of the four: DateRange needs
    // an end point so it does not match at all, Year matches "2020", and the tail " –" was
    // non-empty — so an ongoing employment, rendered the commonest way, fabricated. Every
    // open-ended fixture in the tree writes a keyword instead ("2005 - nu", "2024 - nuvarande"),
    // which is why this form was unmeasured rather than disproven. It is reached at the LINE level
    // (IsIgnorableTail) and not by DateRange, so the ORGANIZATION is fixed and the PERIOD stays
    // null — a dangling separator is not a period, and inventing an end date would be the
    // confidently-wrong half of the same defect.
    [InlineData("2020 –", null)]
    public void Segment_DateLineTheModelNowReaches_IsNoLongerTakenAsTheOrganization(
        string dateLine, string? expectedPeriod)
    {
        var cv = $"""
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Systemutvecklare
            {dateLine}
            Byggde saker.
            """;

        var result = _sut.Segment(cv);

        var exp = result.Content.Experience.ShouldHaveSingleItem();
        exp.Title.ShouldBe("Systemutvecklare");
        exp.Organization.ShouldBeNull(
            "a line carrying nothing but a date must not become the employer (#1060 β-3's rule, " +
            "now reaching the population the date model previously hid from it).");
        exp.Period.ShouldBe(expectedPeriod,
            "the period is recovered only where DateRange models the form as a POINT range; where " +
            "the line is recognised at the line level only, honest-absent beats invented.");
    }

    /// <summary>
    /// THE LAST β-3 POPULATION, CLOSED (ADR 0136). Decision D′ removed the year-first SLASH point
    /// from <c>DatePatterns.DateRange</c> to close a Blocker, and the cost was that the date row
    /// stopped being recognised at the LINE level too — so on the TWO-LINE "Title / Dates" layout it
    /// became the employer, fabricated, with <c>ParseConfidence</c> = Confident, on a CV the user
    /// sends to employers. ADR 0136 separates the two grammars, so the row grammar recognises the
    /// line and β-3's guard acts on it again without the value grammar moving.
    /// </summary>
    [Fact]
    public void Segment_DateLineTheYearFirstSlashForm_IsNotTakenAsTheOrganization()
    {
        const string cv = """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Systemutvecklare
            2020/01 – 2024/12
            Byggde saker.
            """;

        var result = _sut.Segment(cv);

        var exp = result.Content.Experience.ShouldHaveSingleItem();
        exp.Title.ShouldBe("Systemutvecklare");
        exp.Organization.ShouldBeNull(
            "a line carrying nothing but a date must not become the employer (#1060 β-3). The row " +
            "grammar reads the slash point, so the guard can act on it (ADR 0136).");
    }

    /// <summary>
    /// A FOURTH ALTITUDE THE ROUND-5 BIND DID NOT ENUMERATE (found in round 6), CLOSED BY ADR 0136.
    /// On the two-column "period first" layout, <c>Lines[0]</c> IS the date row. Under decision D′
    /// the slash form was not reduced at all, so <c>splitSource</c> stayed the WHOLE date row — and
    /// <c>TitleOrgSeparators</c> contains <c>" – "</c>, the exact glyph inside
    /// <c>"2020/01 – 2024/12"</c>. The loop matched on the date's own separator and split the DATE
    /// ITSELF into <c>Title</c>/<c>Organization</c>, so the real employer on <c>Lines[1]</c> was
    /// never read. That is a distinct FAILURE SHAPE from the two-line-layout Organization
    /// fabrication pinned above — there the real Title survives and only the Organization is wrong;
    /// here BOTH fields were fabricated out of the date row and the real employer was lost — so it
    /// keeps its own pin rather than being assumed covered by that one.
    ///
    /// <para>With the row grammar reading the slash point, <c>StripTrailingPeriod</c> reduces
    /// <c>Lines[0]</c> to empty again and the split falls through to <c>Lines[1]</c>. The employer is
    /// read; the Role is genuinely absent from this layout's first two lines, so it stays null and
    /// the Domain refuses honestly (<c>Resume.ExperienceRoleRequired</c>) — the same outcome every
    /// other recognised date form already produces here, which is the parity that makes this a
    /// closure rather than a new class.</para>
    /// </summary>
    [Fact]
    public void Segment_TheYearFirstSlashFormOnTheFirstLineLayout_IsNotSplitIntoTitleAndOrganization()
    {
        const string cv = """
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            2020/01 – 2024/12
            Acme AB
            Byggde saker.
            """;

        var result = _sut.Segment(cv);

        var exp = result.Content.Experience.ShouldHaveSingleItem();
        exp.Title.ShouldBeNull(
            "the date row reduces to empty, so the split reads Lines[1] and no field is fabricated " +
            "out of the date's own en dash (ADR 0136).");
        exp.Organization.ShouldBe("Acme AB",
            "the real employer on Lines[1] is read, where before both slots were fabricated.");
    }

    [Theory]
    [InlineData("Operatör - Acme AB", "Operatör", "Acme AB")]
    [InlineData("Klarna AB - Backend-utvecklare", "Klarna AB", "Backend-utvecklare")]
    [InlineData("Verkstaden AB, Göteborg", "Verkstaden AB", "Göteborg")]
    public void Segment_SameHeaderLine_SplitsIdentically_WhicheverLineCarriesIt(
        string headerLine, string expectedTitle, string expectedOrganization)
    {
        // ACCEPTED-AND-KNOWN, not aspirational. Two of these three inputs produce a WRONG result:
        // "Klarna AB - Backend-utvecklare" puts the employer in the role slot, and
        // "Verkstaden AB, Göteborg" puts the city in the company slot. Both are pinned anyway,
        // because the slot ORDER is deliberately un-guessed (senior-cto-advisor 2026-06-23) and
        // guessing it is the one thing β-1 must not start doing.
        //
        // WHAT THIS PIN IS FOR is the second assertion, not the first. β-1's defence is that
        // relocating the split adds no new class — the same line yields the same slots wherever it
        // sits. That was an argument in a review report; here it is a fixture. Without it, a future
        // session reads the period-first path as a regression and "fixes" it by teaching the engine
        // which side is the role.
        //
        // Before β-1 the period-first form did not merely give a different split: it handed the
        // whole line to Organization and blocked on the missing Role. So the block was standing in
        // front of a FUSED field, not a correct one, which is why restoring it was refused.
        //
        // HOW THIS PIN RETIRES, so it does not read as "never change this". Rows 2 and 3 encode an
        // ACCEPTED defect, not a desired behaviour. The day the engine is given a lawful way to
        // decide which side of a header line is the role — a ratified change to the 2026-06-23
        // no-slot-guessing bind, not an inference added to this method — rows 2 and 3 SHOULD flip
        // and this test should be edited, loudly, in that PR. Row 1 must never flip: it is the
        // correctly-ordered control and its two positions must always agree.
        var periodFirst = _sut.Segment(
            $"""
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            2005 – 2010
            {headerLine}
            Körde maskiner.
            """);

        var headerFirst = _sut.Segment(
            $"""
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            {headerLine}
            2005 – 2010
            Körde maskiner.
            """);

        var a = periodFirst.Content.Experience.ShouldHaveSingleItem();
        var b = headerFirst.Content.Experience.ShouldHaveSingleItem();

        a.Title.ShouldBe(expectedTitle);
        a.Organization.ShouldBe(expectedOrganization);
        b.Title.ShouldBe(expectedTitle);
        b.Organization.ShouldBe(expectedOrganization);
    }

    [Fact]
    public void Segment_ExperienceHeaderWithPeriodOnSeparateLine_KeepsFieldsClean()
    {
        // No regression: "Role — Company\nYYYY-YYYY" (period on its own line) has no date on the
        // header line, so the trailing-period strip is a no-op and the fields stay clean.
        var result = _sut.Segment(SwedishCv);

        foreach (var exp in result.Content.Experience)
        {
            // Not `Organization?.ShouldNotContain(...)`: the null-conditional makes the assertion
            // a silent no-op when Organization IS null, which is fail-open — and #1060 β-3 is the
            // commit that enlarges the null-Organization population, so the shape that was merely
            // latent here is now reachable.
            exp.Organization.ShouldNotBeNull();
            exp.Organization.ShouldNotContain("2021");
            exp.Organization.ShouldNotContain("2024");
            // Same fix, same reason: the null-Title population is reachable on the period-first
            // path, pinned by
            // Segment_ExperienceHeaderThatIsOnlyADate_FallsBackToSecondLineForOrganization
            // (kept on one line: a pin citation broken across a line break is not greppable,
            // which defeats naming it). So `Title?.` would no-op exactly where it matters.
            // NOT enlarged by
            // β-1 — that relocation only moves which line the split reads, and its early return
            // always yields a non-null Title, so it could only SHRINK this population. The
            // population predates it. The first revision of this repair fixed Organization and
            // left this line one below it.
            exp.Title.ShouldNotBeNull();
            exp.Title.ShouldNotContain("2021");
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
    public void Segment_MononymName_IsRefused_AndTheGapIsHonest()
    {
        // A DELIBERATE REVERSAL of a #428 decision, and it must be read as one. #428 pinned the
        // mononym ("Zlatan") as extracted, on the reasoning that the banner fix must not be paid for
        // with a ">= 2 tokens" rule. #898 pays exactly that price, knowingly, because the shape of a
        // one-token line is IDENTICAL for a mononym and for the job title that sits above the name on a
        // very common layout ("Systemutvecklare"). No deterministic rule can tell them apart, so the
        // question is only which error to make: report a job title as her name, or report no name.
        //
        // Reporting no name is the honest one. It is also not silent: ContactConfidence drops to
        // Degraded (proved below), ParsedGapSummary.HasFullName tells the guide, and B3 warns — so the
        // user is ASKED for her name instead of being shown a wrong one (ADR 0040, ADR 0071).
        //
        // KNOCK-ON, stated because it is real and reaches the user: contact Degraded makes
        // ParseConfidence.RequiresManualReview true, so 5a's auto-promote leaves this CV pending with
        // AutoPromoteBlockReason.ParseNotConfident instead of saving it silently. That is the gate
        // working as documented ("the parser owns the definition of clean; auto-promote does not
        // second-guess it") — and it now says "clean" about one fewer CV that it could not read.
        // The promoted CONTENT is unaffected either way: AutoPromoteContentMapper writes the ACCOUNT
        // name, never the parsed one (Klas-bound 2026-07-16).
        const string cv =
            """
            Zlatan
            zlatan@example.com

            Arbetslivserfarenhet
            Anfallare — Klubben
            2001 - 2020
            """;

        var result = _sut.Segment(cv);

        result.Content.Contact.FullName.ShouldBeNull();
        LevelOf(result, ParsedSectionKind.Contact).ShouldBe(SectionConfidenceLevel.Degraded);

        // The knock-on chain, ASSERTED rather than argued in prose: it is the justification for
        // deliberately reversing a #428 decision, so it has to be visible in the suite. Degraded
        // contact ⇒ the parse asks for review ⇒ 5a leaves the CV pending (ParseNotConfident) instead
        // of saving it silently, and the guide is told which field is missing.
        result.Confidence.RequiresManualReview.ShouldBeTrue();
        ParsedGapSummary.FromContent(result.Content).HasFullName.ShouldBeFalse();
    }

    [Fact]
    public void Segment_CvBannerPrefixedToTheName_ReportsTheBannerToo_KnownResidual()
    {
        // "CV Anna Andersson" is neither a banner (membership is whole-line: "cv anna andersson" is
        // not in the list) nor refusable by shape (3 capitalised tokens). It is therefore accepted
        // VERBATIM, banner word included — the #428 defect class one token wider.
        //
        // Pinned rather than fixed: stripping a leading banner word would be the engine editing her
        // line. The production comment names this exact input, so the suite must show what it does
        // rather than leave the reader to assume the banner check covers it.
        const string cv =
            """
            CV Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Utvecklare — Acme AB
            2021 - 2024
            """;

        _sut.Segment(cv).Content.Contact.FullName.ShouldBe("CV Anna Andersson");
    }

    [Fact]
    public void Segment_TwoTokenBannerAloneAboveTheName_IsSkipped_NotTakenAsTheName()
    {
        // The banner list, pinned where it is LOAD-BEARING. The #428 test above uses
        // "Meritförteckning", which is one token and would be refused by the token band even if the
        // banner list were empty — so it pins nothing about the list (verified: deleting
        // "meritförteckning" from the shipped lexicon leaves every test green). A two-token banner is
        // the case where only membership can save the field: "Curriculum Vitae" is exactly the shape
        // TryPersonName accepts.
        const string cv =
            """
            Curriculum Vitae
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Utvecklare — Acme AB
            2021 - 2024
            """;

        _sut.Segment(cv).Content.Contact.FullName.ShouldBe("Anna Andersson");
    }

    [Fact]
    public void Segment_RailLineUnderAKontaktHeading_YieldsNoName_KnownAsymmetry()
    {
        // The two arms of DetectName do not see the same shapes, and the asymmetry is structural: the
        // PREAMBLE arm reads residue fragments (so a rail line has already had its contact spans
        // subtracted and "Anna Andersson" survives alone — pinned in PreambleResidueTests), while the
        // CONTACT-BLOCK arm reads raw lines and the residue never runs there.
        //
        // A rail line under an explicit Kontakt heading therefore yields NO name: it glues several
        // items onto one line, and the recogniser refuses a glued line by construction. Honest — the
        // gap reaches the guide — but the class doc would otherwise read as if the rail layout is
        // solved everywhere, and it is solved above the first heading only.
        const string cv =
            """
            Kontakt
            Anna Andersson | anna@example.com | 070-123 45 67

            Arbetslivserfarenhet
            Utvecklare — Acme AB
            2021 - 2024
            """;

        var result = _sut.Segment(cv);

        result.Content.Contact.FullName.ShouldBeNull();
        result.Content.Contact.Email.ShouldBe("anna@example.com");
    }

    // ===============================================================
    // #898 — the name question has a RECOGNISER, and the layouts it used to get wrong
    // ===============================================================

    [Fact]
    public void Segment_SummaryAboveAKontaktHeading_ReadsTheNameUnderTheHeading_NotTheSummary()
    {
        // THE second layout in #898. The heuristic answered with the first line of her SUMMARY
        // ("Erfaren undersköterska, tio år i yrket." — 39 chars, no mail, no phone, no date: it
        // cleared every check the heuristic had), and the real name under "Kontakt" was never reached,
        // because the preamble arm runs first and the heuristic never declines.
        //
        // The recogniser declines on prose, so the preamble arm falls through and the contact block
        // is read. Both halves are asserted: the name is HERS, and the summary is still CARRIED
        // (#844's guarantee — the name fix must not eat the carrier).
        const string cv =
            """
            Erfaren undersköterska, tio år i yrket.
            Trygg i stressade lägen.

            Kontakt
            Anna Andersson
            anna.andersson@example.com

            Arbetslivserfarenhet
            Undersköterska — Vårdcentralen
            2015 - 2024
            """;

        var content = _sut.Segment(cv).Content;

        content.Contact.FullName.ShouldBe("Anna Andersson");
        content.Preamble.ShouldNotBeNull();
        content.Preamble.ShouldContain("Erfaren undersköterska");
    }

    [Fact]
    public void Segment_ContactBlockLineGluingTheNameToAJobTitle_YieldsNoName()
    {
        // The Kontakt-block arm reads RAW lines, so before the recogniser owned its fragmentation this
        // line came back as FullName = "Anna Andersson, Undersköterska" — the job title inside the
        // field labelled namn, i.e. #898's own defect surviving in the half of the parser the fix had
        // not re-read. (Above the first heading the residue splits the line and the name resolves; the
        // two arms must not disagree about the same text.)
        //
        // A glued line now yields no name at all rather than a wrong one, and the gap reaches the
        // guide. Refusing beats guessing when the guess lands in a field the user reads as fact.
        const string cv =
            """
            Kontakt
            Anna Andersson, Undersköterska
            anna.andersson@example.com

            Arbetslivserfarenhet
            Undersköterska — Vårdcentralen
            2015 - 2024
            """;

        _sut.Segment(cv).Content.Contact.FullName.ShouldBeNull();
    }

    [Fact]
    public void Segment_BulletedContactBlock_ExtractsTheNameWithoutTheBullet()
    {
        // A bulleted contact block yielded FullName = "• Anna Andersson" — bullet included — because
        // the glue trim lived at the OTHER call site (the residue), not in the question itself. The
        // normalisation now travels with the recogniser, so no call site can forget it.
        const string cv =
            """
            Kontakt
            • Anna Andersson
            • anna.andersson@example.com
            • 070-123 45 67

            Arbetslivserfarenhet
            Utvecklare — Acme AB
            2021 - 2024
            """;

        _sut.Segment(cv).Content.Contact.FullName.ShouldBe("Anna Andersson");
    }

    [Fact]
    public void Segment_GluedCvTitleBannerInTheContactBlock_IsStillABanner()
    {
        // The two-normaliser defect, made observable end to end. The residue asked
        // NormalizeHeading(TrimGlue(line)) and this segmenter asked NormalizeHeading(line), so
        // "- Curriculum Vitae" was a banner to one side and CONTENT to the other — and inside a
        // Kontakt block (which the residue never sees) the segmenter read the banner, glue and all,
        // as her name. One owner now answers for both.
        const string cv =
            """
            Kontakt
            - Curriculum Vitae
            Anna Andersson
            anna.andersson@example.com

            Arbetslivserfarenhet
            Utvecklare — Acme AB
            2021 - 2024
            """;

        _sut.Segment(cv).Content.Contact.FullName.ShouldBe("Anna Andersson");
    }

    [Fact]
    public void Segment_NameWithAParticle_IsRecognised()
    {
        // The versioned nameParticles vocabulary reaching production through the segmenter — the
        // lowercase token between two capitalised ones is exactly what a shape-only rule would refuse.
        const string cv =
            """
            Anna von Sydow
            anna.von.sydow@example.com

            Arbetslivserfarenhet
            Utvecklare — Acme AB
            2021 - 2024
            """;

        _sut.Segment(cv).Content.Contact.FullName.ShouldBe("Anna von Sydow");
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
        // used to truncate the range END to a bare year — "2020-06 – 2024" — which this test was
        // written to survive rather than assert, being out of #420 scope. #1060 road 3 commit 1
        // corrected the ordering and the truncation is gone; this test stayed green through it,
        // which is what "robust whether or not that quirk is later fixed" was for. The END value
        // itself is now pinned in DatePatternsAlternationOrderingTests — deliberately not here,
        // because this test's subject is the START granularity round-tripping to PeriodParser.)
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

    // ── #856 (CV-lane STEG 3) — route over-long skill/language tokens OUT of the scored-atom ──
    //    lists into a free ParsedSection (CTO bind C1; dotnet-architect mechanics).
    //
    // ParseList caps the COUNT of skill/language tokens but never their LENGTH, and it splits only on
    // [\n,;•·|] (space is deliberately NOT a separator). A long, unsplittable line under a
    // Kompetenser/Språk heading therefore becomes ONE over-long "skill" chip — and the chip IS the
    // unit the matcher scores (Skill.NameMaxLength, #855), so a sentence let in as a skill poisons the
    // atom. The fix: PER-TOKEN, when trimmed.Length > Skill.NameMaxLength (strict >), route that token
    // OUT of Skills/Languages into a free ParsedSection carrying the recognised heading VERBATIM plus
    // the prose as an entry. Nothing is truncated and nothing is dropped (ADR 0071) — the over-long
    // prose stays visible and editable, just not as a scored atom.

    [Fact]
    public void Segment_OverLongSkillToken_RoutedOutOfSkillsIntoFreeSection()
    {
        // 101 chars, no separator glyph, no leading bullet — one unsplittable over-long token.
        var overLong = new string('a', Skill.NameMaxLength + 1);
        var cv =
            $"""
            Anna Andersson
            anna@example.com

            Kompetenser
            C#
            {overLong}
            """;

        var result = _sut.Segment(cv);

        // The short atom stays a scored skill; the over-long line does NOT poison the atom list.
        result.Content.Skills.ShouldContain("C#");
        result.Content.Skills.ShouldNotContain(overLong);

        // It is routed into a free section carrying the heading verbatim, prose intact (no truncation).
        result.Content.Sections.Count.ShouldBe(1);
        RoutedLines(result, "Kompetenser").ShouldContain(overLong);
    }

    [Theory]
    [InlineData(Skill.NameMaxLength, false)]      // exactly at the bound → stays a skill (strict >)
    [InlineData(Skill.NameMaxLength + 1, true)]   // one past the bound → routed out
    public void Segment_SkillTokenAtLengthBoundary_RoutesOnlyWhenStrictlyOverMaxLength(
        int length, bool expectRouted)
    {
        var token = new string('a', length);
        var cv =
            $"""
            Anna Andersson
            anna@example.com

            Kompetenser
            {token}
            """;

        var result = _sut.Segment(cv);

        if (expectRouted)
        {
            result.Content.Skills.ShouldNotContain(token);
            RoutedLines(result, "Kompetenser").ShouldContain(token);
        }
        else
        {
            // Exactly at the bound is a valid atom — the routing is strict >, never >=.
            result.Content.Skills.ShouldContain(token);
            result.Content.Sections.ShouldNotContain(s => s.Heading == "Kompetenser");
        }
    }

    [Fact]
    public void Segment_OverLongLanguageToken_RoutedOutOfLanguagesIntoFreeSection()
    {
        // The same bound (Skill.NameMaxLength) governs Languages — a spoken-language name is a scored
        // atom too (Resume.ValidateContent caps SpokenLanguage.Name at the same 100, #855).
        var overLong = new string('a', Skill.NameMaxLength + 1);
        var cv =
            $"""
            Anna Andersson
            anna@example.com

            Språk
            Svenska
            {overLong}
            """;

        var result = _sut.Segment(cv);

        result.Content.Languages.ShouldContain("Svenska");
        result.Content.Languages.ShouldNotContain(overLong);

        result.Content.Sections.Count.ShouldBe(1);
        RoutedLines(result, "Språk").ShouldContain(overLong);
    }

    [Fact]
    public void Segment_SkillBlockOfOnlyOverLongToken_DegradesWithRoutedEvidence_NotEmptyMisleading()
    {
        var overLong = new string('a', Skill.NameMaxLength + 1);
        var cv =
            $"""
            Anna Andersson
            anna@example.com

            Kompetenser
            {overLong}
            """;

        var result = _sut.Segment(cv);

        // No atom survives — but the heading WAS matched, so this is Degraded, never NotFound.
        result.Content.Skills.ShouldBeEmpty();
        LevelOf(result, ParsedSectionKind.Skills).ShouldBe(SectionConfidenceLevel.Degraded);

        // The evidence must state, STRUCTURALLY, that tokens were ROUTED — distinguishing "routed
        // away" from the misleading "no entries parsed" — and it must NEVER carry the CV text
        // (the confidence channel is not encrypted; structural facts only, ADR 0071 / §5).
        var skills = SectionOf(result, ParsedSectionKind.Skills);
        skills.Evidence.ShouldContain(
            e => e.Contains("routed", StringComparison.OrdinalIgnoreCase),
            "evidensen ska strukturellt notera att tokens routades ut.");
        skills.Evidence.ShouldNotContain(
            e => e.Contains(overLong, StringComparison.Ordinal),
            "konfidens-evidensen får aldrig bära CV-innehåll (ADR 0071, §5).");
    }

    [Fact]
    public void Segment_SkillBlockWithKeptAtomAndOverLongToken_ConfidentWithRoutedEvidence()
    {
        // The count>0 & routedCount>0 arm of ListSectionConfidence: a Kompetenser block with BOTH a
        // kept atom AND an over-long token must land Confident (a real atom survived) AND still carry
        // the structural routed-note. Without this test that arm's routed-note is unasserted —
        // deleting it from production keeps the rest of the suite green (a mutation gap). Distinct from
        // test 4 (the count==0 Degraded arm, which has its own routed-note) and from the routing/
        // section tests above (none of which assert the Confident-arm evidence).
        var overLong = new string('a', Skill.NameMaxLength + 1);
        var cv =
            $"""
            Anna Andersson
            anna@example.com

            Kompetenser
            C#
            {overLong}
            """;

        var result = _sut.Segment(cv);

        // A real atom survived → Confident, not Degraded.
        result.Content.Skills.ShouldContain("C#");
        LevelOf(result, ParsedSectionKind.Skills).ShouldBe(SectionConfidenceLevel.Confident);

        // ...and the Confident arm STILL carries the structural routed-note, never the CV text.
        var skills = SectionOf(result, ParsedSectionKind.Skills);
        skills.Evidence.ShouldContain(
            e => e.Contains("routed", StringComparison.OrdinalIgnoreCase),
            "Confident-armen (count>0) måste också bära routed-noten, inte bara Degraded-armen.");
        skills.Evidence.ShouldNotContain(
            e => e.Contains(overLong, StringComparison.Ordinal),
            "konfidens-evidensen får aldrig bära CV-innehåll (ADR 0071, §5).");
    }

    [Fact]
    public void Segment_RoutedSection_SurvivesEvenWhenFreeSectionCapIsSaturated()
    {
        // THE load-bearing ADR 0071 guarantee (dotnet-architect Blocker-class): the routed section
        // must NOT be silently dropped by the MaxSections cap. Saturate the free-section list with
        // 30+ recognised free headings (the detector only recognises lexicon freeSections synonyms,
        // so these are real synonyms, not invented "Projekt 1..30"), THEN add a Kompetenser block
        // whose only token is over-long. The routed prose must still appear — a dropped routed
        // section would be a silent content loss (§5).
        var freeHeadings = new[]
        {
            "projekt", "projektportfölj", "utvalda projekt", "egna projekt", "projects",
            "selected projects", "certifieringar", "certifikat", "certifications", "certificates",
            "certifikat och intyg", "certifieringar och kurser", "kurser", "vidareutbildning",
            "fortbildning", "courses", "kurser och certifikat", "kurser och intyg",
            "kurser och utbildningar", "uppdrag", "assignments", "förtroendeuppdrag",
            "ideella uppdrag", "volunteering", "ideellt engagemang", "publikationer", "publications",
            "utmärkelser", "priser", "stipendier", "awards", "intressen",
        };

        var overLong = new string('a', Skill.NameMaxLength + 1);
        var freeBlocks = string.Join(
            "\n", freeHeadings.Select(h => $"{h}\nInnehåll under {h}.\n"));
        var cv = $"Anna Andersson\nanna@example.com\n\n{freeBlocks}\nKompetenser\n{overLong}";

        var result = _sut.Segment(cv);

        // Regardless of the cap, the routed Kompetenser prose is retained (never silently dropped).
        RoutedLines(result, "Kompetenser").ShouldContain(overLong);

        // ...AND prove the cap ACTUALLY engaged, or this is a silently-trivial green. The 32
        // recognised document free headings must be capped to MaxSections (=30, private const in the
        // segmenter), so exactly 30 DOCUMENT free sections land and the tail (incl. the 32nd,
        // "intressen") is dropped. If a future lexicon shrink drops recognised free headings below 30,
        // OR the cap stops engaging, this fails loudly instead of passing with the cap never hit.
        var documentFreeSections = result.Content.Sections
            .Where(s => s.Heading != "Kompetenser")
            .ToList();
        documentFreeSections.Count.ShouldBe(30);
        documentFreeSections.ShouldNotContain(s => s.Heading == "intressen");
    }

    [Fact]
    public void Segment_ShortOnlySkillBlock_AddsNoRoutedSection_Regression()
    {
        // Nothing is over-long, so nothing routes: the skills parse is unchanged and NO spurious free
        // section appears. Guards against a fix that routes on the wrong condition.
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Kompetenser
            C#, PostgreSQL, Docker
            """;

        var result = _sut.Segment(cv);

        result.Content.Skills.ShouldBe(["C#", "PostgreSQL", "Docker"]);
        result.Content.Sections.ShouldBeEmpty();
    }

    // The lines of the routed free section whose heading matches (verbatim). Fails cleanly when no
    // such section exists (the RED state against un-fixed production code).
    private static List<string> RoutedLines(
        Application.Resumes.Abstractions.ResumeSegmentationResult result, string heading)
    {
        var section = result.Content.Sections
            .FirstOrDefault(s => s.Heading == heading)
            .ShouldNotBeNull($"en routad fri sektion med rubriken '{heading}' ska finnas.");

        return section.Entries.SelectMany(e => e.Lines).ToList();
    }

    private static SectionConfidence SectionOf(
        Application.Resumes.Abstractions.ResumeSegmentationResult result, ParsedSectionKind kind) =>
        result.Confidence.Sections.First(s => s.Kind == kind);

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
