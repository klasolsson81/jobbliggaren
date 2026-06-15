namespace Jobbliggaren.Application.KnowledgeBank.Abstractions;

/// <summary>
/// One curated Swedish CV cliché (research §6.1) with the diagnosis and a constructive
/// alternative. Versioned DATA (CLAUDE.md §5: "cliché lists ... versioned data/config
/// per the knowledge bank, not inline strings"). Every entry carries both
/// <see cref="Why"/> (so the determinism can cite why it flagged) and
/// <see cref="BetterAlternative"/> (so it can propose, never just flag) — consumed by
/// F4-9 (A7 anti-cliché) and F4-10 (propose-and-approve rewrite suggestions).
/// </summary>
public sealed record ClicheEntry(string Phrase, string Why, string BetterAlternative);

/// <summary>
/// The versioned cliché lexicon (F4-7, research §6.1). Plain-string version
/// (senior-cto-advisor DQ3 — cliché/verb evolve at a different cadence than the rubric
/// and keep parity with the existing <c>TaxonomyVersion</c> string convention).
/// </summary>
public sealed record ClicheList(string Version, IReadOnlyList<ClicheEntry> Entries);
