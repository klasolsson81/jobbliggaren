using Jobbliggaren.Domain.JobAds;

namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Deterministic per-job-ad keyword/skill extractor (F4-4, ADR 0071/0074 Path C —
/// NO AI/LLM). Normalizes the ad's title + description via the local NLP tier
/// (<c>ITextAnalyzer.ToLexemes</c>, Snowball — <c>to_tsvector('swedish')</c>
/// parity, F4-2) and resolves salient terms against the committed JobTech
/// skill-taxonomy + synonym asset, producing the canonical
/// <see cref="ExtractedTerms"/> the matching engine (F4-6) consumes for
/// keyword/skill overlap. Every term cites its evidence (explainable by design,
/// CLAUDE.md §5).
/// <para>
/// Returns the Domain <see cref="ExtractedTerms"/> value object directly (it is
/// persisted aggregate state, not a read projection — dotnet-architect Variant A,
/// ADR 0075). Pure/deterministic: no DB write, no external call, reads only public
/// ad text (no CV-PII — ADR 0074 invariant 3 does not bite here), never logs the
/// ad text. Synchronous: the skill index is built once from an embedded resource
/// (no I/O per call). Blank/empty input → <see cref="ExtractedTerms.Empty"/>,
/// never throws.
/// </para>
/// </summary>
public interface IJobAdKeywordExtractor
{
    ExtractedTerms Extract(JobAdExtractionInput input);
}
