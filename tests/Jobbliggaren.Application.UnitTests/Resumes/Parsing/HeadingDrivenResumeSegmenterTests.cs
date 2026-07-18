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
        // REGRESSION PIN — read before touching PhoneRegex.
        // IsNameLike rejects a line if LooksLikePhone(line) is true. That is an ACCIDENT: the
        // sloppy phone regex happens to match date ranges, so date lines get filtered out of name
        // detection as a side effect. Tighten the phone regex and this line stops looking like a
        // phone — and, because it contains letters, it becomes a name candidate. The name must be
        // rejected on its DATE shape, not on a phone coincidence.
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
