namespace Jobbliggaren.Domain.JobAds;

/// <summary>
/// #874 — the deterministic keyword/skill extraction (F4-4, ADR 0071/0074 Path C — NO AI/LLM),
/// supplied to the ingest transitions (<see cref="JobAd.Import"/> / <see cref="JobAd.UpdateFromSource"/>)
/// as a REQUIRED, caller-provided pure function. It is the <see cref="JobAdFacets"/> guarantee applied
/// to the one derived value that could not be pre-computed: a text write that forgets to refresh
/// <see cref="JobAd.ExtractedTerms"/> must not be expressible.
///
/// <para>
/// <b>Why a delegate and not the extractor port.</b> <c>ExtractedTerms</c> derives from the POST-SCRUB
/// <see cref="JobAd.Title"/> + <see cref="JobAd.Description"/> (the #842 Tier A invariant — the terms must
/// derive from clean text, never from body text that still carries a recruiter's contact span). That
/// scrubbed text exists only AFTER the transition rewrites its own fields inside
/// <see cref="JobAd.SetSourcePayload"/>, so — unlike the facets, which the ACL parses from the payload
/// BEFORE the transition — the value cannot be handed in as a plain argument. The aggregate must call
/// back out for it. But the real extractor (<c>IJobAdKeywordExtractor</c>) is an Application port that
/// must not enter Domain (CLAUDE.md §2.1). This delegate is the boundary-safe shape: it references only
/// <see langword="string"/> and the Domain <see cref="ExtractedTerms"/>, so the PORT stays in Application
/// (which closes over the extractor and the structured requirements) and only the produced VALUE crosses
/// the boundary — the same rule <see cref="JobAdFacets"/> follows.
/// </para>
///
/// <para>
/// <b>Contract (the Application impl must honour it).</b> Pure and deterministic: no I/O, no external
/// call, reads only the public post-scrub ad text it is given, never throws, blank input →
/// <see cref="ExtractedTerms.Empty"/> (see <c>IJobAdKeywordExtractor</c>). The aggregate invokes it ONCE
/// per transition, synchronously, and discards it on return — it is a value, not a capability the
/// aggregate depends on. A domain test can pass <c>(_, _) =&gt; ExtractedTerms.Empty</c> with no
/// infrastructure (CLAUDE.md §2.4), which is exactly what proves it is not a smuggled port.
/// </para>
/// </summary>
/// <param name="scrubbedTitle">The aggregate's post-scrub <see cref="JobAd.Title"/>.</param>
/// <param name="scrubbedDescription">The aggregate's post-scrub <see cref="JobAd.Description"/>.</param>
public delegate ExtractedTerms JobAdTermExtraction(string scrubbedTitle, string scrubbedDescription);
