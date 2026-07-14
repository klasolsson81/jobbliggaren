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
            anna.andersson@example.com

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
            anna.andersson@example.com

            Erfaren undersköterska, tio år i yrket, van vid natt.

            Arbetslivserfarenhet
            Undersköterska — Vårdcentralen
            2015 - 2024
            """;

        _sut.Segment(cv).Content.Preamble
            .ShouldBe("Erfaren undersköterska, tio år i yrket, van vid natt.");
    }

    [Fact]
    public void Segment_ProseLineEndingInASeparator_KeepsThatSeparator()
    {
        // A REAL defect this test was written to kill. A trailing comma produces one EMPTY fragment.
        // While empty fragments counted as "consumed", that flipped the any-consumed flag and sent the
        // line down the REBUILD path — which returned it WITHOUT the user's own comma:
        //
        //     in:  "Erfaren undersköterska, tio år i yrket,"
        //     out: "Erfaren undersköterska, tio år i yrket"
        //
        // The engine handing back text it silently rewrote is exactly what the carrier exists to
        // refuse (ADR 0109: it DESCRIBES, it does not edit). Empty fragments are neutral: a line whose
        // only "consumption" is empty glue has consumed nothing, and must come back byte-for-byte.
        const string cv =
            """
            Anna Andersson
            anna.andersson@example.com

            Erfaren undersköterska, tio år i yrket,

            Arbetslivserfarenhet
            Undersköterska — Vårdcentralen
            2015 - 2024
            """;

        _sut.Segment(cv).Content.Preamble
            .ShouldBe("Erfaren undersköterska, tio år i yrket,");
    }

    [Fact]
    public void Segment_PreambleOfOnlySeparators_CarriesNothing()
    {
        // A decorative rule line ("| | |", "•••") is glue and nothing else. It must not become a
        // "preamble" — that would push A8 to NotAssessed on a CV that has no summary at all, turning
        // the honesty arm into noise.
        const string cv =
            """
            Anna Andersson
            anna@example.com
            | | |

            Arbetslivserfarenhet
            Utvecklare — Acme AB
            2021 - 2024
            """;

        _sut.Segment(cv).Content.Preamble.ShouldBeNull();
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
            anna.andersson@example.com

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
            anna.andersson@example.com

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

    // ── The subtraction leaves the user's own text, not its debris ─────────────────

    [Fact]
    public void Segment_LineWhoseFIRSTItemIsConsumed_CarriesNoOrphanedGlue()
    {
        // The rail tests all consume the TAIL of a line and keep the head ("Anna Andersson | …").
        // Consume the HEAD instead and the glue reconstruction runs the other way: the surviving
        // fragment is preceded by a separator whose left-hand item no longer exists.
        //
        // That separator must NOT be emitted. It is not the user's text — it is a fragment of the
        // field we just removed, and Trim() cannot save us here (it strips whitespace, not "|"). A
        // carrier that opens with an orphaned pipe is the engine handing back debris from its own
        // subtraction, and the guide would offer her that debris as candidate summary text.
        // The consumed item leads the line — an e-mail, which is unambiguously contact material on any
        // line (a bare kommun is not: see Segment_CityOnANonContactLine_ReachesSOMEField_NeverVanishes).
        const string cv =
            """
            Anna Andersson
            anna.andersson@example.com | Erfaren undersköterska med tio års erfarenhet av natt.

            Arbetslivserfarenhet
            Undersköterska — Vårdcentralen
            2015 - 2024
            """;

        var preamble = _sut.Segment(cv).Content.Preamble;

        preamble.ShouldBe("Erfaren undersköterska med tio års erfarenhet av natt.");
    }

    [Fact]
    public void Segment_DigitRunTheSegmenterRefusesToCallAPhone_IsKept_NotSubtracted()
    {
        // THE sharing contract, stated as an observable: the residue subtracts EXACTLY what the
        // extractors recognise — no more. A pattern travels with its guard, so the residue's phone
        // arm re-applies IsPhoneShaped; drop that guard and the subtraction starts eating spans the
        // segmenter itself refuses to call a phone.
        //
        // This run is 20 digits — past E.164's 15 — so IsPhoneShaped rejects it and the segmenter
        // reports NO phone. If the residue ate it anyway, the line would reach no field at all: not
        // Phone (refused), not the carrier (subtracted). Text the user typed, silently gone — which
        // is #844's own defect, re-created inside #844's own fix.
        // The reference sits BELOW the contact block on purpose. Wedged BETWEEN the name and the
        // e-mail it would be inside the contact block and dropped by the position rule — a bound,
        // measured trade-off (see Segment_TaglineWedgedInsideTheContactBlock_IsDropped_AndCounted),
        // and a different guarantee from the one under test here. This test is about the SHARING
        // contract, so it puts the run where the position rule is not the variable.
        const string stampedReference = "0000 1234 5678 9012 3456";
        const string cv =
            $"""
            Anna Andersson
            anna.andersson@example.com

            {stampedReference}

            Arbetslivserfarenhet
            Undersköterska — Vårdcentralen
            2015 - 2024
            """;

        var content = _sut.Segment(cv).Content;

        // The extractor's own verdict: this is not a phone.
        content.Contact.Phone.ShouldBeNull();

        // Therefore the subtraction may not claim it either.
        content.Preamble.ShouldNotBeNull();
        content.Preamble.ShouldContain(stampedReference);
    }

    // ── The accepted residual, made visible ────────────────────────────────────────

    [Fact]
    public void Segment_TaglineWedgedInsideTheContactBlock_IsDropped_AndCounted()
    {
        // THE COST OF THE POSITION RULE, pinned in the open rather than left as a footnote.
        //
        // The contact block runs to the last line a recogniser consumed anything on. A tagline wedged
        // BETWEEN the name and the e-mail is therefore inside it, and it is DROPPED. That is a real
        // discard of text the user wrote — the only one this engine makes — and it was accepted
        // deliberately: the alternative (drop by name-guess) deleted a job title on one common layout
        // and the first line of the user's summary on another, which is #844's own bug.
        //
        // It is bounded and it is COUNTED. A count is not an apology — it is what turns "rare, we
        // think" into a number we can read off production and act on. If this ever goes red because the
        // drop got wider, that is exactly the alarm it exists to raise.
        const string cv =
            """
            Anna Andersson
            Systemutvecklare med fokus på betalningar
            anna.andersson@example.com

            Arbetslivserfarenhet
            Utvecklare — Acme AB
            2021 - 2024
            """;

        var result = _sut.Segment(cv);

        result.Content.Preamble.ShouldBeNull();

        var profile = result.Confidence.Sections.First(s => s.Kind == ParsedSectionKind.Profile);
        profile.Evidence.ShouldContain(e => e.Contains("dropped as contact-block material"));

        // A COUNT — never the text. This evidence rides parse_confidence, which is NOT encrypted.
        string.Join(" ", profile.Evidence).ShouldNotContain("Systemutvecklare");
    }

    [Fact]
    public void Segment_RailLineCarryingBOTHContactAndSummary_KeepsTheSummary()
    {
        // #844's own bug, on #844's own motivating layout. A rail line can BOTH end the contact block
        // and carry the user's summary. A LINE-granular position rule drops that summary and A8 goes
        // straight back to "Profiltext saknas helt." — the exact false Fail this whole change exists to
        // kill, re-created inside its own fix. The contact block therefore ends at the last consumed
        // FRAGMENT, and text after it on the same line survives.
        const string cv =
            """
            Anna Andersson | anna.andersson@example.com | Göteborg | Erfaren undersköterska med tio år i yrket

            Arbetslivserfarenhet
            Undersköterska — Vårdcentralen
            2015 - 2024
            """;

        var content = _sut.Segment(cv).Content;

        content.Preamble.ShouldBe("Erfaren undersköterska med tio år i yrket");

        // And the contact fields are still harvested from the same line — the name is not fabricated
        // out of the rebuilt remainder.
        content.Contact.FullName.ShouldBe("Anna Andersson");
        content.Contact.Email.ShouldBe("anna.andersson@example.com");
        content.Contact.Location.ShouldBe("Göteborg");
    }

    [Fact]
    public void Segment_CityOnANonContactLine_ReachesSOMEField_NeverVanishes()
    {
        // THE CONTRACT, as an observable: the subtraction and the extractor must AGREE about what is
        // contact material. When they disagree, a city is CLAIMED BY THE SUBTRACTION AND HARVESTED BY
        // NOBODY — absent from Location, absent from the carrier, present in NO FIELD AT ALL. That is
        // the silent loss this entire change exists to end, and it would have been introduced by the
        // very gate written to prevent the employer's-city fabrication. One rule, two call sites.
        //
        // "Göteborg | Erfaren undersköterska…" carries no contact span, so the city is NOT read as her
        // home (honest-absent — we cannot know). It must therefore survive in the carrier, verbatim.
        const string cv =
            """
            Göteborg | Erfaren undersköterska med tio års erfarenhet av natt

            Arbetslivserfarenhet
            Undersköterska — Vårdcentralen
            2015 - 2024
            """;

        var content = _sut.Segment(cv).Content;

        // Not asserted as her home — the line never identified itself as contact material.
        content.Contact.Location.ShouldBeNull();

        // But NOT thrown away either. It is carried, whole.
        content.Preamble.ShouldNotBeNull();
        content.Preamble.ShouldContain("Göteborg");
        content.Preamble.ShouldContain("Erfaren undersköterska");
    }

    // ── The confidence block is NOT a PII channel ──────────────────────────────────

    [Fact]
    public void Segment_UnheadedSummary_ProfileEvidenceCitesACount_NeverTheCarriedText()
    {
        // #844 made the preamble an INPUT to ProfileConfidence for the first time — and
        // ParseConfidence is the one place in this aggregate that is NOT encrypted:
        // ParsedResumeConfiguration stores it as "Non-PII metadata — plain jsonb"
        // (parse_confidence), and GetParsedResumeMapper hands the evidence strings straight out
        // through SectionConfidenceDto to the API.
        //
        // So a single interpolated string here would write CV prose — from the most
        // personnummer-dense region of the document — into a plaintext column and an HTTP
        // response, past the encryption pipeline that exists precisely to stop that (ADR 0074
        // Invariant 3, CLAUDE.md §5). The COUNT is the entire firewall. Nothing else is stopping it.
        var result = _sut.Segment(UnheadedSummaryCv);
        var profile = result.Confidence.Sections.Single(s => s.Kind == ParsedSectionKind.Profile);

        // Positive control: there really IS carried text, so the assertions below are not vacuous.
        result.Content.Preamble.ShouldNotBeNull();
        result.Content.Preamble.ShouldContain("betalbranschen");

        // The level is literally true and stays NotFound — no heading was detected. Stretching it to
        // Degraded would corrupt that level's meaning ("heading matched, empty block").
        profile.Level.ShouldBe(SectionConfidenceLevel.NotFound);

        // The signal the user needs: SOMETHING was carried. Without it she is left believing her
        // summary simply was not there.
        profile.Evidence.ShouldContain(e => e.Contains("unclassified", StringComparison.Ordinal));

        // The firewall: not one word of her CV, anywhere in the confidence block.
        var allEvidence = string.Join(
            " ", result.Confidence.Sections.SelectMany(s => s.Evidence));

        allEvidence.ShouldNotContain("betalbranschen");
        allEvidence.ShouldNotContain("driftsäkra");
        allEvidence.ShouldNotContain("Anna Andersson");
        allEvidence.ShouldNotContain("anna.andersson@example.com");
    }

    // ── The pathological bound ─────────────────────────────────────────────────────

    [Fact]
    public void Segment_HeadinglessCv_TruncatesOnALineBoundary_NeverMidSentence()
    {
        // The cap is a REAL content loss (RawText is not exposed in ParsedResumeDetailDto), so what
        // survives it must at least be text the user recognises as her own. A hard cut at char 2000
        // ends mid-word, and the guide would then offer her a half-sentence as candidate summary
        // text — the engine handing back a string she never wrote.
        //
        // The existing cap test asserts only length <= MaxPreambleChars, which a hard cut satisfies
        // perfectly. The line-boundary rule is what makes the truncation honest, and it needs its
        // own pin.
        //
        // The line is deliberately over IsNameLike's 60-char limit, so no line is claimed as the
        // name and every carried line must be a WHOLE source line.
        const string line = "Erfaren undersköterska med tio års erfarenhet av natt och trygg vård.";
        var giant = string.Join('\n', Enumerable.Repeat(line, 200));

        var preamble = _sut.Segment(giant).Content.Preamble;

        preamble.ShouldNotBeNull();
        preamble.Length.ShouldBeLessThanOrEqualTo(PreambleResidue.MaxPreambleChars);
        preamble.Split('\n').ShouldAllBe(carried => carried == line);
    }

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
