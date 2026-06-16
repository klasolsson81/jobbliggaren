using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;

namespace Jobbliggaren.Application.Resumes.Improvement.Abstractions;

/// <summary>
/// One propose-and-approve diff (Fas 4 STEG 10, F4-10; ADR 0074). NEVER auto-applied (the
/// engine proposes, the user approves); NEVER synthesised. A private constructor + two static
/// factories make the no-synthesis guard UNAVOIDABLE (parity
/// <see cref="CvCriterionVerdict.Assessed"/>): the <c>After</c> string can ONLY originate from
/// the knowledge bank (<see cref="FromKnowledgeBank"/>) or a verified pure transform of
/// <c>Before</c> (<see cref="FromStructuralOp"/>). Inconsistent provenance↔payload pairings are
/// unconstructable, so a hand-tweaked or invented replacement cannot exist (CLAUDE.md §5).
/// </summary>
public sealed record ProposedChange
{
    /// <summary>A deterministic, stable address for a future approve step (e.g. "cliche:0",
    /// "weakverb:1") — stable across reruns (Goodhart-free ordering).</summary>
    public string TargetId { get; }

    public ProposedChangeKind Kind { get; }

    public RubricCategory Category { get; }

    /// <summary>The F4-9 review criterion this change addresses (e.g. "A7"); null when no review
    /// was supplied (the engine still proposes — it can detect a cliché on its own).</summary>
    public string? CriterionId { get; }

    /// <summary>The cited grounding (reused verbatim from F4-9): <see cref="TextSpanEvidence"/>
    /// for present-text edits, <see cref="StructuralEvidence"/> for absence/structural ops.</summary>
    public CitedEvidence Evidence { get; }

    /// <summary>The before→after text edit; null for a pure structural removal.</summary>
    public ProposedReplacement? Replacement { get; }

    /// <summary>The structural operation; null for a pure text replacement.</summary>
    public StructuralOperation? Operation { get; }

    /// <summary>Why the change is proposed (from the KB <c>Why</c> field / the rule) — never
    /// synthesised prose about the user.</summary>
    public string Rationale { get; }

    public ChangeProvenance Provenance { get; }

    private ProposedChange(
        string targetId,
        ProposedChangeKind kind,
        RubricCategory category,
        string? criterionId,
        CitedEvidence evidence,
        ProposedReplacement? replacement,
        StructuralOperation? operation,
        string rationale,
        ChangeProvenance provenance)
    {
        TargetId = targetId;
        Kind = kind;
        Category = category;
        CriterionId = criterionId;
        Evidence = evidence;
        Replacement = replacement;
        Operation = operation;
        Rationale = rationale;
        Provenance = provenance;
    }

    /// <summary>
    /// A knowledge-bank-sourced text replacement. THROWS unless: <paramref name="provenance"/>
    /// is non-null; <paramref name="replacement"/> is non-null with a non-empty <c>After</c>;
    /// <c>After</c> EQUALS <paramref name="resolvedKbValue"/> (the value the engine resolved
    /// from the KB for <c>provenance.Key</c>) — a hand-tweaked <c>After</c> would be synthesised
    /// prose (ADR 0074 / CLAUDE.md §5); and, when <paramref name="evidence"/> is a
    /// <see cref="TextSpanEvidence"/>, <c>Before</c> EQUALS the cited span's <c>Quote</c>
    /// (proposing to replace a span the user did not write breaks the propose-and-approve
    /// contract). A KB replacement carries no <see cref="StructuralOperation"/>.
    /// </summary>
    public static ProposedChange FromKnowledgeBank(
        string targetId,
        ProposedChangeKind kind,
        RubricCategory category,
        string? criterionId,
        CitedEvidence evidence,
        ProposedReplacement replacement,
        string rationale,
        KnowledgeBankProvenance provenance,
        string resolvedKbValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(provenance);

        if (string.IsNullOrEmpty(replacement.After))
        {
            throw new ArgumentException(
                "A knowledge-bank replacement must carry a non-empty After value " +
                "(ADR 0074 no-synthesis: the proposal is a curated KB value).",
                nameof(replacement));
        }

        if (!string.Equals(replacement.After, resolvedKbValue, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"After must be EXACTLY the knowledge-bank value resolved for key " +
                $"'{provenance.Key}' — a hand-tweaked replacement is synthesised prose " +
                "(ADR 0074 / CLAUDE.md §5: the determinism diagnoses and structures, never invents).",
                nameof(replacement));
        }

        if (evidence is TextSpanEvidence span
            && !string.Equals(replacement.Before, span.Span.Quote, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Before must EQUAL the cited text-span Quote — proposing to replace a span the " +
                "user did not write breaks the propose-and-approve contract (ADR 0074).",
                nameof(replacement));
        }

        return new ProposedChange(
            targetId, kind, category, criterionId, evidence, replacement,
            operation: null, rationale, provenance);
    }

    /// <summary>
    /// A pure structural transform. THROWS unless: <paramref name="provenance"/> and
    /// <paramref name="operation"/> are non-null; <c>operation.Kind</c> EQUALS
    /// <c>provenance.Transform</c> (a mismatch would misreport which transform produced the
    /// change); and, when <paramref name="replacement"/> is non-null, a non-null
    /// <paramref name="pureTransform"/> reproduces its <c>After</c> from its <c>Before</c>
    /// EXACTLY — the "show your work" check that makes a fabricated <c>After</c> impossible
    /// (ADR 0074 / CLAUDE.md §5). A pure removal passes <paramref name="replacement"/> = null
    /// and <paramref name="pureTransform"/> = null (nothing is rewritten, only removed).
    /// </summary>
    public static ProposedChange FromStructuralOp(
        string targetId,
        ProposedChangeKind kind,
        RubricCategory category,
        string? criterionId,
        CitedEvidence evidence,
        ProposedReplacement? replacement,
        StructuralOperation operation,
        string rationale,
        StructuralTransformProvenance provenance,
        Func<string, string>? pureTransform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(operation);

        if (operation.Kind != provenance.Transform)
        {
            throw new ArgumentException(
                $"Operation.Kind ({operation.Kind}) must equal the provenance Transform " +
                $"({provenance.Transform}) — a mismatch misreports the structural transform.",
                nameof(operation));
        }

        if (replacement is not null)
        {
            if (pureTransform is null)
            {
                throw new ArgumentException(
                    "A structural replacement must supply the pure transform that produced it, " +
                    "so After can be verified as a transform of Before and not synthesised " +
                    "(ADR 0074 / CLAUDE.md §5).",
                    nameof(pureTransform));
            }

            if (!string.Equals(pureTransform(replacement.Before), replacement.After, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "After must equal pureTransform(Before) — a result that is not a mechanical " +
                    "transform of what the user wrote is synthesised prose (ADR 0074 / CLAUDE.md §5).",
                    nameof(replacement));
            }
        }

        return new ProposedChange(
            targetId, kind, category, criterionId, evidence, replacement,
            operation, rationale, provenance);
    }

    /// <summary>
    /// Builds a transmit-safe COPY of an ALREADY-VALIDATED change with personnummer redacted out of
    /// its cited evidence + replacement strings (Fas 4 STEG B-2 hardening; ADR 0074 Invariant 1;
    /// CLAUDE.md §5 — the personnummer guard is highest-priority). This is the ONLY non-synthesis
    /// construction path: it carries <c>TargetId</c>/<c>Kind</c>/<c>Category</c>/<c>CriterionId</c>/
    /// <c>Operation</c>/<c>Rationale</c>/<c>Provenance</c> VERBATIM from <paramref name="original"/>
    /// (it has no parameters for them, so it cannot invent any) and swaps in only the redacted
    /// <paramref name="redactedEvidence"/> + <paramref name="redactedReplacement"/>.
    ///
    /// <para>Redaction is NOT synthesis, so this path deliberately SKIPS the
    /// <see cref="FromKnowledgeBank"/>/<see cref="FromStructuralOp"/> guards (CTO D3,
    /// <c>docs/reviews/2026-06-17-f4-improvement-evidence-redaction-cto.md</c>): those guards
    /// (<c>After == resolvedKbValue</c> / <c>After == pureTransform(Before)</c>) already held at the
    /// original's truthful construction; re-validating MASKED strings is meaningless — and impossible
    /// for a structural <c>After</c>, since <c>pureTransform(maskedBefore) != maskedAfter</c>. The
    /// caller is the engine's single redaction choke point, which only ever passes masked copies of
    /// the original's own strings — it cannot mint new provenance.</para>
    /// </summary>
    public static ProposedChange ForRedaction(
        ProposedChange original,
        CitedEvidence redactedEvidence,
        ProposedReplacement? redactedReplacement)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(redactedEvidence);

        return new ProposedChange(
            original.TargetId,
            original.Kind,
            original.Category,
            original.CriterionId,
            redactedEvidence,
            redactedReplacement,
            original.Operation,
            original.Rationale,
            original.Provenance);
    }
}
