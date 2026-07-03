using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Privacy;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Improvement;

/// <summary>
/// Fas 4 STEG 10 (F4-10, ADR 0074) — the no-synthesis guards on <see cref="ProposedChange"/>.
/// These are the LOAD-BEARING tests of the engine's honesty contract (CLAUDE.md §5:
/// "synthesising prose the user did not write" is forbidden — the determinism may only
/// propose a value resolved from the versioned knowledge bank, or a verified pure structural
/// transform of text the user actually wrote).
///
/// A private ctor + two static factories make the guard UNAVOIDABLE — no caller can mint a
/// <see cref="ProposedChange"/> whose <c>After</c> text is not exactly the KB-resolved value
/// (or a verified transform output), and whose <c>Before</c> is not exactly the cited span.
/// Mirrors the F4-9 <c>CvCriterionVerdict.Assessed/NotAssessed</c> guard tests.
///
/// SPEC-DRIVEN against the architect-bound factory signatures. RED until ProposedChange + the
/// provenance union + the operation/replacement records ship in
/// Jobbliggaren.Application.Resumes.Improvement.Abstractions.
/// </summary>
public class ProposedChangeFactoryTests
{
    private const string TargetId = "exp-0";
    private const string Rationale = "Tom passion-signal som alla använder";

    private static TextSpanEvidence SpanEvidence(string quote) =>
        new(new TextSpan(0, quote.Length, quote), Note: "klyscha");

    private static KnowledgeBankProvenance KbProvenance(string key) =>
        new(Source: "cliche-list", Version: "2", Key: key);

    private static StructuralTransformProvenance StructProvenance(StructuralTransformKind kind) =>
        new(kind);

    // ===============================================================
    // FromKnowledgeBank — happy path
    // ===============================================================

    [Fact]
    public void FromKnowledgeBank_ShouldSucceed_WhenAfterEqualsResolvedKbValueAndBeforeEqualsQuote()
    {
        const string before = "Brinner för";
        const string after = "Beskriv ett konkret projekt eller initiativ.";

        var change = ProposedChange.FromKnowledgeBank(
            targetId: TargetId,
            kind: ProposedChangeKind.ClicheReplacement,
            category: RubricCategory.Content,
            criterionId: "A7",
            evidence: SpanEvidence(before),
            replacement: new ProposedReplacement(before, after),
            rationale: Rationale,
            provenance: KbProvenance(before),
            resolvedKbValue: after);

        change.TargetId.ShouldBe(TargetId);
        change.Kind.ShouldBe(ProposedChangeKind.ClicheReplacement);
        change.Replacement.ShouldNotBeNull();
        change.Replacement!.After.ShouldBe(after);
        change.Operation.ShouldBeNull();
        change.Provenance.ShouldBeOfType<KnowledgeBankProvenance>();
    }

    // ===============================================================
    // FromKnowledgeBank — no-synthesis guards (the load-bearing tests)
    // ===============================================================

    [Fact]
    public void FromKnowledgeBank_ShouldThrow_WhenAfterDiffersFromResolvedKbValue()
    {
        // After must be EXACTLY the value the engine resolved from the KB — a hand-tweaked
        // "After" would be synthesised prose (CLAUDE.md §5). Throws ArgumentException.
        const string before = "Brinner för";

        var act = () => ProposedChange.FromKnowledgeBank(
            targetId: TargetId,
            kind: ProposedChangeKind.ClicheReplacement,
            category: RubricCategory.Content,
            criterionId: "A7",
            evidence: SpanEvidence(before),
            replacement: new ProposedReplacement(before, "en helt påhittad ersättning"),
            rationale: Rationale,
            provenance: KbProvenance(before),
            resolvedKbValue: "Beskriv ett konkret projekt eller initiativ.");

        var ex = act.ShouldThrow<ArgumentException>();
        ex.Message.ShouldContain("0074", Case.Sensitive,
            "Felmeddelandet ska referera no-synthesis/ADR 0074-intentionen.");
    }

    [Fact]
    public void FromKnowledgeBank_ShouldThrow_WhenBeforeDiffersFromCitedTextSpanQuote()
    {
        // Before must be EXACTLY the cited span the verdict quotes — proposing to replace a
        // span the user did not actually write breaks the propose-and-approve contract.
        const string after = "Beskriv ett konkret projekt eller initiativ.";

        var act = () => ProposedChange.FromKnowledgeBank(
            targetId: TargetId,
            kind: ProposedChangeKind.ClicheReplacement,
            category: RubricCategory.Content,
            criterionId: "A7",
            evidence: SpanEvidence("Brinner för"),
            replacement: new ProposedReplacement("Något annat citat", after),
            rationale: Rationale,
            provenance: KbProvenance("Brinner för"),
            resolvedKbValue: after);

        var ex = act.ShouldThrow<ArgumentException>();
        ex.Message.ShouldContain("0074");
    }

    [Fact]
    public void FromKnowledgeBank_ShouldThrow_WhenProvenanceIsNotKnowledgeBankArm()
    {
        // A KB replacement must carry KnowledgeBankProvenance — a structural arm here would
        // mislabel the change's source.
        const string before = "Brinner för";
        const string after = "Beskriv ett konkret projekt eller initiativ.";

        var act = () => ProposedChange.FromKnowledgeBank(
            targetId: TargetId,
            kind: ProposedChangeKind.ClicheReplacement,
            category: RubricCategory.Content,
            criterionId: "A7",
            evidence: SpanEvidence(before),
            replacement: new ProposedReplacement(before, after),
            rationale: Rationale,
            provenance: (KnowledgeBankProvenance)null!,
            resolvedKbValue: after);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void FromKnowledgeBank_ShouldThrow_WhenReplacementAfterIsEmpty()
    {
        const string before = "Brinner för";

        var act = () => ProposedChange.FromKnowledgeBank(
            targetId: TargetId,
            kind: ProposedChangeKind.ClicheReplacement,
            category: RubricCategory.Content,
            criterionId: "A7",
            evidence: SpanEvidence(before),
            replacement: new ProposedReplacement(before, string.Empty),
            rationale: Rationale,
            provenance: KbProvenance(before),
            resolvedKbValue: string.Empty);

        act.ShouldThrow<ArgumentException>();
    }

    // ===============================================================
    // FromStructuralOp — happy path (verified transform + pure removal)
    // ===============================================================

    [Fact]
    public void FromStructuralOp_ShouldSucceed_WhenPureTransformOfBeforeEqualsAfter()
    {
        // A verified transform: pureTransform(Before) must equal After (the proposed text is
        // a mechanical transform of what the user wrote, not invented prose).
        Func<string, string> normalizeCase = s => s.ToLowerInvariant();
        const string before = "ARBETSLIVSERFARENHET";
        var after = normalizeCase(before);

        var change = ProposedChange.FromStructuralOp(
            targetId: "heading-0",
            kind: ProposedChangeKind.HeadingNormalization,
            category: RubricCategory.Structure,
            criterionId: "D6",
            evidence: SpanEvidence(before),
            replacement: new ProposedReplacement(before, after),
            operation: new StructuralOperation(StructuralTransformKind.NormalizeHeadingCase, Target: "heading-0"),
            rationale: "Standardiserad rubrik-versalisering",
            provenance: StructProvenance(StructuralTransformKind.NormalizeHeadingCase),
            pureTransform: normalizeCase);

        change.Operation.ShouldNotBeNull();
        change.Operation!.Kind.ShouldBe(StructuralTransformKind.NormalizeHeadingCase);
        change.Provenance.ShouldBeOfType<StructuralTransformProvenance>();
    }

    [Fact]
    public void FromStructuralOp_ShouldSucceed_WhenPureRemovalHasNullReplacementAndNullTransform()
    {
        // A pure removal (personnummer / GPA) carries no replacement text and no transform —
        // there is nothing to verify because nothing is rewritten, only removed.
        var change = ProposedChange.FromStructuralOp(
            targetId: TargetId,
            kind: ProposedChangeKind.PersonnummerStrip,
            category: RubricCategory.Structure,
            criterionId: "B4",
            evidence: new StructuralEvidence("1 personnummer hittat"),
            replacement: null,
            operation: new StructuralOperation(StructuralTransformKind.RemovePersonnummer, Target: TargetId),
            rationale: "Ta bort personnummer (GDPR)",
            provenance: StructProvenance(StructuralTransformKind.RemovePersonnummer),
            pureTransform: null);

        change.Replacement.ShouldBeNull();
        change.Operation!.Kind.ShouldBe(StructuralTransformKind.RemovePersonnummer);
    }

    // ===============================================================
    // FromStructuralOp — no-synthesis guards (the load-bearing tests)
    // ===============================================================

    [Fact]
    public void FromStructuralOp_ShouldThrow_WhenPureTransformOfBeforeDoesNotEqualAfter()
    {
        // If pureTransform(Before) != After the proposed "After" is not actually a transform
        // of what the user wrote — it is synthesised. Throws ArgumentException citing ADR 0074.
        Func<string, string> normalizeCase = s => s.ToLowerInvariant();
        const string before = "ARBETSLIVSERFARENHET";

        var act = () => ProposedChange.FromStructuralOp(
            targetId: "heading-0",
            kind: ProposedChangeKind.HeadingNormalization,
            category: RubricCategory.Structure,
            criterionId: "D6",
            evidence: SpanEvidence(before),
            replacement: new ProposedReplacement(before, "Helt annan rubriktext"),
            operation: new StructuralOperation(StructuralTransformKind.NormalizeHeadingCase, Target: "heading-0"),
            rationale: "Standardiserad rubrik-versalisering",
            provenance: StructProvenance(StructuralTransformKind.NormalizeHeadingCase),
            pureTransform: normalizeCase);

        var ex = act.ShouldThrow<ArgumentException>();
        ex.Message.ShouldContain("0074", Case.Sensitive,
            "Felmeddelandet ska referera no-synthesis/ADR 0074-intentionen.");
    }

    [Fact]
    public void FromStructuralOp_ShouldThrow_WhenOperationKindDiffersFromProvenanceTransform()
    {
        // The operation kind and the provenance transform must agree — a mismatch would
        // misreport which structural transform produced the change.
        var act = () => ProposedChange.FromStructuralOp(
            targetId: TargetId,
            kind: ProposedChangeKind.PersonnummerStrip,
            category: RubricCategory.Structure,
            criterionId: "B4",
            evidence: new StructuralEvidence("1 personnummer hittat"),
            replacement: null,
            operation: new StructuralOperation(StructuralTransformKind.RemoveGpa, Target: TargetId),
            rationale: "Ta bort personnummer (GDPR)",
            provenance: StructProvenance(StructuralTransformKind.RemovePersonnummer),
            pureTransform: null);

        act.ShouldThrow<ArgumentException>();
    }

    // ===============================================================
    // ForRedaction — the privileged, guard-free redaction-only construction path (Fas 4 STEG B-2;
    // CTO D3, docs/reviews/2026-06-17-f4-improvement-evidence-redaction-cto.md). Takes an
    // already-validated change + masked copies of its evidence/replacement, carries all metadata
    // verbatim, and deliberately SKIPS the no-synthesis guards (re-validating masked strings is
    // meaningless — and impossible for a structural After).
    // ===============================================================

    private const string Pnr = "811218-9876";
    private const string Mask = "******-****";

    private static ProposedChange OriginalKbChange()
    {
        const string before = "Brinner för";
        const string after = "Beskriv ett konkret projekt eller initiativ.";
        return ProposedChange.FromKnowledgeBank(
            targetId: TargetId,
            kind: ProposedChangeKind.ClicheReplacement,
            category: RubricCategory.Content,
            criterionId: "A7",
            evidence: SpanEvidence(before),
            replacement: new ProposedReplacement(before, after),
            rationale: Rationale,
            provenance: KbProvenance(before),
            resolvedKbValue: after);
    }

    [Fact]
    public void ForRedaction_ShouldCarryAllMetadataVerbatim_AndSwapOnlyEvidenceAndReplacement()
    {
        var original = OriginalKbChange();
        var redactedEvidence = SpanEvidence("maskat citat");
        var redactedReplacement = new ProposedReplacement("maskat citat", "ersättning");

        var redacted = ProposedChange.ForRedaction(original, redactedEvidence, redactedReplacement);

        // Provenance + metadata are reference/value-identical to the original (never re-minted).
        redacted.Provenance.ShouldBeSameAs(original.Provenance);
        redacted.TargetId.ShouldBe(original.TargetId);
        redacted.Kind.ShouldBe(original.Kind);
        redacted.Category.ShouldBe(original.Category);
        redacted.CriterionId.ShouldBe(original.CriterionId);
        redacted.Operation.ShouldBeSameAs(original.Operation);
        redacted.Rationale.ShouldBe(original.Rationale);

        // Only evidence + replacement are swapped — to the exact masked copies passed in.
        redacted.Evidence.ShouldBeSameAs(redactedEvidence);
        redacted.Replacement.ShouldBeSameAs(redactedReplacement);
    }

    [Fact]
    public void ForRedaction_ShouldAcceptANullReplacement_ForAPureRemoval()
    {
        var original = ProposedChange.FromStructuralOp(
            targetId: TargetId,
            kind: ProposedChangeKind.PersonnummerStrip,
            category: RubricCategory.Structure,
            criterionId: "B4",
            evidence: new StructuralEvidence("1 personnummer hittat"),
            replacement: null,
            operation: new StructuralOperation(StructuralTransformKind.RemovePersonnummer, Target: TargetId),
            rationale: "Ta bort personnummer (GDPR)",
            provenance: StructProvenance(StructuralTransformKind.RemovePersonnummer),
            pureTransform: null);

        var redacted = ProposedChange.ForRedaction(
            original, new StructuralEvidence("1 personnummer hittat"), redactedReplacement: null);

        redacted.Replacement.ShouldBeNull();
        redacted.Operation!.Kind.ShouldBe(StructuralTransformKind.RemovePersonnummer);
    }

    [Fact]
    public void ForRedaction_ShouldNotRunSynthesisGuards_WhenAfterNoLongerEqualsTheKbValue()
    {
        // The whole point: redaction must be able to produce an After that the FromKnowledgeBank
        // guard would REJECT (e.g. a masked structural After, where pureTransform(maskedBefore) !=
        // maskedAfter). ForRedaction bypasses the guards because the original already passed them.
        var original = OriginalKbChange();
        var afterThatWouldFailTheKbGuard = new ProposedReplacement("maskat", "en helt annan sträng");

        var act = () => ProposedChange.ForRedaction(
            original, SpanEvidence("maskat"), afterThatWouldFailTheKbGuard);

        act.ShouldNotThrow();
        act().Replacement!.After.ShouldBe("en helt annan sträng");
    }

    [Fact]
    public void ForRedaction_ShouldProduceAPiiFreeChange_WhenGivenMaskedEvidenceAndReplacement()
    {
        // Anchor: the mask we use is the real scanner-masked form (anti-stale).
        PersonnummerScanner.Scan(Pnr).ShouldHaveSingleItem().Masked.ShouldBe(Mask);

        // An original that legitimately carried a pnr in its quote/before (e.g. a structural heading).
        var withPnr = $"Rubrik {Pnr}";
        var original = ProposedChange.FromStructuralOp(
            targetId: "heading-0",
            kind: ProposedChangeKind.HeadingNormalization,
            category: RubricCategory.Structure,
            criterionId: "D6",
            evidence: SpanEvidence(withPnr),
            replacement: new ProposedReplacement(withPnr, withPnr.ToLowerInvariant()),
            operation: new StructuralOperation(StructuralTransformKind.NormalizeHeadingCase, Target: "rubrik"),
            rationale: "Standardisera rubrik",
            provenance: StructProvenance(StructuralTransformKind.NormalizeHeadingCase),
            pureTransform: s => s.ToLowerInvariant());

        var maskedQuote = PersonnummerRedactor.Redact(withPnr);
        var redacted = ProposedChange.ForRedaction(
            original,
            new TextSpanEvidence(new TextSpan(0, 0, maskedQuote), Note: null),
            new ProposedReplacement(maskedQuote, PersonnummerRedactor.Redact(withPnr.ToLowerInvariant())));

        var quote = ((TextSpanEvidence)redacted.Evidence).Span.Quote;
        quote.ShouldContain(Mask);
        quote.ShouldNotContain(Pnr);
        redacted.Replacement!.Before.ShouldNotContain(Pnr);
        redacted.Replacement.After.ShouldNotContain(Pnr);
    }

    [Fact]
    public void ForRedaction_ShouldThrow_WhenOriginalIsNull()
    {
        var act = () => ProposedChange.ForRedaction(
            original: null!, SpanEvidence("x"), redactedReplacement: null);

        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void ForRedaction_ShouldThrow_WhenRedactedEvidenceIsNull()
    {
        var act = () => ProposedChange.ForRedaction(
            OriginalKbChange(), redactedEvidence: null!, redactedReplacement: null);

        act.ShouldThrow<ArgumentNullException>();
    }

    [Fact]
    public void FromStructuralOp_ShouldThrow_WhenProvenanceIsNotStructuralArm()
    {
        var act = () => ProposedChange.FromStructuralOp(
            targetId: TargetId,
            kind: ProposedChangeKind.PersonnummerStrip,
            category: RubricCategory.Structure,
            criterionId: "B4",
            evidence: new StructuralEvidence("1 personnummer hittat"),
            replacement: null,
            operation: new StructuralOperation(StructuralTransformKind.RemovePersonnummer, Target: TargetId),
            rationale: "Ta bort personnummer (GDPR)",
            provenance: (StructuralTransformProvenance)null!,
            pureTransform: null);

        act.ShouldThrow<ArgumentException>();
    }
}
