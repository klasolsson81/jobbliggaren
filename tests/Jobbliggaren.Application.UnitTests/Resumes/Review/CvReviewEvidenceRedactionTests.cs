using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Review;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Review.CvReviewFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Review;

/// <summary>
/// Fas 4 hardening-STEG (CTO-bound, <c>docs/reviews/2026-06-16-f4-hardening-pnr-evidence-cto.md</c>) —
/// the CV-review engine MUST redact personnummer/samordningsnummer out of EVERY
/// <see cref="TextSpanEvidence"/> (Quote + Note) BEFORE a <c>CvReviewResult</c> can be
/// logged/cached/persisted (ADR 0074 Invariant 1; the security-auditor BINDING OBLIGATION).
/// NO AI/LLM (ADR 0071). The redaction is an inline pass in <c>CvReviewEngine.ReviewAsync</c>
/// BEFORE <c>BuildCategories</c> (CTO Fork 1 = 1A), masking via <c>Personnummer.Masked</c>
/// (Fork 2b), zeroing <c>Span.Start</c>/<c>Span.Length</c> on spans that covered a pnr
/// (Fork 3 = 3B).
///
/// <para>RED PHASE: the engine does NOT yet apply this pass — today A1/A2/A6/A8 quote the raw
/// pnr when the user placed it inside profile/experience text (the STEG C finding). These tests
/// FAIL until CC adds the redaction pass; CC writes production to green afterward. The
/// test-writer does NOT implement production.</para>
///
/// <para>Vectors derived from the real types/fixtures (never guessed): the Luhn-valid
/// <c>811218-9876</c> rides into the engine via the profile + an experience rawText, with
/// <see cref="PersonnummerScanOutcome.FromMatches"/> over the real scanner so B4 sees it. The
/// expected mask <c>******-****</c> is the real <c>Personnummer.Masked</c> form (anchored
/// below against the scanner's own output). All vectors are SYNTHETIC test numbers.</para>
/// </summary>
public class CvReviewEvidenceRedactionTests
{
    private const string Pnr = "811218-9876";
    private const string RawDigits = "8112189876"; // separator-free raw digits
    private const string Mask = "******-****";

    private static CvReviewEngine NewEngine() =>
        new(RealRubricProvider(), RealClicheLexicon(), RealVerbMapper(), Analyzer());

    private static async Task<CvReviewResult> ReviewAsync(ParsedResume resume, RenderProfile profile) =>
        await NewEngine().ReviewAsync(resume, profile, TestContext.Current.CancellationToken);

    /// <summary>
    /// A CV that deliberately carries the personnummer where the present-text rules quote it:
    /// in the profile (A8 cites <c>Truncate(profile)</c>) and in an experience rawText
    /// (A1/A6 cite the offending/quantified bullet). B4 sees it via the PII-safe scan outcome.
    /// </summary>
    private static ParsedResume ResumeWithPnr() =>
        Resume(
            profile: $"Systemvetare. Personnummer: {Pnr}.",
            experience:
            [
                Experience(rawText: $"Backend-utvecklare. Kontakt-pnr i CV: {Pnr}."),
            ],
            personnummer: PersonnummerScanOutcome.FromMatches(
                PersonnummerScanner.Scan($"Personnummer: {Pnr} {Pnr}")));

    // The closed evidence hierarchy is TextSpanEvidence / StructuralEvidence (CitedEvidence.cs).
    private static IEnumerable<string> EvidenceStrings(CvCriterionVerdict v)
    {
        foreach (var e in v.Evidence)
        {
            switch (e)
            {
                case TextSpanEvidence ts:
                    yield return ts.Span.Quote;
                    if (ts.Note is not null) yield return ts.Note;
                    break;
                case StructuralEvidence se:
                    yield return se.Observation;
                    break;
            }
        }

        if (v.NotAssessedReason is not null) yield return v.NotAssessedReason;
    }

    public static TheoryData<RenderProfile> BothProfiles() =>
        new() { RenderProfile.Ats, RenderProfile.Visual };

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
    // 1. No leak across ALL verdicts / both profiles (the BINDING OBLIGATION)
    // ===============================================================

    [Theory]
    [MemberData(nameof(BothProfiles))]
    public async Task ReviewAsync_ShouldNeverEchoTheRawPersonnummer_InAnyTextSpanEvidence(RenderProfile profile)
    {
        var result = await ReviewAsync(ResumeWithPnr(), profile);

        foreach (var verdict in result.Verdicts)
        {
            foreach (var e in verdict.Evidence)
            {
                if (e is TextSpanEvidence ts)
                {
                    ts.Span.Quote.ShouldNotContain(Pnr,
                        Case.Sensitive,
                        $"{profile}/{verdict.CriterionId}: Quote echoed the raw personnummer (Inv. 1).");
                    ts.Span.Quote.ShouldNotContain(RawDigits);

                    if (ts.Note is not null)
                    {
                        ts.Note.ShouldNotContain(Pnr,
                            Case.Sensitive,
                            $"{profile}/{verdict.CriterionId}: Note echoed the raw personnummer (Inv. 1).");
                        ts.Note.ShouldNotContain(RawDigits);
                    }
                }
            }
        }
    }

    // ===============================================================
    // 2. The masked form is what shows where a quote originally carried the pnr
    // ===============================================================

    [Theory]
    [MemberData(nameof(BothProfiles))]
    public async Task ReviewAsync_ShouldShowTheMaskedForm_WhereAQuoteOriginallyCarriedThePnr(RenderProfile profile)
    {
        // Redaction MASKS (does not merely delete): at least one verdict's TextSpanEvidence Quote
        // contains the masked form. (A8 cites the profile whole — "…Personnummer: ******-****." —
        // so the mask is present, proving the pass ran, not just stripped the digits.)
        var result = await ReviewAsync(ResumeWithPnr(), profile);

        var quotes = result.Verdicts
            .SelectMany(v => v.Evidence)
            .OfType<TextSpanEvidence>()
            .Select(ts => ts.Span.Quote)
            .ToList();

        quotes.ShouldContain(q => q.Contains(Mask, StringComparison.Ordinal),
            $"{profile}: at least one redacted quote should carry the mask '{Mask}', not just drop the digits.");
    }

    // ===============================================================
    // 3. Fork 3 = 3B — offset zeroed on pnr spans (no surviving pointer into RawText)
    // ===============================================================

    [Theory]
    [MemberData(nameof(BothProfiles))]
    public async Task ReviewAsync_ShouldZeroSpanOffset_OnEveryTextSpanThatCoveredAPnr(RenderProfile profile)
    {
        // Any TextSpanEvidence whose Quote was redacted (now carries the mask) must have its
        // Start/Length zeroed — no offset survives that could re-slice the raw pnr out of the
        // decrypted RawText (GDPR Art. 5(1)(c) data-minimisation, CTO Fork 3 = 3B).
        var result = await ReviewAsync(ResumeWithPnr(), profile);

        var redactedSpans = result.Verdicts
            .SelectMany(v => v.Evidence)
            .OfType<TextSpanEvidence>()
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
    // 4. B4 unchanged — the already-safe structural channel is not altered
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldLeaveB4AsFailWithStructuralEvidence_WhenPnrPresent()
    {
        // Regression guard: redaction touches Quote/Note on TextSpanEvidence, NEVER the count-only
        // B4 StructuralEvidence channel — B4 still Fails and still cites structurally.
        var result = await ReviewAsync(ResumeWithPnr(), RenderProfile.Ats);

        var b4 = Verdict(result, "B4");
        b4.Verdict.ShouldBe(CriterionVerdict.Fail,
            "B4 must still FAIL when a personnummer is present (critical, ADR 0071 Decision 2).");
        b4.Evidence.ShouldContain(e => e is StructuralEvidence,
            "B4 cites the count structurally; redaction must not turn it into a text span.");
        b4.Evidence.ShouldNotContain(e => e is TextSpanEvidence,
            "B4's channel stays count-only — redaction does not add a text span to it.");

        foreach (var s in EvidenceStrings(b4))
        {
            s.ShouldNotContain(Pnr);
            s.ShouldNotContain(RawDigits);
        }
    }

    // ===============================================================
    // 5. Categories + CriticalFails carry the redacted verdicts too (double-embedding guard, 1A)
    // ===============================================================

    [Theory]
    [MemberData(nameof(BothProfiles))]
    public async Task ReviewAsync_ShouldRedactBeforeBuildingCategories_SoNoProjectionEchoesThePnr(RenderProfile profile)
    {
        // The same verdict instances are projected into Categories[*].Verdicts AND CriticalFails.
        // Redaction MUST happen before BuildCategories (CTO Fork 1 = 1A) so none of those views
        // echo the raw pnr — the load-bearing double-embedding guard.
        var result = await ReviewAsync(ResumeWithPnr(), profile);

        var projectedVerdicts = result.Categories
            .SelectMany(c => c.Verdicts)
            .Concat(result.CriticalFails);

        foreach (var verdict in projectedVerdicts)
        {
            foreach (var s in EvidenceStrings(verdict))
            {
                s.ShouldNotContain(Pnr,
                    Case.Sensitive,
                    $"{profile}/{verdict.CriterionId}: a Category/CriticalFail projection echoed the raw pnr " +
                    "→ redaction did not run before BuildCategories (1A).");
                s.ShouldNotContain(RawDigits);
            }
        }
    }

    // ===============================================================
    // 6. No over-redaction of a clean CV — normal verdicts and offsets are unaffected
    // ===============================================================

    [Fact]
    public async Task ReviewAsync_ShouldNotAlterACleanCvReview_WhenNoPersonnummerPresent()
    {
        // A clean CV (no pnr): a normal Pass-bearing TextSpanEvidence quote is intact and its
        // span is NOT zeroed — redaction only touches spans that covered a pnr.
        var result = await ReviewAsync(Resume(), RenderProfile.Ats);

        var textSpans = result.Verdicts
            .SelectMany(v => v.Evidence)
            .OfType<TextSpanEvidence>()
            .ToList();

        textSpans.ShouldNotBeEmpty("a clean CV still cites present-text spans (e.g. A8 profile).");

        // No mask appears anywhere on a clean CV (nothing was redacted).
        textSpans.ShouldNotContain(ts => ts.Span.Quote.Contains(Mask, StringComparison.Ordinal),
            "a clean CV has nothing to mask — no '" + Mask + "' should appear.");

        // Clean spans keep their real (non-zero-length) quote pointer — redaction does NOT zero
        // a span that never covered a pnr (the 3B precision boundary).
        textSpans.ShouldContain(ts => ts.Span.Length > 0,
            "a clean text-span keeps its real Length — 3B must not zero spans without a pnr.");

        foreach (var ts in textSpans)
        {
            ts.Span.Length.ShouldBe(ts.Span.Quote.Length,
                "a clean span's Length must still equal its quote length (untouched by redaction).");
        }
    }

    // ===============================================================
    // 7. #268 C2 — a personnummer in the CV FILENAME (B8 StructuralEvidence) is masked.
    //    B8 interpolates the raw SourceFileName into a StructuralEvidence observation; the
    //    redactor now runs over that channel too, so a filename-borne pnr never surfaces.
    // ===============================================================

    [Theory]
    [MemberData(nameof(BothProfiles))]
    public async Task ReviewAsync_ShouldMaskAPersonnummerInTheFilename_InB8StructuralEvidence(RenderProfile profile)
    {
        // A user who names their CV with a personnummer ("CV_811218-9876.pdf"). The filename is not
        // the recommended CV_Förnamn_Efternamn shape (digits where letters are required), so B8 warns
        // and quotes the filename in a StructuralEvidence observation — which must be masked (Inv. 1).
        var resume = Resume(sourceFileName: $"CV_{Pnr}.pdf");

        var result = await ReviewAsync(resume, profile);

        var b8 = Verdict(result, "B8");
        b8.Verdict.ShouldBe(CriterionVerdict.Warn,
            "a digit-bearing filename does not match the CV_Förnamn_Efternamn recommendation → B8 warns.");

        foreach (var s in EvidenceStrings(b8))
        {
            s.ShouldNotContain(Pnr,
                Case.Sensitive,
                $"{profile}/B8: the filename's personnummer must be masked in the structural observation (Inv. 1).");
            s.ShouldNotContain(RawDigits);
        }

        // Masking (not deletion): the masked form shows where the filename carried the pnr.
        b8.Evidence.OfType<StructuralEvidence>()
            .ShouldContain(e => e.Observation.Contains(Mask, StringComparison.Ordinal),
                $"{profile}/B8: the structural observation should carry the mask '{Mask}'.");
    }
}
