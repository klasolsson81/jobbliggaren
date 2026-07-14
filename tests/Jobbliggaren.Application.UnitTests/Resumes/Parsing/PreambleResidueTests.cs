using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Parsing;

/// <summary>
/// #844 — the preamble carrier. A CV that opens with a summary but NO "Profil" heading had that
/// prose dropped from <see cref="ParsedResumeContent"/> entirely: it reached no field, no DTO, no
/// guide, and the review engine (which reads the STRUCTURED content, never RawText) then told its
/// author, as a hard A8 Fail, that "Profiltext saknas helt."
///
/// <para>The carrier asserts NOTHING about what the text is (ADR 0071 — the engine never invents a
/// section the user did not write). These tests pin exactly that: what is carried, what is
/// subtracted, and — most importantly — that prose is returned VERBATIM, never rewritten.</para>
/// </summary>
public class PreambleResidueTests
{
    private readonly HeadingDrivenResumeSegmenter _sut = CvParsingLexiconFixture.Segmenter();

    // ── The bug itself ──────────────────────────────────────────────────────────────

    private const string UnheadedSummaryCv =
        """
        Anna Andersson
        anna.andersson@example.com
        070-123 45 67
        Göteborg

        Erfaren backend-utvecklare med tio år i betalbranschen. Jag bygger driftsäkra
        tjänster i .NET och trivs närmast produktionen.

        Arbetslivserfarenhet
        Backend-utvecklare — Acme AB
        2021 - 2024
        Byggde betaltjänster i .NET.

        Utbildning
        Civilingenjör — KTH
        2016 - 2021
        """;

    [Fact]
    public void Segment_UnheadedSummary_IsCarriedInPreamble_NotDropped()
    {
        var content = _sut.Segment(UnheadedSummaryCv).Content;

        content.Preamble.ShouldNotBeNull();
        content.Preamble.ShouldContain("betalbranschen");
        content.Preamble.ShouldContain("driftsäkra");
    }

    [Fact]
    public void Segment_UnheadedSummary_IsNeverClassifiedAsProfile()
    {
        // The whole doctrine in one assertion: the engine DESCRIBES (carries the text), it does not
        // CLASSIFY (call it a Profil). Assigning it to Profile would mint a section identity out of
        // position + shape — the engine inventing a section the user did not write (ADR 0071), and
        // it would route an address block or OCR noise into A7/A9's prose corpus.
        _sut.Segment(UnheadedSummaryCv).Content.Profile.ShouldBeNull();
    }

    [Fact]
    public void Segment_UnheadedSummary_ContactIsStillFullyHarvested()
    {
        // The carrier must not cannibalise the contact fields: everything an extractor claims is
        // SUBTRACTED from the residue, which is what makes "we could not account for this" true.
        var contact = _sut.Segment(UnheadedSummaryCv).Content.Contact;

        contact.FullName.ShouldBe("Anna Andersson");
        contact.Email.ShouldBe("anna.andersson@example.com");
        contact.Phone.ShouldBe("070-123 45 67");
        contact.Location.ShouldBe("Göteborg");
    }

    [Fact]
    public void Segment_UnheadedSummary_PreambleCarriesNoContactMaterial()
    {
        var preamble = _sut.Segment(UnheadedSummaryCv).Content.Preamble;

        preamble.ShouldNotBeNull();
        preamble.ShouldNotContain("Anna Andersson");
        preamble.ShouldNotContain("anna.andersson@example.com");
        preamble.ShouldNotContain("070-123 45 67");
        preamble.ShouldNotContain("Göteborg");
    }

    // ── The rail / sidebar CV: everything is consumed ───────────────────────────────
    //
    // A two-column PDF linearizes its contact rail onto ONE line. This is the shape that makes a
    // line-level subtraction useless (the whole rail would leak into the carrier) — and the shape
    // on which DetectName was, until #844, returning NULL: IsNameLike rejects any line matching
    // EmailRegex, so the raw rail line was thrown away wholesale.

    private const string RailCv =
        """
        Anna Andersson | anna.andersson@example.com | 070-123 45 67 | Göteborg

        Arbetslivserfarenhet
        Backend-utvecklare — Acme AB
        2021 - 2024
        Byggde betaltjänster i .NET.
        """;

    [Fact]
    public void Segment_RailContactLine_LeavesNoPreamble()
    {
        // Mutation target: make the subtraction consume NOTHING → the rail leaks into the carrier,
        // A8 goes NotAssessed for essentially every CV, and the honesty arm degenerates into noise.
        _sut.Segment(RailCv).Content.Preamble.ShouldBeNull();
    }

    [Fact]
    public void Segment_RailContactLine_StillFindsTheName()
    {
        // A LIVE defect on main before #844: the rail line contains an e-mail, so IsNameLike
        // rejected it and FullName came back null on the most common two-column layout. The residue
        // runs BEFORE DetectName, so DetectName sees the surviving fragment "Anna Andersson".
        _sut.Segment(RailCv).Content.Contact.FullName.ShouldBe("Anna Andersson");
    }

    [Fact]
    public void Segment_RailContactLine_StillFindsTheCity()
    {
        // The other half of the same live defect: FromBareMunicipality required a WHOLE line to be a
        // kommun, so a rail CV lost the city too. It is fixed here BY NECESSITY, not as a bonus: the
        // subtraction CONSUMES the "Göteborg" fragment, so if rung 3 could not also read it
        // fragment-wise, the city would be claimed by the subtraction and harvested by nobody —
        // landing in no field at all, and making the carrier's contract false.
        _sut.Segment(RailCv).Content.Contact.Location.ShouldBe("Göteborg");
    }

    // ── Prose is returned VERBATIM — the engine never hands back text it rewrote ────

    [Fact]
    public void Segment_ProseLineContainingAnEmail_SurvivesWHOLE_EmailIncluded()
    {
        // The fragment is not WHOLLY consumed (an e-mail sits inside prose), so it is kept intact.
        // We never punch a hole in prose we keep — a partially-scrubbed sentence would be the engine
        // rewriting the user's words.
        const string cv =
            """
            Anna Andersson

            Kontakta mig gärna på anna@example.com om du vill veta mer om mina projekt.

            Arbetslivserfarenhet
            Utvecklare — Acme AB
            2021 - 2024
            """;

        var preamble = _sut.Segment(cv).Content.Preamble;

        preamble.ShouldNotBeNull();
        preamble.ShouldBe("Kontakta mig gärna på anna@example.com om du vill veta mer om mina projekt.");
    }

    [Fact]
    public void Segment_ProseWithCommas_KeepsItsOwnPunctuation()
    {
        // The line fragments on commas, but NO fragment is consumed, so the line comes back byte-for
        // byte — commas and all. A subtraction that rebuilt the line from its fragments would quietly
        // eat the user's punctuation.
        const string cv =
            """
            Anna Andersson

            Erfaren undersköterska, tio år i yrket, van vid natt.

            Arbetslivserfarenhet
            Undersköterska — Vårdcentralen
            2015 - 2024
            """;

        _sut.Segment(cv).Content.Preamble
            .ShouldBe("Erfaren undersköterska, tio år i yrket, van vid natt.");
    }

    // ── The label-prefix rule (FORM), and its narrow gate ───────────────────────────

    [Fact]
    public void Segment_LabelledContactLines_LeaveNoOrphanedLabel()
    {
        // "E-post: anna@x.se" — after the span is subtracted only the prefix "E-post:" remains, so
        // the whole fragment is glue. Without this rule the orphaned labels would be a FALSE
        // preamble on a very common CV shape, and A8 would go NotAssessed for no reason.
        const string cv =
            """
            Anna Andersson
            E-post: anna.andersson@example.com
            Telefon: 070-123 45 67

            Arbetslivserfarenhet
            Utvecklare — Acme AB
            2021 - 2024
            """;

        _sut.Segment(cv).Content.Preamble.ShouldBeNull();
    }

    [Fact]
    public void Segment_ColonProseContainingAnEmail_IsKeptWHOLE_LabelIncluded()
    {
        // The narrowing that keeps the label rule from eating content: the prefix is glue ONLY when
        // NOTHING BUT the prefix survives the subtraction. Here "se ... för exempel" survives, so the
        // fragment is content and is kept whole — label included. Bias: unsure ⇒ KEEP.
        const string cv =
            """
            Anna Andersson

            Portfolio: se anna@example.com för exempel på mitt arbete.

            Arbetslivserfarenhet
            Utvecklare — Acme AB
            2021 - 2024
            """;

        var preamble = _sut.Segment(cv).Content.Preamble;

        preamble.ShouldNotBeNull();
        preamble.ShouldContain("Portfolio:");
        preamble.ShouldContain("för exempel på mitt arbete");
    }

    [Fact]
    public void Segment_ColonProseWithNoContactSpan_IsNeverTouched()
    {
        // The consumed-span gate: no contact span in the fragment ⇒ the label rule never fires, so a
        // prose line that merely contains a colon cannot be mistaken for an orphaned label.
        const string cv =
            """
            Anna Andersson

            Min styrka: att leda team genom förändring.

            Arbetslivserfarenhet
            Utvecklare — Acme AB
            2021 - 2024
            """;

        _sut.Segment(cv).Content.Preamble
            .ShouldBe("Min styrka: att leda team genom förändring.");
    }

    [Fact]
    public void Segment_ColonTerminatedLineWithNoContact_IsKept_NotSilentlyDeleted()
    {
        // The case the consumed-span gate ACTUALLY protects, and the one a weaker test missed: a
        // line that ENDS in a colon but holds no contact span at all — a heading the user wrote that
        // the lexicon does not know ("Mina styrkor:"). Without the gate, the label rule would see a
        // colon-terminated remainder, call the whole line glue, and DELETE it: the engine silently
        // discarding a line the user typed, which is #844's own defect in miniature.
        //
        // The gate is what makes "we only strip a label when a contact span proved the fragment was
        // contact material" true rather than merely intended.
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Mina styrkor:
            Noggrann och trygg i stressade lägen.

            Arbetslivserfarenhet
            Utvecklare — Acme AB
            2021 - 2024
            """;

        var preamble = _sut.Segment(cv).Content.Preamble;

        preamble.ShouldNotBeNull();
        preamble.ShouldContain("Mina styrkor:");
        preamble.ShouldContain("Noggrann och trygg");
    }

    // ── Banners, and the honest-Fail case ──────────────────────────────────────────

    [Fact]
    public void Segment_CvBannerAbovethename_IsNotCarried()
    {
        // #428: "Curriculum Vitae" is document metadata, not content. It is a lexicon nameBanner and
        // therefore a subtraction term — carrying it would make the guide offer the user her own
        // document title as a candidate summary.
        const string cv =
            """
            Curriculum Vitae
            Anna Andersson
            anna@example.com

            Arbetslivserfarenhet
            Utvecklare — Acme AB
            2021 - 2024
            """;

        _sut.Segment(cv).Content.Preamble.ShouldBeNull();
    }

    [Fact]
    public void Segment_CvWithNoSummaryAtAll_CarriesNothing_SoA8sFailStaysEarned()
    {
        // The arm that must NOT be withdrawn. When the preamble is fully accounted for, the absence
        // of a profile is genuinely OBSERVED, and A8's "Profiltext saknas helt." is earned. Deleting
        // this case would be a regression dressed as honesty.
        const string cv =
            """
            Anna Andersson
            anna@example.com
            070-123 45 67

            Arbetslivserfarenhet
            Utvecklare — Acme AB
            2021 - 2024
            """;

        _sut.Segment(cv).Content.Preamble.ShouldBeNull();
    }

    [Fact]
    public void Segment_HeadedProfile_StillGoesToProfile_NotToPreamble()
    {
        // No regression on the normal shape: a CV that DOES head its summary is unaffected — the
        // text is a Profile section and the preamble is contact-only.
        const string cv =
            """
            Anna Andersson
            anna@example.com

            Profil
            Erfaren backend-utvecklare med fokus på betaltjänster.

            Arbetslivserfarenhet
            Utvecklare — Acme AB
            2021 - 2024
            """;

        var content = _sut.Segment(cv).Content;

        content.Profile.ShouldBe("Erfaren backend-utvecklare med fokus på betaltjänster.");
        content.Preamble.ShouldBeNull();
    }

    // ── The pathological bound ─────────────────────────────────────────────────────

    [Fact]
    public void Segment_HeadinglessCv_CarriesAtMostTheCap()
    {
        // A CV with NO headings has a preamble of the WHOLE document (PreambleLines takes
        // lines.Take(lines.Length)), which would duplicate the entire CV into the encrypted JSON
        // shadow. Truncation here is a REAL content loss — RawText is not exposed in the DTO — so the
        // bound exists to refuse to allocate for a pathological document, not because it is lossless.
        var giant = string.Join('\n', Enumerable.Repeat("Lorem ipsum dolor sit amet consectetur.", 200));

        var preamble = _sut.Segment(giant).Content.Preamble;

        preamble.ShouldNotBeNull();
        preamble.Length.ShouldBeLessThanOrEqualTo(PreambleResidue.MaxPreambleChars);
    }

    // ── Back-compat: the artifact is an encrypted JSON shadow (ADR 0095 D-D) ───────

    [Fact]
    public void ParsedResumeContent_LegacyJsonWithoutPreambleKey_BindsToNull()
    {
        // Rows written before #844 simply have no "preamble" key. The additive trailing ctor
        // parameter takes its default. No migration, no backfill — and no guessing about what those
        // older parses carried above their first heading. Make Preamble a REQUIRED member and this
        // goes red, before a deserialization failure reaches a real user's stored CV.
        const string legacy =
            """
            {
              "contact": { "fullName": "Anna", "email": "a@example.com", "phone": null, "location": null },
              "profile": "Erfaren utvecklare.",
              "experience": [], "education": [], "skills": [], "languages": [], "sections": []
            }
            """;

        var content = System.Text.Json.JsonSerializer.Deserialize<ParsedResumeContent>(
            legacy, Jobbliggaren.Infrastructure.Security.EncryptedFieldRegistry.ContentJsonOptions);

        content.ShouldNotBeNull();
        content.Preamble.ShouldBeNull();
        content.Profile.ShouldBe("Erfaren utvecklare.");
    }
}
