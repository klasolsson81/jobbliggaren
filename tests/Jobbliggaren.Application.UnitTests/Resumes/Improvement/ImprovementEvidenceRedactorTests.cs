using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Infrastructure.Resumes.Improvement;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Improvement;

/// <summary>
/// Fas 4 STEG B-2 — direct seam tests for <see cref="ImprovementEvidenceRedactor"/> (CTO-bound,
/// <c>docs/reviews/2026-06-17-f4-improvement-evidence-redaction-cto.md</c>; ADR 0074 Invariant 1).
/// The engine-level <c>CvImprovementEvidenceRedactionTests</c> drive the redactor through real
/// transforms, but no production transform can today emit a personnummer-bearing STRUCTURAL
/// <c>Replacement.After</c> (HeadingNormalization only fires on an exact canonical heading line,
/// which cannot contain a pnr). These seam tests therefore pin the two branches the engine cannot
/// reach: that a structural After IS masked, and that a knowledge-bank After is LEFT untouched
/// (the provenance discrimination that field-set decision D1 = Variant B rests on). Reaches the
/// internal redactor via Infrastructure's <c>InternalsVisibleTo(Application.UnitTests)</c>.
/// All vectors are SYNTHETIC test numbers.
/// </summary>
public class ImprovementEvidenceRedactorTests
{
    private const string Pnr = "811218-9876";
    private const string RawDigits = "8112189876";
    private const string Mask = "******-****";

    private static TextSpanEvidence SpanEvidence(string quote) =>
        new(new TextSpan(0, quote.Length, quote), Note: null);

    [Fact]
    public void Mask_IsTheRealScannerMaskedForm()
    {
        PersonnummerScanner.Scan(Pnr).ShouldHaveSingleItem().Masked.ShouldBe(Mask);
    }

    [Fact]
    public void Redact_ShouldMaskAStructuralAfter_WhenItCarriesAPersonnummer()
    {
        // A structural change whose Before AND After carry the pnr (pureTransform = ToLowerInvariant
        // preserves digits). This is the branch no production transform reaches today — D1 = Variant B
        // redacts it so a future phrase-level structural transform is safe by construction.
        var before = $"RUBRIK {Pnr}";
        var after = before.ToLowerInvariant();
        var change = ProposedChange.FromStructuralOp(
            targetId: "heading:0",
            kind: ProposedChangeKind.HeadingNormalization,
            category: RubricCategory.Structure,
            criterionId: "D6",
            evidence: SpanEvidence(before),
            replacement: new ProposedReplacement(before, after),
            operation: new StructuralOperation(StructuralTransformKind.NormalizeHeadingCase, "rubrik"),
            rationale: "Standardisera rubrik.",
            provenance: new StructuralTransformProvenance(StructuralTransformKind.NormalizeHeadingCase),
            pureTransform: s => s.ToLowerInvariant());

        var redacted = ImprovementEvidenceRedactor.Redact([change]).ShouldHaveSingleItem();

        var quote = ((TextSpanEvidence)redacted.Evidence).Span.Quote;
        quote.ShouldContain(Mask);
        quote.ShouldNotContain(Pnr);
        ((TextSpanEvidence)redacted.Evidence).Span.Start.ShouldBe(0, "3B: a redacted pnr span zeroes Start.");
        ((TextSpanEvidence)redacted.Evidence).Span.Length.ShouldBe(0, "3B: a redacted pnr span zeroes Length.");

        redacted.Replacement!.Before.ShouldContain(Mask);
        redacted.Replacement.Before.ShouldNotContain(Pnr);
        redacted.Replacement.After.ShouldContain(Mask, Case.Sensitive,
            "a structural After (a pure transform of the user's text) must be masked — CTO D1 = Variant B.");
        redacted.Replacement.After.ShouldNotContain(Pnr);
        redacted.Replacement.After.ShouldNotContain(RawDigits);
    }

    [Fact]
    public void Redact_ShouldLeaveAKnowledgeBankAfterUntouched_WhileMaskingBefore()
    {
        // A KB change whose cited Before carries the pnr but whose After is a clean curated KB value.
        // The redactor masks Before (user text) but MUST NOT touch the KB After (KnowledgeBankProvenance) —
        // a curated value pinned to the resolved KB string by the no-synthesis contract. This is the
        // provenance discrimination D1 rests on. (In production a KB After can never carry a pnr; this
        // pins that the discrimination keys off provenance, not the After's content.)
        var before = $"Brinner för {Pnr}";
        const string kbAfter = "Beskriv ett konkret projekt eller initiativ.";
        var change = ProposedChange.FromKnowledgeBank(
            targetId: "cliche:0",
            kind: ProposedChangeKind.ClicheReplacement,
            category: RubricCategory.Content,
            criterionId: "A7",
            evidence: SpanEvidence(before),
            replacement: new ProposedReplacement(before, kbAfter),
            rationale: "Byt klyscha.",
            provenance: new KnowledgeBankProvenance("cliche-list", "1", before),
            resolvedKbValue: kbAfter);

        var redacted = ImprovementEvidenceRedactor.Redact([change]).ShouldHaveSingleItem();

        redacted.Replacement!.Before.ShouldContain(Mask, Case.Sensitive, "the user-text Before is masked.");
        redacted.Replacement.Before.ShouldNotContain(Pnr);
        redacted.Replacement.After.ShouldBe(kbAfter,
            "a KnowledgeBankProvenance After is the curated KB value — redaction must leave it byte-identical.");
        ((TextSpanEvidence)redacted.Evidence).Span.Quote.ShouldContain(Mask);
        ((TextSpanEvidence)redacted.Evidence).Span.Start.ShouldBe(0);
        ((TextSpanEvidence)redacted.Evidence).Span.Length.ShouldBe(0);
    }

    [Fact]
    public void Redact_ShouldReturnTheSameInstance_WhenNoFieldCarriesAPnr()
    {
        // The allocation-light fast path: a change with no personnummer anywhere is returned as the
        // SAME instance (no copy), so a pnr-free CV (the common case) costs nothing.
        var change = ProposedChange.FromKnowledgeBank(
            targetId: "weakverb:0",
            kind: ProposedChangeKind.WeakVerbUpgrade,
            category: RubricCategory.Content,
            criterionId: "A2",
            evidence: SpanEvidence("Ledde"),
            replacement: new ProposedReplacement("Ledde", "ansvarade för"),
            rationale: "Starkare verb.",
            provenance: new KnowledgeBankProvenance("verb-mapping", "1", "Ledde"),
            resolvedKbValue: "ansvarade för");

        var redacted = ImprovementEvidenceRedactor.Redact([change]).ShouldHaveSingleItem();

        redacted.ShouldBeSameAs(change, "a pnr-free change is returned unchanged (no allocation).");
    }

    [Fact]
    public void Redact_ShouldMaskAStructuralEvidenceObservation_WhenItCarriesAPersonnummer()
    {
        // #268 C2: the StructuralEvidence channel is now redacted too (parity with the review-side
        // EvidenceRedactor). No production improvement transform emits a pnr-bearing structural
        // observation today — this seam test pins the by-construction guard so a future
        // phrase-level structural transform is safe. A null replacement isolates the evidence channel.
        var change = ProposedChange.FromStructuralOp(
            targetId: "structural:0",
            kind: ProposedChangeKind.HeadingNormalization,
            category: RubricCategory.Structure,
            criterionId: "D6",
            evidence: new StructuralEvidence($"Strukturell observation som råkar bära {Pnr}."),
            replacement: null,
            operation: new StructuralOperation(StructuralTransformKind.NormalizeHeadingCase, "rubrik"),
            rationale: "Strukturell not.",
            provenance: new StructuralTransformProvenance(StructuralTransformKind.NormalizeHeadingCase),
            pureTransform: null);

        var redacted = ImprovementEvidenceRedactor.Redact([change]).ShouldHaveSingleItem();

        var observation = redacted.Evidence.ShouldBeOfType<StructuralEvidence>().Observation;
        observation.ShouldContain(Mask, Case.Sensitive,
            "a structural observation carrying a personnummer must be masked (#268 C2, Inv. 1).");
        observation.ShouldNotContain(Pnr);
        observation.ShouldNotContain(RawDigits);
    }

    [Fact]
    public void Redact_ShouldReturnSameInstance_ForACountOnlyStructuralObservation()
    {
        // A genuine count-only structural observation holds no Luhn-valid number → returned
        // unchanged (the B4-style channel is not disturbed by the #268 C2 widening).
        var change = ProposedChange.FromStructuralOp(
            targetId: "structural:1",
            kind: ProposedChangeKind.HeadingNormalization,
            category: RubricCategory.Structure,
            criterionId: "D6",
            evidence: new StructuralEvidence("1 personnummer hittat."),
            replacement: null,
            operation: new StructuralOperation(StructuralTransformKind.NormalizeHeadingCase, "rubrik"),
            rationale: "Strukturell not.",
            provenance: new StructuralTransformProvenance(StructuralTransformKind.NormalizeHeadingCase),
            pureTransform: null);

        var redacted = ImprovementEvidenceRedactor.Redact([change]).ShouldHaveSingleItem();

        redacted.ShouldBeSameAs(change, "a count-only structural observation is returned unchanged.");
    }
}
