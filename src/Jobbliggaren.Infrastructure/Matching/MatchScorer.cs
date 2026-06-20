using Jobbliggaren.Application.Common.Abstractions.TextAnalysis;
using Jobbliggaren.Application.Common.Exceptions;
using Jobbliggaren.Application.Matching.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Infrastructure.Matching;

/// <summary>
/// Fas 4 STEG 5 (F4-5, ADR 0074 row U5a; senior-cto-advisor Decision 3/4/5 = V3-a /
/// V4-b / V5-a) — the deterministic "Fast mode" <see cref="IMatchScorer"/>. NO
/// AI/LLM (ADR 0071, CLAUDE.md §5). Scores ONE job ad against a caller-supplied
/// <see cref="CandidateMatchProfile"/> over four dimensions:
/// <list type="number">
/// <item>SSYK level-4 overlap — the ad's STORED <c>occupation_group_concept_id</c>
/// shadow vs the profile's confirmed ssyk-4 ids (set membership);</item>
/// <item>title similarity — stemmed lexeme overlap of the ad title vs the CV title
/// via <see cref="ITextAnalyzer"/> (F4-2 Snowball, <c>to_tsvector('swedish')</c>
/// parity);</item>
/// <item>region fit — the ad's <c>region_concept_id</c> shadow vs preferred ids;</item>
/// <item>employment-type fit — the ad's <c>employment_type_concept_id</c> shadow vs
/// preferred ids.</item>
/// </list>
/// All scoring maths (set intersection, lexeme overlap, NotAssessed rules, Ordinal
/// ordering) lives inline here (CTO Decision 4 = V4-b; the result types are BCL-only
/// in Application and Application has no <c>InternalsVisibleTo</c> for Infrastructure,
/// so a separate Application calculator would be invisible). Mirrors
/// <c>OccupationCodeDeriver</c>: <c>internal sealed</c>, reads via the shadow columns,
/// takes no <see cref="Microsoft.Extensions.Logging.ILogger"/> — the title is never
/// logged (CLAUDE.md §5 / BUILD §13). Reads no raw CV PII (the title is a plain
/// caller-supplied string, the F4-3 boundary; the personnummer guard +
/// field-encryption pipeline live at the F4-8 call-site that builds the profile).
/// <para>
/// <b>Explainable, category-primary (Goodhart guard, CLAUDE.md §5 / ADR 0074):</b>
/// every dimension surfaces matched + missing evidence and a verdict; there is no
/// aggregate total. A dimension whose CV-side input is empty, or whose ad-side value
/// is absent (NULL shadow / no lexemes), is <see cref="MatchDimensionVerdict.NotAssessed"/>
/// — never <see cref="MatchDimensionVerdict.NoMatch"/> (the honest "not assessed v1"
/// state). Scoped (touches <see cref="AppDbContext"/>), unlike the singleton-cached
/// deriver.
/// </para>
/// </summary>
internal sealed class MatchScorer(AppDbContext db, ITextAnalyzer analyzer) : IMatchScorer
{
    // Shadow-property names (EF.Property keys) — the CLR-side names EF maps to the
    // STORED generated columns. Parity JobAdSearchQuery.ShadowColumn; the column
    // names themselves are an Infrastructure secret that never leaks to Application.
    private const string OccupationGroupColumn = "OccupationGroupConceptId";
    private const string RegionColumn = "RegionConceptId";
    private const string EmploymentTypeColumn = "EmploymentTypeConceptId";

    // Shared empty result for the no-ids batch fast-path (no allocation per call).
    private static readonly IReadOnlyDictionary<JobAdId, MatchScore> EmptyScores =
        new Dictionary<JobAdId, MatchScore>();

    private static readonly IReadOnlyDictionary<JobAdId, FullMatchScore> EmptyFullScores =
        new Dictionary<JobAdId, FullMatchScore>();

    public async ValueTask<MatchScore> ScoreAsync(
        JobAdId jobAdId, CandidateMatchProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // Project only the title + the three STORED shadow columns (no wide row,
        // no raw_payload). EF.Property reads the shadows (Npgsql-bound — stays in
        // Infrastructure, ADR 0062). AsNoTracking: read-only. The HasQueryFilter
        // (DeletedAt == null) excludes soft-deleted ads → they read as not-found.
        var ad = await db.JobAds
            .AsNoTracking()
            .Where(j => j.Id == jobAdId)
            .Select(j => new AdShadowRow(
                j.Title,
                EF.Property<string?>(j, OccupationGroupColumn),
                EF.Property<string?>(j, RegionColumn),
                EF.Property<string?>(j, EmploymentTypeColumn)))
            .FirstOrDefaultAsync(cancellationToken);

        if (ad is null)
        {
            throw new NotFoundException($"JobAd {jobAdId.Value} hittades inte.");
        }

        return new MatchScore(
            SsykOverlap: ScoreMembership(profile.SsykGroupConceptIds, ad.OccupationGroupConceptId),
            TitleSimilarity: ScoreTitle(profile.Title, ad.Title),
            RegionFit: ScoreMembership(profile.PreferredRegionConceptIds, ad.RegionConceptId),
            EmploymentFit: ScoreMembership(profile.PreferredEmploymentTypeConceptIds, ad.EmploymentTypeConceptId));
    }

    // Fas 4 STEG 13 (F4-13, ADR 0076 Decision 5; senior-cto-advisor 2026-06-19
    // Decision A=A1) — the zero-N+1 batch form of ScoreAsync (the page-scoped match-tag
    // overlay, parity isSaved/isApplied per ADR 0063). It loads ALL requested ads' Fast
    // shadow rows in ONE round-trip, then runs the SAME four dim-helpers in-memory per
    // row (so each MatchScore equals ScoreAsync for that ad + profile — the regression
    // contract). NO AI/LLM.
    public async ValueTask<IReadOnlyDictionary<JobAdId, MatchScore>> ScoreBatchAsync(
        IReadOnlyList<JobAdId> jobAdIds, CandidateMatchProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobAdIds);
        ArgumentNullException.ThrowIfNull(profile);

        if (jobAdIds.Count == 0)
        {
            return EmptyScores;
        }

        var ids = jobAdIds.Select(id => id.Value).Distinct().ToArray();

        // ONE round-trip, filtered by id IN (...) via parameterized `= ANY`. EF Core 10 +
        // Npgsql cannot translate Contains() over the strongly-typed JobAdId key (memory
        // ef_strongly_typed_vo_contains — both `List<JobAdId>.Contains(j.Id)` AND the
        // post-Select `.Value` form fail at runtime, verified in CI). job_ads is unbounded,
        // so the status-batch "load-all-for-seeker-then-client-filter" escape does not
        // apply. FromSql parameterizes the Guid[] (`= ANY(@p)`, injection-safe, NOT
        // concatenation — CLAUDE.md §5), composes with the global soft-delete query filter
        // (DeletedAt == null → soft-deleted ads are absent ⇒ no tag) and the EF.Property
        // shadow projection (stays in Infrastructure, ADR 0062). The Testcontainers
        // integration test is the oracle (InMemory hides the translation — same memory).
        var rows = await db.JobAds
            .FromSql($"SELECT * FROM job_ads WHERE id = ANY({ids})")
            .AsNoTracking()
            .Select(j => new AdBatchShadowRow(
                j.Id,
                j.Title,
                EF.Property<string?>(j, OccupationGroupColumn),
                EF.Property<string?>(j, RegionColumn),
                EF.Property<string?>(j, EmploymentTypeColumn)))
            .ToListAsync(cancellationToken);

        // Hoist the CV-title lexeme computation OUT of the per-ad loop (CTO Decision E):
        // the profile — hence the CV title — is constant across the batch. For F4-13's
        // preference profile Title is always empty ⇒ cvTitleLexemes is empty ⇒ every title
        // dimension reads NotAssessed; the hoist keeps the port general (a future
        // non-empty-title caller is not re-stemmed per ad).
        var cvTitleLexemes = TryCvTitleLexemes(profile.Title);

        var result = new Dictionary<JobAdId, MatchScore>(rows.Count);
        foreach (var ad in rows)
        {
            result[ad.Id] = new MatchScore(
                SsykOverlap: ScoreMembership(profile.SsykGroupConceptIds, ad.OccupationGroupConceptId),
                TitleSimilarity: ScoreTitle(cvTitleLexemes, ad.Title),
                RegionFit: ScoreMembership(profile.PreferredRegionConceptIds, ad.RegionConceptId),
                EmploymentFit: ScoreMembership(profile.PreferredEmploymentTypeConceptIds, ad.EmploymentTypeConceptId));
        }

        return result;
    }

    // Fas 4 STEG 6 (F4-6, ADR 0074 row U5b; senior-cto-advisor Decision A=A2 /
    // B=B2 / C=C1 / D=DD-shape-1+DD-verdict-A / E=DE-combine-2(skill-only)+DE-display-1
    // / F=F1) — the FULL match scorer. Built ON TOP of the Fast vertical: it loads
    // the ad's title + the three STORED shadow columns + the extracted_terms VO in
    // ONE round-trip (CTO C1 — single-ad scoring computes the overlap in-memory on
    // the loaded VO; the extracted_lexemes GIN serves the DEFERRED multi-ad search,
    // not this), builds the embedded Fast MatchScore via the SAME dim-1..4 helpers
    // (so result.Fast equals ScoreAsync for the same ad + Fast profile), then adds
    // three concept-id-coverage dimensions read from the VO:
    //   SkillOverlap:       terms where Kind==Skill          vs profile.CvSkillConceptIds.
    //   MustHaveCoverage:   terms where Kind==Requirement && Source==MustHave.
    //   NiceToHaveCoverage: terms where Kind==Requirement && Source==NiceToHave (bonus).
    // Each verdict derives from SET EMPTINESS only (parity ScoreTitle — no
    // ratio/Jaccard threshold, CLAUDE.md §5); NotAssessed when the CV has no skill
    // ids OR the ad has no terms of that kind/source (NULL/empty VO) — never NoMatch
    // on absence. Matched/Missing are ALWAYS surfaced (ADR 0074 gate), as Display
    // labels (DE-display-1, not raw concept-ids) Ordinal-sorted (deterministic).
    // must_have is the binding requirement signal but it is just its own dimension's
    // verdict — there is no opaque total it could gate (Goodhart guard, CTO D0).
    // NotFoundException if the ad does not exist (parity ScoreAsync).
    public async ValueTask<FullMatchScore> ScoreFullAsync(
        JobAdId jobAdId, FullCandidateMatchProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // One round-trip: the title + three shadows (the Fast inputs) + the
        // extracted_terms VO (the Full inputs). The VO is materialized via its jsonb
        // ValueConverter (parity GetJobAdExtractedTermsQueryHandler); EF.Property
        // reads the Npgsql-bound shadows (stays in Infrastructure, ADR 0062).
        var ad = await db.JobAds
            .AsNoTracking()
            .Where(j => j.Id == jobAdId)
            .Select(j => new AdFullRow(
                j.Title,
                EF.Property<string?>(j, OccupationGroupColumn),
                EF.Property<string?>(j, RegionColumn),
                EF.Property<string?>(j, EmploymentTypeColumn),
                j.ExtractedTerms))
            .FirstOrDefaultAsync(cancellationToken);

        if (ad is null)
        {
            throw new NotFoundException($"JobAd {jobAdId.Value} hittades inte.");
        }

        // Embedded Fast — the SAME four helpers/inputs as ScoreAsync, so the result
        // is identical (the regression contract).
        var fast = profile.Fast;
        var fastScore = new MatchScore(
            SsykOverlap: ScoreMembership(fast.SsykGroupConceptIds, ad.OccupationGroupConceptId),
            TitleSimilarity: ScoreTitle(fast.Title, ad.Title),
            RegionFit: ScoreMembership(fast.PreferredRegionConceptIds, ad.RegionConceptId),
            EmploymentFit: ScoreMembership(fast.PreferredEmploymentTypeConceptIds, ad.EmploymentTypeConceptId));

        var terms = (ad.ExtractedTerms ?? ExtractedTerms.Empty).Terms;
        var cvSkills = profile.CvSkillConceptIds.ToHashSet(StringComparer.Ordinal);

        return new FullMatchScore(
            Fast: fastScore,
            SkillOverlap: ScoreConceptCoverage(
                terms.Where(t => t.Kind == ExtractedTermKind.Skill), cvSkills),
            MustHaveCoverage: ScoreConceptCoverage(
                terms.Where(t => t.Kind == ExtractedTermKind.Requirement
                    && t.Source == ExtractedTermSource.MustHave), cvSkills),
            NiceToHaveCoverage: ScoreConceptCoverage(
                terms.Where(t => t.Kind == ExtractedTermKind.Requirement
                    && t.Source == ExtractedTermSource.NiceToHave), cvSkills));
    }

    // Fas 4 STEG 15 (F4-15, ADR 0076 Decision 6) — the zero-N+1 batch form of
    // ScoreFullAsync (the page-scoped match-tag overlay upgraded to Full). It loads
    // ALL requested ads' Fast shadows + the extracted_terms VO in ONE round-trip
    // (the SAME `FromSql = ANY` shape as ScoreBatchAsync, extended with the VO
    // projection of ScoreFullAsync), then runs the SAME Fast + concept-coverage
    // helpers in-memory per row (so each FullMatchScore equals ScoreFullAsync for
    // that ad + profile — the regression contract). NO AI/LLM.
    public async ValueTask<IReadOnlyDictionary<JobAdId, FullMatchScore>> ScoreFullBatchAsync(
        IReadOnlyList<JobAdId> jobAdIds, FullCandidateMatchProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobAdIds);
        ArgumentNullException.ThrowIfNull(profile);

        if (jobAdIds.Count == 0)
        {
            return EmptyFullScores;
        }

        var ids = jobAdIds.Select(id => id.Value).Distinct().ToArray();

        // ONE round-trip via parameterized `= ANY` (parity ScoreBatchAsync — Contains
        // over the strongly-typed JobAdId key does not translate, ef_strongly_typed_vo_
        // contains). The extracted_terms VO materializes via its jsonb ValueConverter
        // exactly as in ScoreFullAsync's .Select; the global soft-delete filter composes
        // (soft-deleted ads absent ⇒ no entry). Testcontainers is the oracle.
        var rows = await db.JobAds
            .FromSql($"SELECT * FROM job_ads WHERE id = ANY({ids})")
            .AsNoTracking()
            .Select(j => new AdFullBatchRow(
                j.Id,
                j.Title,
                EF.Property<string?>(j, OccupationGroupColumn),
                EF.Property<string?>(j, RegionColumn),
                EF.Property<string?>(j, EmploymentTypeColumn),
                j.ExtractedTerms))
            .ToListAsync(cancellationToken);

        // Hoist the constant CV-side inputs out of the per-ad loop (parity the Fast
        // batch's cvTitleLexemes hoist): the title lexemes and the skill-id set are
        // the same for every ad in the batch.
        var fast = profile.Fast;
        var cvTitleLexemes = TryCvTitleLexemes(fast.Title);
        var cvSkills = profile.CvSkillConceptIds.ToHashSet(StringComparer.Ordinal);

        var result = new Dictionary<JobAdId, FullMatchScore>(rows.Count);
        foreach (var ad in rows)
        {
            var fastScore = new MatchScore(
                SsykOverlap: ScoreMembership(fast.SsykGroupConceptIds, ad.OccupationGroupConceptId),
                TitleSimilarity: ScoreTitle(cvTitleLexemes, ad.Title),
                RegionFit: ScoreMembership(fast.PreferredRegionConceptIds, ad.RegionConceptId),
                EmploymentFit: ScoreMembership(fast.PreferredEmploymentTypeConceptIds, ad.EmploymentTypeConceptId));

            var terms = (ad.ExtractedTerms ?? ExtractedTerms.Empty).Terms;
            result[ad.Id] = new FullMatchScore(
                Fast: fastScore,
                SkillOverlap: ScoreConceptCoverage(
                    terms.Where(t => t.Kind == ExtractedTermKind.Skill), cvSkills),
                MustHaveCoverage: ScoreConceptCoverage(
                    terms.Where(t => t.Kind == ExtractedTermKind.Requirement
                        && t.Source == ExtractedTermSource.MustHave), cvSkills),
                NiceToHaveCoverage: ScoreConceptCoverage(
                    terms.Where(t => t.Kind == ExtractedTermKind.Requirement
                        && t.Source == ExtractedTermSource.NiceToHave), cvSkills));
        }

        return result;
    }

    // Concept-id coverage of one ad-side term partition (Skill / must_have /
    // nice_to_have) against the CV's skill concept-ids. The overlap key is the
    // concept-id (Lexeme == ConceptId for Skill/Requirement terms); the surfaced
    // evidence is the human Display label (DE-display-1). The two "absent" cases are
    // now DISTINCT (ADR 0076 amendment 2026-06-20, requirement-aware grade):
    //   - CV side empty (no CV / no resolved skills) → NotAssessed ("can't assess").
    //   - CV present but THIS partition has no ad terms → Vacuous ("we looked; the ad
    //     specifies none of this kind"). This per-partition distinction lets a
    //     no-must-have ad be gate-OPEN for the grade while a no-CV user is gate-CLOSED.
    // NoMatch stays reserved for "data present on both sides, disjoint" (parity
    // ScoreMembership rule 1). Verdict from set emptiness only (no threshold).
    private static MatchDimension ScoreConceptCoverage(
        IEnumerable<ExtractedTerm> adTerms, HashSet<string> cvSkillConceptIds)
    {
        // Distinct concept-id → Display. Terms arrive pre-sorted by ExtractedTerms.From
        // (deterministic), so the first Display kept per concept-id is stable.
        var byConcept = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var term in adTerms)
        {
            byConcept.TryAdd(term.ConceptId!, term.Display);
        }

        // CV side empty → cannot assess (no CV). Checked FIRST so "no CV" always reads
        // NotAssessed regardless of the ad partition.
        if (cvSkillConceptIds.Count == 0)
        {
            return NotAssessed();
        }

        // CV present but the ad has no terms of this partition → Vacuous, NOT NotAssessed.
        if (byConcept.Count == 0)
        {
            return Vacuous();
        }

        // Partition the ad's concept-ids by CV coverage; surface Display labels
        // (Ordinal-sorted). Missing = "what the ad wants that the CV lacks".
        var matched = byConcept
            .Where(kv => cvSkillConceptIds.Contains(kv.Key))
            .Select(kv => kv.Value)
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();
        var missing = byConcept
            .Where(kv => !cvSkillConceptIds.Contains(kv.Key))
            .Select(kv => kv.Value)
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();

        var verdict = matched.Count == 0
            ? MatchDimensionVerdict.NoMatch
            : missing.Count == 0
                ? MatchDimensionVerdict.Match
                : MatchDimensionVerdict.Partial;

        return new MatchDimension(verdict, matched, missing);
    }

    // SSYK / region / employment: the CV holds a list, the ad holds a single value.
    // NotAssessed if either side is absent (empty CV list OR NULL ad shadow) — rule 1
    // (CTO Decision 3): NoMatch is reserved for "data present on both sides, disjoint".
    // Binary set membership → never Partial. Matched/Missing carry the cited evidence.
    private static MatchDimension ScoreMembership(IReadOnlyList<string> cvPreferred, string? adValue)
    {
        if (cvPreferred.Count == 0 || string.IsNullOrEmpty(adValue))
        {
            return NotAssessed();
        }

        return cvPreferred.Contains(adValue, StringComparer.Ordinal)
            ? new MatchDimension(MatchDimensionVerdict.Match, [adValue], [])
            : new MatchDimension(MatchDimensionVerdict.NoMatch, [], [adValue]);
    }

    // Title similarity via stemmed lexeme overlap (F4-2). Matched = ad ∩ cv lexemes;
    // Missing = ad \ cv (the civic-useful direction: "what the ad wants that you
    // lack"). Verdict derives from set emptiness ONLY — no hardcoded ratio/Jaccard
    // threshold (CLAUDE.md §5). Match = all ad lexemes covered; Partial = overlap +
    // leftover; NoMatch = disjoint (both non-empty); NotAssessed = either side has no
    // lexemes (blank/all-stopword title) or a non-Swedish analysis request.
    // The single-ad path stems the CV title inline; the batch path (ScoreBatchAsync)
    // hoists it once. Both share the precomputed-CV overload below.
    private MatchDimension ScoreTitle(string cvTitle, string adTitle)
        => ScoreTitle(TryCvTitleLexemes(cvTitle), adTitle);

    // Stems the CV title once into its lexeme set (Swedish). Returns null when the
    // analysis is unsupported — the forward-compat dormant guard (CTO re-ruling
    // 2026-06-15): F4-5/F4-13 always request Swedish (the Platsbanken corpus is Swedish;
    // the preference profile has no language signal and language detection is F4-8/9,
    // ADR 0074 amendment) so this does not fire today. When the language-aware matcher
    // contract arrives (ParsedResume carries a detected language), a non-Swedish request
    // degrades every title dimension to NotAssessed instead of crashing. Caught NARROWLY
    // around the analyzer call only (CLAUDE.md §5 — no catch-all).
    private HashSet<string>? TryCvTitleLexemes(string cvTitle)
    {
        try
        {
            return analyzer.ToLexemes(cvTitle, TextLanguage.Swedish).ToHashSet(StringComparer.Ordinal);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    // Title similarity via stemmed lexeme overlap (F4-2), given the precomputed CV
    // lexeme set (null ⇒ unsupported CV-side ⇒ NotAssessed). Matched = ad ∩ cv lexemes;
    // Missing = ad \ cv (the civic-useful direction: "what the ad wants that you lack").
    // Verdict derives from set emptiness ONLY — no hardcoded ratio/Jaccard threshold
    // (CLAUDE.md §5). Match = all ad lexemes covered; Partial = overlap + leftover;
    // NoMatch = disjoint (both non-empty); NotAssessed = either side has no lexemes
    // (blank/all-stopword title) or a non-Swedish ad-title analysis request (caught
    // NARROWLY around the analyzer call only).
    private MatchDimension ScoreTitle(HashSet<string>? cvLexemes, string adTitle)
    {
        if (cvLexemes is null)
        {
            return NotAssessed();
        }

        HashSet<string> ad;
        try
        {
            ad = analyzer.ToLexemes(adTitle, TextLanguage.Swedish).ToHashSet(StringComparer.Ordinal);
        }
        catch (NotSupportedException)
        {
            return NotAssessed();
        }

        if (cvLexemes.Count == 0 || ad.Count == 0)
        {
            return NotAssessed();
        }

        var matched = ad.Where(cvLexemes.Contains).OrderBy(l => l, StringComparer.Ordinal).ToList();
        var missing = ad.Where(l => !cvLexemes.Contains(l)).OrderBy(l => l, StringComparer.Ordinal).ToList();

        var verdict = matched.Count == 0
            ? MatchDimensionVerdict.NoMatch
            : missing.Count == 0
                ? MatchDimensionVerdict.Match
                : MatchDimensionVerdict.Partial;

        return new MatchDimension(verdict, matched, missing);
    }

    private static MatchDimension NotAssessed() =>
        new(MatchDimensionVerdict.NotAssessed, [], []);

    // CV present, but this concept-coverage partition has no ad terms ("nothing
    // required, and we looked"). Distinct from NotAssessed — see MatchDimensionVerdict.
    private static MatchDimension Vacuous() =>
        new(MatchDimensionVerdict.Vacuous, [], []);

    // The minimal row the scorer needs — title + the three STORED shadow values.
    // A constructor-projected type (EF maps the ctor); shadow values are nullable
    // (NULL ⇒ the ad has no value on that dimension ⇒ NotAssessed).
    private sealed record AdShadowRow(
        string Title,
        string? OccupationGroupConceptId,
        string? RegionConceptId,
        string? EmploymentTypeConceptId);

    // The batch row (F4-13) — AdShadowRow plus the JobAdId key, so ScoreBatchAsync can
    // key the result dictionary. j.Id materializes via its value converter (parity the
    // single-ad equality filter); shadow values are nullable (NULL ⇒ NotAssessed).
    private sealed record AdBatchShadowRow(
        JobAdId Id,
        string Title,
        string? OccupationGroupConceptId,
        string? RegionConceptId,
        string? EmploymentTypeConceptId);

    // The Full row (F4-6) — the Fast inputs plus the extracted_terms VO (materialized
    // via its jsonb ValueConverter). Constructor-projected; ExtractedTerms is nullable
    // (NULL ⇒ never extracted ⇒ the three new dims read NotAssessed).
    private sealed record AdFullRow(
        string Title,
        string? OccupationGroupConceptId,
        string? RegionConceptId,
        string? EmploymentTypeConceptId,
        ExtractedTerms? ExtractedTerms);

    // The Full batch row (F4-15) — AdFullRow plus the JobAdId key, so ScoreFullBatchAsync
    // can key the result dictionary. j.Id + the extracted_terms VO materialize via their
    // value converters (parity ScoreBatchAsync + ScoreFullAsync).
    private sealed record AdFullBatchRow(
        JobAdId Id,
        string Title,
        string? OccupationGroupConceptId,
        string? RegionConceptId,
        string? EmploymentTypeConceptId,
        ExtractedTerms? ExtractedTerms);
}
