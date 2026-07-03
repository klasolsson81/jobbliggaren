using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Improvement;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Improvement;

/// <summary>
/// Fas 4 STEG B-2 — improvement-evidence-redaction hardening (CTO-bound,
/// <c>docs/reviews/2026-06-17-f4-improvement-evidence-redaction-*.md</c>; ADR 0074 Invariant 1;
/// CLAUDE.md §5 personnummer-guard). The deterministic CV-improvement engine MUST redact
/// personnummer/samordningsnummer out of EVERY <see cref="ProposedChange"/> user-text field —
/// the cited <see cref="TextSpanEvidence"/> (<c>Span.Quote</c> + <c>Note</c>),
/// <see cref="ProposedReplacement.Before"/>, and a STRUCTURAL <see cref="ProposedReplacement.After"/>
/// (only when the provenance is <see cref="StructuralTransformProvenance"/>) — BEFORE the
/// <see cref="CvImprovementResult"/> is assembled. NO AI/LLM (ADR 0071). Masking is via
/// <see cref="PersonnummerRedactor.Redact"/> / <see cref="Personnummer.Masked"/> (Domain.Privacy).
///
/// <para>CTO D1 = Variant B field-set, exact:
/// <list type="bullet">
/// <item>REDACTED: <c>Evidence</c> (TextSpanEvidence Quote + Note), <c>Replacement.Before</c>,
/// and a structural <c>Replacement.After</c> (StructuralTransformProvenance only,
/// e.g. HeadingNormalization).</item>
/// <item>LEFT UNTOUCHED: a KB <c>Replacement.After</c> (KnowledgeBankProvenance — Cliché/WeakVerb
/// curated value), <c>Rationale</c>, <c>Provenance.Key</c>, <c>Operation.Target</c>.</item>
/// </list>
/// CTO D5 = 3B: every redacted TextSpanEvidence whose Quote carried a pnr gets
/// <c>Span.Start == 0 &amp;&amp; Span.Length == 0</c> (no surviving offset back into RawText).</para>
///
/// <para>RED PHASE: the engine does NOT yet apply this pass — today DateNormalization quotes the
/// raw pnr when the user placed it inside a period string (the STEG B finding). These tests FAIL
/// until CC adds the redaction pass; CC writes production to green afterward. The test-writer does
/// NOT implement production.</para>
///
/// <para>Vectors derived from the real types/fixtures (never guessed): the Luhn-valid
/// <c>811218-9876</c> (= <c>SwedishCorpusLexicon.FakePersonnummer[0]</c>) rides into the engine via
/// the profile, an experience bullet, a non-canonical PERIOD string (the B6 vector that the
/// DateNormalization transform quotes), an education text, and a RawText heading line. The expected
/// mask <c>******-****</c> is the real <c>Personnummer.Masked</c> form (anchored below against the
/// scanner's own output). All vectors are SYNTHETIC test numbers.</para>
/// </summary>
public class CvImprovementEvidenceRedactionTests
{
    private const string Pnr = "811218-9876";
    private const string RawDigits = "8112189876"; // separator-free raw digits
    private const string Mask = "******-****";

    private static CvImprovementEngine NewEngine() =>
        new(RealClicheLexicon(), RealVerbMapper(), RealRubricProvider(), Analyzer());

    // The engine is null-tolerant on the F4-9 review (CTO Q2): run with review: null so the gates
    // exercise the engine's own rule logic (a review only enriches CriterionId, never the text).
    private static async Task<CvImprovementResult> SuggestAsync(ParsedResume resume, RenderProfile profile) =>
        await NewEngine().SuggestAsync(resume, review: null, profile, TestContext.Current.CancellationToken);

    public static TheoryData<RenderProfile> BothProfiles() =>
        new() { RenderProfile.Ats, RenderProfile.Visual };

    // ── Vector CVs ────────────────────────────────────────────────────────

    /// <summary>
    /// A CV that drives the pnr into every TextSpanEvidence-citing transform's quote/note surface:
    /// (a) profile, (b) an experience bullet, (c) a NON-CANONICAL period string (PeriodParser
    /// rejects "jan 2022 - …", so DateNormalization fires and quotes the period — the B6 core
    /// vector), (d) education text, (e) a heading line in RawText. The aggregate carries a PII-safe
    /// scan outcome so PersonnummerStrip (B4) also fires.
    /// </summary>
    private static ParsedResume ResumeWithPnr()
    {
        // The period is quoted verbatim by DateNormalization; it must (i) carry a digit and
        // (ii) NOT parse via PeriodParser ("jan" is a month NAME, not a recognised point) so the
        // transform flags it. The pnr lives inside the period AND inside the bullet rawText, so the
        // located span's Quote contains the pnr.
        var pnrPeriod = $"jan 2022 - {Pnr}";
        var bullet = $"Var ansvarig för betalsystem. Kontakt-pnr i CV: {Pnr}. Period: {pnrPeriod}.";

        return Resume(
            profile: $"Brinner för systemutveckling. Personnummer: {Pnr}.",
            experience:
            [
                Experience(period: pnrPeriod, rawText: bullet),
            ],
            education:
            [
                Education(rawText: $"KTH Civilingenjör. Pnr på intyget: {Pnr}."),
            ],
            rawText: $"Anna Andersson\nARBETSLIVSERFARENHET {Pnr}\n{bullet}",
            personnummer: PersonnummerScanOutcome.FromMatches(
                PersonnummerScanner.Scan($"Personnummer: {Pnr} {Pnr} {Pnr}")));
    }

    /// <summary>
    /// A clean CV whose ONLY proposed change is a DateNormalization flag (a non-canonical period
    /// with NO pnr): proves redaction does not zero a span that never covered a pnr (the 3B
    /// precision boundary). "jan 2022 - juni 2024" carries digits and is rejected by PeriodParser.
    /// </summary>
    private static ParsedResume CleanCvWithFlaggablePeriod()
    {
        var period = "jan 2022 - juni 2024";
        return Resume(
            profile: "Erfaren backend-utvecklare med åtta års erfarenhet inom betalsystem.",
            experience:
            [
                Experience(period: period, rawText: $"Backend-utvecklare. Period: {period}."),
            ]);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    // Every user-text string a ProposedChange exposes (the exhaustive field sweep, CTO D6).
    private static IEnumerable<string> ProposedChangeStrings(ProposedChange change)
    {
        switch (change.Evidence)
        {
            case TextSpanEvidence ts:
                yield return ts.Span.Quote;
                if (ts.Note is not null) yield return ts.Note;
                break;
            case StructuralEvidence se:
                yield return se.Observation;
                break;
        }

        if (change.Replacement is not null)
        {
            yield return change.Replacement.Before;
            yield return change.Replacement.After;
        }

        if (change.Operation is not null) yield return change.Operation.Target;
        yield return change.Rationale;
        yield return change.Provenance switch
        {
            KnowledgeBankProvenance kb => kb.Key,
            _ => string.Empty,
        };
    }

    private static IReadOnlyList<TextSpanEvidence> TextSpans(CvImprovementResult result) =>
        [.. result.Changes.Select(c => c.Evidence).OfType<TextSpanEvidence>()];

    // ===============================================================
    // Anti-stale anchor — the mask we assert is the real Personnummer.Masked form
    // ===============================================================

    [Fact]
    public void Mask_IsTheRealScannerMaskedForm()
    {
        var match = PersonnummerScanner.Scan(Pnr).ShouldHaveSingleItem();
        match.Masked.ShouldBe(Mask);
    }

    // ===============================================================
    // 1. Exhaustive field sweep — NO ProposedChange field echoes the raw pnr, both profiles
    // ===============================================================

    [Theory]
    [MemberData(nameof(BothProfiles))]
    public async Task SuggestAsync_ShouldNeverEchoTheRawPersonnummer_InAnyProposedChangeField(RenderProfile profile)
    {
        var result = await SuggestAsync(ResumeWithPnr(), profile);

        result.Changes.ShouldNotBeEmpty($"{profile}: the pnr fixture must drive at least one change.");

        foreach (var change in result.Changes)
        {
            foreach (var s in ProposedChangeStrings(change))
            {
                s.ShouldNotContain(Pnr,
                    Case.Sensitive,
                    $"{profile}/{change.TargetId}: a ProposedChange field echoed the raw personnummer (Inv. 1).");
                s.ShouldNotContain(RawDigits);
            }
        }
    }

    // ===============================================================
    // 2. Mask presence — at least one field shows the masked form (proves masking, not just strip)
    // ===============================================================

    [Theory]
    [MemberData(nameof(BothProfiles))]
    public async Task SuggestAsync_ShouldShowTheMaskedForm_WhereAFieldOriginallyCarriedThePnr(RenderProfile profile)
    {
        var result = await SuggestAsync(ResumeWithPnr(), profile);

        var fields = result.Changes.SelectMany(ProposedChangeStrings).ToList();

        fields.ShouldContain(f => f.Contains(Mask, StringComparison.Ordinal),
            $"{profile}: at least one redacted field should carry the mask '{Mask}', not just drop the digits.");
    }

    // ===============================================================
    // 3. CTO D5 = 3B — offset zeroed on every TextSpan whose Quote carried a pnr
    // ===============================================================

    [Theory]
    [MemberData(nameof(BothProfiles))]
    public async Task SuggestAsync_ShouldZeroSpanOffset_OnEveryTextSpanThatCoveredAPnr(RenderProfile profile)
    {
        var result = await SuggestAsync(ResumeWithPnr(), profile);

        var redactedSpans = TextSpans(result)
            .Where(ts => ts.Span.Quote.Contains(Mask, StringComparison.Ordinal))
            .ToList();

        redactedSpans.ShouldNotBeEmpty(
            $"{profile}: the pnr fixture must produce at least one redacted (masked) span to assert 3B on.");

        foreach (var ts in redactedSpans)
        {
            ts.Span.Start.ShouldBe(0, $"{profile}: a redacted pnr span must zero its Start (3B).");
            ts.Span.Length.ShouldBe(0, $"{profile}: a redacted pnr span must zero its Length (3B).");
        }
    }

    // ===============================================================
    // 4. Before == Quote holds AFTER redaction for KB transforms (Cliché/WeakVerb) + Heading —
    //    Before and the cited Quote are masked identically (the propose-and-approve contract).
    // ===============================================================

    [Theory]
    [MemberData(nameof(BothProfiles))]
    public async Task SuggestAsync_ShouldKeepBeforeEqualToQuote_AfterRedaction(RenderProfile profile)
    {
        var result = await SuggestAsync(ResumeWithPnr(), profile);

        var textGroundedReplacements = result.Changes
            .Where(c => c.Replacement is not null && c.Evidence is TextSpanEvidence)
            .ToList();

        textGroundedReplacements.ShouldNotBeEmpty(
            $"{profile}: the fixture must produce a text-grounded replacement (Cliché/WeakVerb/Heading).");

        foreach (var change in textGroundedReplacements)
        {
            var quote = ((TextSpanEvidence)change.Evidence).Span.Quote;
            change.Replacement!.Before.ShouldBe(quote,
                $"{profile}/{change.TargetId}: Before must equal the cited Quote after redaction — " +
                "both masked identically (propose-and-approve contract).");
        }
    }

    // ===============================================================
    // 5. KB After untouched — Cliché/WeakVerb Replacement.After == the verbatim KB value
    //    (no over-redaction of curated knowledge-bank text)
    // ===============================================================

    [Theory]
    [MemberData(nameof(BothProfiles))]
    public async Task SuggestAsync_ShouldLeaveKnowledgeBankAfterUntouched_ForWeakVerb(RenderProfile profile)
    {
        // The pnr CV triggers a WeakVerb ("Var ansvarig för") whose After is a curated KB value
        // with no pnr in it — redaction must NOT alter it. Expected value read from the real asset
        // (verb-mapping.v1.json). NOTE (#495): the cliché arm no longer contributes a KB After —
        // today's cliche-list.v2.json carries no genuine drop-in, so a cliché is flagged (A7) but
        // never rewritten; the drop-in-untouched-by-redaction path is covered by the weak verb here
        // (and by the fake-lexicon drop-in tests in CvImprovementEngineTests).
        const string WeakVerbAfter = "ansvarade för";

        var result = await SuggestAsync(ResumeWithPnr(), profile);

        var kbAfters = result.Changes
            .Where(c => c.Provenance is KnowledgeBankProvenance && c.Replacement is not null)
            .Select(c => c.Replacement!.After)
            .ToList();

        kbAfters.ShouldContain(WeakVerbAfter,
            $"{profile}: the weak-verb 'Var ansvarig för' After must be the verbatim KB SuggestedStrong " +
            "(redaction must not touch a KnowledgeBankProvenance After).");
    }

    // ===============================================================
    // 6. Structural After is redacted — a heading-normalization change's After is masked when it
    //    carries a pnr.
    //
    // NOTE (fixture gap, reported to CC): the heading transform only fires on a raw-text LINE that
    // EXACTLY equals a canonical section heading (case-insensitive), so a heading line can never
    // also carry a pnr — there is no production path that puts a pnr into a structural After today.
    // This test therefore binds the WEAKER, satisfiable invariant the field-set still requires:
    // a heading-normalization change exists and its structural After carries NO raw pnr (so the
    // redaction pass, if/when a future transform produces a pnr-bearing structural After, is the
    // only thing that can keep this green). The strong "structural After masked" assertion is
    // specified in the report for CC to add behind a transform that can actually produce it.
    // ===============================================================

    [Fact]
    public async Task SuggestAsync_ShouldRedactStructuralAfter_ForHeadingNormalization()
    {
        // RawText carries an ALL-CAPS canonical heading ("ARBETSLIVSERFARENHET") so the heading
        // transform fires with a StructuralTransformProvenance + a Replacement.After.
        var resume = Resume(
            rawText: "Anna Andersson\nARBETSLIVSERFARENHET\nLedde teamet om 8 personer.");

        var result = await SuggestAsync(resume, RenderProfile.Ats);

        var heading = result.Changes
            .SingleOrDefault(c => c.Kind == ProposedChangeKind.HeadingNormalization);

        heading.ShouldNotBeNull(
            "an ALL-CAPS canonical heading must produce a HeadingNormalization change " +
            "(proves the structural-After channel is exercised).");
        heading!.Provenance.ShouldBeOfType<StructuralTransformProvenance>();
        heading.Replacement.ShouldNotBeNull();
        heading.Replacement!.After.ShouldNotContain(Pnr);
        heading.Replacement.After.ShouldNotContain(RawDigits);
    }

    // ===============================================================
    // 7. No over-redaction on a clean CV — Quote/Before/After intact, offsets NOT zeroed (the 3B
    //    precision boundary, parity #110 test 6).
    // ===============================================================

    [Fact]
    public async Task SuggestAsync_ShouldNotAlterACleanChange_WhenNoPersonnummerPresent()
    {
        var result = await SuggestAsync(CleanCvWithFlaggablePeriod(), RenderProfile.Ats);

        var spans = TextSpans(result);
        spans.ShouldNotBeEmpty("a flaggable-but-clean period must cite a present-text span.");

        // Nothing is masked on a clean CV.
        result.Changes.SelectMany(ProposedChangeStrings)
            .ShouldNotContain(s => s.Contains(Mask, StringComparison.Ordinal),
                "a clean CV has nothing to mask — no '" + Mask + "' should appear.");

        // The flaggable period span keeps its real (non-zero) pointer — redaction does NOT zero a
        // span that never covered a pnr (the 3B precision boundary).
        spans.ShouldContain(ts => ts.Span.Length > 0,
            "a clean text-span keeps its real Length — 3B must not zero spans without a pnr.");

        foreach (var ts in spans)
        {
            ts.Span.Length.ShouldBe(ts.Span.Quote.Length,
                "a clean span's Length must still equal its quote length (untouched by redaction).");
        }
    }

    // ===============================================================
    // 8. The B6 PERIOD vector (the core leak, CTO-bound in-block) — the DateNormalization change
    //    quotes the period; with a pnr inside the period its Quote must be masked + zeroed, and no
    //    field of ANY change echoes the pnr injected across profile/bullet/period/education/heading.
    // ===============================================================

    [Theory]
    [MemberData(nameof(BothProfiles))]
    public async Task SuggestAsync_ShouldRedactThePeriodVector_AcrossEveryInjectionSite(RenderProfile profile)
    {
        var result = await SuggestAsync(ResumeWithPnr(), profile);

        // The DateNormalization change must exist (non-canonical period) and must NOT echo the pnr.
        var dateChange = result.Changes
            .SingleOrDefault(c => c.Kind == ProposedChangeKind.DateNormalization);

        dateChange.ShouldNotBeNull(
            $"{profile}: a non-canonical period must produce a DateNormalization change (the B6 vector).");

        var dateSpan = dateChange!.Evidence.ShouldBeOfType<TextSpanEvidence>();
        dateSpan.Span.Quote.ShouldNotContain(Pnr,
            Case.Sensitive,
            $"{profile}: the DateNormalization period Quote echoed the raw pnr (the core leak).");
        dateSpan.Span.Quote.ShouldNotContain(RawDigits);

        // And the global sweep holds for every injection site (profile/bullet/period/education/heading).
        foreach (var change in result.Changes)
        {
            foreach (var s in ProposedChangeStrings(change))
            {
                s.ShouldNotContain(Pnr, Case.Sensitive,
                    $"{profile}/{change.TargetId}: a field echoed the pnr from one of the injection sites.");
                s.ShouldNotContain(RawDigits);
            }
        }
    }
}
