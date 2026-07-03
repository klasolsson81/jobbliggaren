namespace Jobbliggaren.Application.KnowledgeBank.Abstractions;

/// <summary>
/// What a lexicon entry IS, so the determinism can route it to exactly one criterion
/// (one phrase → one verdict, no double-punishment — #490). A <see cref="Cliche"/> is an
/// empty buzzword phrase the anti-cliché rule (A7) flags; a <see cref="SoftSkill"/> is a
/// bare personality adjective the "soft skills underbyggda" rule (A9) flags when it lacks a
/// nearby concrete example. Pre-#490 both rules reused the whole lexicon via naive
/// <c>Contains</c>, so one phrase produced two simultaneous verdicts.
/// </summary>
public enum ClicheKind
{
    /// <summary>An empty buzzword phrase ("Brinner för", "Tänker utanför boxen") — A7's domain.</summary>
    Cliche,

    /// <summary>A bare personality adjective ("Social", "Noggrann") — A9's domain (backed only
    /// when a concrete example sits near it).</summary>
    SoftSkill,
}

/// <summary>
/// One curated Swedish CV lexicon entry (research §6.1) with its diagnosis and constructive
/// guidance. Versioned DATA (CLAUDE.md §5: "cliché lists ... versioned data/config per the
/// knowledge bank, not inline strings"). Every entry carries <see cref="Why"/> (so the
/// determinism can cite why it flagged) and advisory <see cref="Guidance"/> (intended for
/// display as an example the job-seeker adapts, never applied verbatim; no consumer renders it
/// yet). The v2 split (#495): <see cref="Guidance"/> is ADVISORY —
/// it may embed illustrative numbers or meta-instructions and must NEVER be applied as a
/// literal replacement — while the OPTIONAL <see cref="DropInReplacement"/> is a genuine
/// same-meaning literal the propose step (F4-10) may offer verbatim. When
/// <see cref="DropInReplacement"/> is null the engine flags but proposes no rewrite (no
/// synthesis — ADR 0071/0074). <see cref="Kind"/> routes the phrase to exactly one criterion.
/// </summary>
public sealed record ClicheEntry(
    string Phrase,
    ClicheKind Kind,
    string Why,
    string Guidance,
    string? DropInReplacement);

/// <summary>
/// The versioned CV lexicon (F4-7, research §6.1) — clichés + soft-skill adjectives
/// discriminated by <see cref="ClicheEntry.Kind"/>. Plain-string version
/// (senior-cto-advisor DQ3 — cliché/verb evolve at a different cadence than the rubric
/// and keep parity with the existing <c>TaxonomyVersion</c> string convention).
/// </summary>
public sealed record ClicheList(string Version, IReadOnlyList<ClicheEntry> Entries);
