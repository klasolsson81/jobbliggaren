namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Input to <see cref="IJobAdKeywordExtractor"/>: the public job-ad text the
/// deterministic extraction reads. Carries no <c>raw_payload</c> and no PII — the
/// extractor reads only the already-public headline + free-text description
/// (ADR 0074 invariant 3 does not bite). Application-layer transport record
/// (CLAUDE.md §3.3); the extractor builds the Domain <c>ExtractedTerms</c> from it.
/// <para>
/// v1 (Path C) extracts from <see cref="Title"/> + <see cref="Description"/> only.
/// Occupation-context enrichment (occupation→skill relations) and employer
/// requirement extraction (must-have/nice-to-have, F4-4b) extend this input
/// additively.
/// </para>
/// </summary>
public sealed record JobAdExtractionInput(string Title, string Description);
