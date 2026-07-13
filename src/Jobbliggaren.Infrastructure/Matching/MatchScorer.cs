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
/// <item>location ("ort") fit — the ad's <c>region_concept_id</c> ∪
/// <c>municipality_concept_id</c> shadows vs the preferred region/municipality ids
/// (Spår 3, ADR 0076-amendment 2026-06-21; the verdict keeps the name <c>RegionFit</c>,
/// two granularities folded into one dimension — see <see cref="ScoreOrtUnion"/>);</item>
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

    // Spår 3 (ADR 0076-amendment 2026-06-21) — the finer location granularity that folds
    // into the SAME "ort" dimension (RegionFit) as the region shadow. STORED generated from
    // raw_payload->'workplace_address'->>'municipality_concept_id' (JobAdConfiguration).
    private const string MunicipalityColumn = "MunicipalityConceptId";

    // Shared empty result for the no-ids batch fast-path (no allocation per call).
    private static readonly IReadOnlyDictionary<JobAdId, MatchScore> EmptyScores =
        new Dictionary<JobAdId, MatchScore>();

    private static readonly IReadOnlyDictionary<JobAdId, FullScoredMatch> EmptyFullScores =
        new Dictionary<JobAdId, FullScoredMatch>();

    public async ValueTask<MatchScore> ScoreAsync(
        JobAdId jobAdId, CandidateMatchProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // Project only the title + the three facet columns (no wide row,
        // no raw_payload). EF.Property reads the shadows (Npgsql-bound — stays in
        // Infrastructure, ADR 0062). AsNoTracking: read-only.
        //
        // KNOWN GAP (#864) — THERE IS NO STATUS GATE HERE. This comment used to claim the
        // global soft-delete filter (DeletedAt == null) excluded retracted ads. It never did:
        // JobAd.DeletedAt had no writer, so the filter was vacuous, and #821 retired it. The
        // consequence is real and lives today: an ARCHIVED ad resolves and is scored. The
        // sibling paths DO gate (PerUserJobAdSearchQuery:307/:368) — this one does not.
        // Adding `.Where(j => j.Status == Active)` here is NOT the fix: ScoreAsync throws
        // NotFoundException below, so a gate here would make GetJobAdMatchDetail 404 archived
        // ads, straight against #805-3. Whose job the gate is (scorer / handler / endpoint) is
        // the open design question — see #864. Pinned by characterization tests.
        var ad = await db.JobAds
            .AsNoTracking()
            .Where(j => j.Id == jobAdId)
            .Select(j => new AdFacetRow(
                j.Title,
                EF.Property<string?>(j, OccupationGroupColumn),
                EF.Property<string?>(j, RegionColumn),
                EF.Property<string?>(j, EmploymentTypeColumn),
                EF.Property<string?>(j, MunicipalityColumn)))
            .FirstOrDefaultAsync(cancellationToken);

        if (ad is null)
        {
            throw new NotFoundException($"JobAd {jobAdId.Value} hittades inte.");
        }

        return new MatchScore(
            SsykOverlap: ScoreSsykMembership(
                profile.SsykGroupConceptIds, profile.RelatedSsykGroupConceptIds, ad.OccupationGroupConceptId),
            TitleSimilarity: ScoreTitle(profile.Title, ad.Title),
            RegionFit: ScoreOrtUnion(
                profile.PreferredRegionConceptIds, profile.PreferredMunicipalityConceptIds,
                profile.ContainmentRegionConceptIds,
                ad.RegionConceptId, ad.MunicipalityConceptId),
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
        // concatenation — CLAUDE.md §5) and composes with the EF.Property shadow projection
        // (stays in Infrastructure, ADR 0062). The Testcontainers integration test is the
        // oracle (InMemory hides the translation — same memory).
        //
        // KNOWN GAP (#864) — NO STATUS GATE, same as ScoreAsync. The old comment here claimed
        // "soft-deleted ads are absent ⇒ no tag"; that filter was vacuous and is retired (#821).
        // An ARCHIVED ad is loaded and tagged. This is the batch feeding the client-supplied-id
        // endpoint, so it is where the gap is actually exposed. Pinned by a characterization test.
        var rows = await db.JobAds
            .FromSql($"SELECT * FROM job_ads WHERE id = ANY({ids})")
            .AsNoTracking()
            .Select(j => new AdBatchShadowRow(
                j.Id,
                j.Title,
                EF.Property<string?>(j, OccupationGroupColumn),
                EF.Property<string?>(j, RegionColumn),
                EF.Property<string?>(j, EmploymentTypeColumn),
                EF.Property<string?>(j, MunicipalityColumn)))
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
                SsykOverlap: ScoreSsykMembership(
                    profile.SsykGroupConceptIds, profile.RelatedSsykGroupConceptIds, ad.OccupationGroupConceptId),
                TitleSimilarity: ScoreTitle(cvTitleLexemes, ad.Title),
                RegionFit: ScoreOrtUnion(
                    profile.PreferredRegionConceptIds, profile.PreferredMunicipalityConceptIds,
                    profile.ContainmentRegionConceptIds,
                    ad.RegionConceptId, ad.MunicipalityConceptId),
                EmploymentFit: ScoreMembership(profile.PreferredEmploymentTypeConceptIds, ad.EmploymentTypeConceptId));
        }

        return result;
    }

    // Fas 4 STEG 6 (F4-6, ADR 0074 row U5b; senior-cto-advisor Decision A=A2 /
    // B=B2 / C=C1 / D=DD-shape-1+DD-verdict-A / E=DE-combine-2(skill-only)+DE-display-1
    // / F=F1) — the FULL match scorer. Built ON TOP of the Fast vertical: it loads
    // the ad's title + the three facet columns + the extracted_terms VO in
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
    // #300 PR-4 (ADR 0084 §F4): returns a FullScoredMatch — the score PLUS SsykIsRelated (the ad
    // matched only via a RELATED occupation group). Lit by the live ?includeRelated toggle (off
    // by default, #300); with it off the related set is empty, so SsykIsRelated is false.
    public async ValueTask<FullScoredMatch> ScoreFullAsync(
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
                EF.Property<string?>(j, MunicipalityColumn),
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
            SsykOverlap: ScoreSsykMembership(
                fast.SsykGroupConceptIds, fast.RelatedSsykGroupConceptIds, ad.OccupationGroupConceptId),
            TitleSimilarity: ScoreTitle(fast.Title, ad.Title),
            RegionFit: ScoreOrtUnion(
                fast.PreferredRegionConceptIds, fast.PreferredMunicipalityConceptIds,
                fast.ContainmentRegionConceptIds,
                ad.RegionConceptId, ad.MunicipalityConceptId),
            EmploymentFit: ScoreMembership(fast.PreferredEmploymentTypeConceptIds, ad.EmploymentTypeConceptId));

        var terms = (ad.ExtractedTerms ?? ExtractedTerms.Empty).Terms;
        var cvSkills = profile.CvSkillConceptIds.ToHashSet(StringComparer.Ordinal);

        var fullScore = new FullMatchScore(
            Fast: fastScore,
            SkillOverlap: ScoreConceptCoverage(
                terms.Where(t => t.Kind == ExtractedTermKind.Skill), cvSkills),
            MustHaveCoverage: ScoreConceptCoverage(
                terms.Where(t => t.Kind == ExtractedTermKind.Requirement
                    && t.Source == ExtractedTermSource.MustHave), cvSkills),
            NiceToHaveCoverage: ScoreConceptCoverage(
                terms.Where(t => t.Kind == ExtractedTermKind.Requirement
                    && t.Source == ExtractedTermSource.NiceToHave), cvSkills));

        return new FullScoredMatch(
            fullScore,
            SsykIsRelated: IsSsykRelated(
                fast.SsykGroupConceptIds, fast.RelatedSsykGroupConceptIds, ad.OccupationGroupConceptId),
            // #477 Low 2 — the covered-skill concept-ids (evidence), same terms/cvSkills the
            // SkillOverlap dimension consumed above.
            CoveredSkillConceptIds(terms, cvSkills));
    }

    // Fas 4 STEG 15 (F4-15, ADR 0076 Decision 6) — the zero-N+1 batch form of
    // ScoreFullAsync (the page-scoped match-tag overlay upgraded to Full). It loads
    // ALL requested ads' Fast shadows + the extracted_terms VO in ONE round-trip
    // (the SAME `FromSql = ANY` shape as ScoreBatchAsync, extended with the VO
    // projection of ScoreFullAsync), then runs the SAME Fast + concept-coverage
    // helpers in-memory per row (so each FullMatchScore equals ScoreFullAsync for
    // that ad + profile — the regression contract). NO AI/LLM.
    // #300 PR-4 (ADR 0084 §F4): each value is a FullScoredMatch carrying SsykIsRelated per ad.
    public async ValueTask<IReadOnlyDictionary<JobAdId, FullScoredMatch>> ScoreFullBatchAsync(
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
        // exactly as in ScoreFullAsync's .Select. Testcontainers is the oracle.
        //
        // KNOWN GAP (#864) — NO STATUS GATE HERE EITHER, and this is the third of three. The old comment
        // claimed the global soft-delete filter composed so that "soft-deleted ads are absent ⇒ no
        // entry"; that filter never had a writer and is retired (#821). An ARCHIVED ad is fully scored.
        // BackgroundMatchingJob calls this path but gates Status == Active itself (:145), which MASKS the
        // gap there — no UserJobAdMatch row is ever persisted for an archived ad. The exposure is the
        // read-only client-supplied-id surface. Pinned by a characterization test.
        var rows = await db.JobAds
            .FromSql($"SELECT * FROM job_ads WHERE id = ANY({ids})")
            .AsNoTracking()
            .Select(j => new AdFullBatchRow(
                j.Id,
                j.Title,
                EF.Property<string?>(j, OccupationGroupColumn),
                EF.Property<string?>(j, RegionColumn),
                EF.Property<string?>(j, EmploymentTypeColumn),
                EF.Property<string?>(j, MunicipalityColumn),
                j.ExtractedTerms))
            .ToListAsync(cancellationToken);

        // Hoist the constant CV-side inputs out of the per-ad loop (parity the Fast
        // batch's cvTitleLexemes hoist): the title lexemes and the skill-id set are
        // the same for every ad in the batch.
        var fast = profile.Fast;
        var cvTitleLexemes = TryCvTitleLexemes(fast.Title);
        var cvSkills = profile.CvSkillConceptIds.ToHashSet(StringComparer.Ordinal);

        var result = new Dictionary<JobAdId, FullScoredMatch>(rows.Count);
        foreach (var ad in rows)
        {
            var fastScore = new MatchScore(
                SsykOverlap: ScoreSsykMembership(
                    fast.SsykGroupConceptIds, fast.RelatedSsykGroupConceptIds, ad.OccupationGroupConceptId),
                TitleSimilarity: ScoreTitle(cvTitleLexemes, ad.Title),
                RegionFit: ScoreOrtUnion(
                    fast.PreferredRegionConceptIds, fast.PreferredMunicipalityConceptIds,
                    fast.ContainmentRegionConceptIds,
                    ad.RegionConceptId, ad.MunicipalityConceptId),
                EmploymentFit: ScoreMembership(fast.PreferredEmploymentTypeConceptIds, ad.EmploymentTypeConceptId));

            var terms = (ad.ExtractedTerms ?? ExtractedTerms.Empty).Terms;
            var fullScore = new FullMatchScore(
                Fast: fastScore,
                SkillOverlap: ScoreConceptCoverage(
                    terms.Where(t => t.Kind == ExtractedTermKind.Skill), cvSkills),
                MustHaveCoverage: ScoreConceptCoverage(
                    terms.Where(t => t.Kind == ExtractedTermKind.Requirement
                        && t.Source == ExtractedTermSource.MustHave), cvSkills),
                NiceToHaveCoverage: ScoreConceptCoverage(
                    terms.Where(t => t.Kind == ExtractedTermKind.Requirement
                        && t.Source == ExtractedTermSource.NiceToHave), cvSkills));

            result[ad.Id] = new FullScoredMatch(
                fullScore,
                SsykIsRelated: IsSsykRelated(
                    fast.SsykGroupConceptIds, fast.RelatedSsykGroupConceptIds, ad.OccupationGroupConceptId),
                // #477 Low 2 — the covered-skill concept-ids (evidence), same terms/cvSkills the
                // SkillOverlap dimension consumed above (parity ScoreFullAsync).
                CoveredSkillConceptIds(terms, cvSkills));
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

    // #477 Low 2 — the concept-ids of the ad's SKILL extracted-terms the CV's confirmed skills
    // COVER (the SkillOverlap intersection surfaced as IDS, for the persisted explainability
    // evidence — ScoreConceptCoverage surfaces Display LABELS, DE-display-1). Deduped +
    // Ordinal-ordered (deterministic). Skill partition ONLY — must_have / nice_to_have are
    // separate coverage dimensions; UserJobAdMatch.MatchedSkillConceptIds is specifically the
    // skill evidence. Empty when the CV has no confirmed skills (parity ScoreConceptCoverage's
    // NotAssessed) or the ad has no covered Skill term. Uncapped + persistence-unaware — the
    // background scan (the only persister) truncates to UserJobAdMatch.MaxMatchedSkills; the
    // display consumers (page tag / modal) read the full set. Never logs (CLAUDE §5).
    private static List<string> CoveredSkillConceptIds(
        IEnumerable<ExtractedTerm> adTerms, HashSet<string> cvSkillConceptIds)
    {
        if (cvSkillConceptIds.Count == 0)
        {
            return [];
        }

        return adTerms
            .Where(t => t.Kind == ExtractedTermKind.Skill)
            .Select(t => t.ConceptId)
            .OfType<string>() // drops a null ConceptId + narrows to string (no naked `!`, §3 NRT)
            .Where(cvSkillConceptIds.Contains)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
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

    // SSYK gate broadened to exact ∪ related (ADR 0084 §5 / §F4, issue #300). The CV side
    // holds TWO ssyk-4 lists — the user's STATED exact occupation groups and the substitutable
    // RELATED groups (derived from PR-1's taxonomy_relations snapshot behind ITaxonomyReadModel).
    // Match when the ad's group is in EITHER set; NotAssessed when BOTH CV-side lists are empty
    // OR the ad has no group (parity ScoreMembership rule 1 — NoMatch stays reserved for "data
    // present on both sides, disjoint"). Matched carries the hit concept-id; Missing carries the
    // ad's group the union lacks (the civic "what the ad offers that you did not pick" direction).
    //
    // SCOPE (senior-cto-advisor Shape α): this broadens the GATE only — the profile builder
    // populates the related set when the live ?includeRelated toggle is on (off by default, #300);
    // with it off (or no related supplied) every caller sees exact-only behaviour bit-for-bit. The
    // exact-vs-related distinction (which set produced the hit) is surfaced to MatchGradeCalculator
    // via its isRelated parameter in PR-4, together with the Related-cap wiring + the GradeRank
    // SQL-rank parity oracle (ADR 0084 §Implementation). Exact precedence is therefore moot for
    // the verdict here (the union is a Match regardless of which set hit).
    private static MatchDimension ScoreSsykMembership(
        IReadOnlyList<string> exact, IReadOnlyList<string> related, string? adValue)
    {
        if ((exact.Count == 0 && related.Count == 0) || string.IsNullOrEmpty(adValue))
        {
            return NotAssessed();
        }

        return exact.Contains(adValue, StringComparer.Ordinal)
               || related.Contains(adValue, StringComparer.Ordinal)
            ? new MatchDimension(MatchDimensionVerdict.Match, [adValue], [])
            : new MatchDimension(MatchDimensionVerdict.NoMatch, [], [adValue]);
    }

    // #300 PR-4 (ADR 0084 §F4) — the exact-vs-related SPLIT the union verdict (ScoreSsykMembership)
    // deliberately does NOT carry: TRUE iff the SSYK gate passed through the RELATED set ONLY, i.e.
    // the ad's group is in the related set AND NOT in the stated exact set (exact-precedence — a
    // group in both is an exact hit, not related). Surfaced on the FullScoredMatch carrier beside
    // the score so MatchGradeCalculator caps a related-only hit at MatchGrade.Related (BEFORE the
    // RB1/F1(b) gates). Categorical, not a magnitude (Goodhart-safe). Lit by the live ?includeRelated
    // toggle (off by default, #300): with it off the related set is empty, so this is false. A non-match ad
    // (group in neither set) reads false here and grades null anyway, so the flag is only consulted
    // by the calculator after the gate passes.
    private static bool IsSsykRelated(
        IReadOnlyList<string> exact, IReadOnlyList<string> related, string? adValue) =>
        !string.IsNullOrEmpty(adValue)
        && related.Contains(adValue, StringComparer.Ordinal)
        && !exact.Contains(adValue, StringComparer.Ordinal);

    // Location ("ort") fit as a region ∪ municipality UNION (Spår 3, ADR 0076-amendment
    // 2026-06-21; senior-cto-advisor verdict C). The two granularities fold into ONE
    // dimension — the verdict keeps the name RegionFit (CTO B); there is no 5th dimension.
    // Match = the ad's region is among the preferred regions OR the ad's municipality is
    // among the preferred municipalities (a "hela länet" region preference matches any ad in
    // that län; a specific municipality preference matches that kommun). NotAssessed when NO
    // ort preference is stated (BOTH lists empty) OR the ad carries NEITHER ort value (BOTH
    // shadows NULL) — the honest "can't assess" state, parity ScoreMembership rule 1.
    // NoMatch (which floors the grade to Basic via the UNCHANGED MatchGradeCalculator RB1
    // rule) ONLY when an ort preference IS stated AND the ad HAS at least one ort value AND
    // there is no union hit AND the containment carve-out below does not apply (e.g. a
    // kommun-SPECIFIC ad in the same län but a non-preferred kommun — mirrors search; a
    // LÄN-ONLY ad in a containment län reads NotAssessed instead, see #477 Low 1 below).
    // Matched/Missing carry the cited ort concept-ids (Ordinal-sorted); the modal
    // (PR-D) resolves their granularity (kommun-träff vs län-träff) for the evidence copy.
    //
    // CRITICAL (CTO impl-trap): the NoMatch test is the COMBINED predicate
    // `stated AND ad-has-some-ort-value AND no-union-hit` — NEVER a bare
    // `!preferredMunicipalities.Contains(adMunicipality)`. A NULL municipality shadow on an
    // ad that has a region must NOT read as a municipality-NoMatch, and must NOT appear in
    // Missing (a municipality hit/miss is only ever considered when the ad HAS a municipality).
    //
    // #477 Low 1 — kommun→län-containment (containmentRegions = the län that contain the user's
    // preferred kommuner, derived by the profile builder from ParentConceptId). ONE new branch,
    // ONE direction: an ad that is LÄN-ONLY (has a region shadow, municipality shadow NULL) whose
    // region is in containmentRegions reads NotAssessed (empty Matched/Missing), NOT NoMatch — a
    // län-only ad in the user's OWN kommun's län is not a location contradiction, so a kommun-only
    // preference must not RB1-floor it to Basic. Evaluated AFTER the direct region/municipality hit
    // (a direct hit is still Match) and ONLY for län-only ads: a kommun-SPECIFIC ad in a different
    // kommun of the same län stays NoMatch (the user deliberately narrowed — mirrors search).
    // NotAssessed (not Match) is the honest verdict: a län-only ad does not confirm the user's
    // specific kommun, so it neither floors nor lifts the grade (MatchGradeCalculator RB1 unchanged).
    private static MatchDimension ScoreOrtUnion(
        IReadOnlyList<string> preferredRegions,
        IReadOnlyList<string> preferredMunicipalities,
        IReadOnlyList<string> containmentRegions,
        string? adRegion,
        string? adMunicipality)
    {
        var stated = preferredRegions.Count > 0 || preferredMunicipalities.Count > 0;
        var hasAdRegion = !string.IsNullOrEmpty(adRegion);
        var hasAdMunicipality = !string.IsNullOrEmpty(adMunicipality);

        // NotAssessed: no ort preference stated, OR the ad has neither ort value.
        if (!stated || (!hasAdRegion && !hasAdMunicipality))
        {
            return NotAssessed();
        }

        // A municipality hit/miss is only ever considered when the ad HAS a municipality
        // (the impl-trap guard): a NULL municipality contributes nothing to either side.
        var regionHit = hasAdRegion && preferredRegions.Contains(adRegion!, StringComparer.Ordinal);
        var municipalityHit =
            hasAdMunicipality && preferredMunicipalities.Contains(adMunicipality!, StringComparer.Ordinal);

        if (regionHit || municipalityHit)
        {
            var matched = new List<string>(2);
            if (regionHit)
            {
                matched.Add(adRegion!);
            }

            if (municipalityHit)
            {
                matched.Add(adMunicipality!);
            }

            matched.Sort(StringComparer.Ordinal);
            return new MatchDimension(MatchDimensionVerdict.Match, matched, []);
        }

        // #477 Low 1 — containment: a LÄN-ONLY ad (region present, municipality NULL) whose region
        // contains a preferred kommun is NOT a location contradiction → NotAssessed (neither floors
        // nor lifts). ONE direction (kommun-pref → län-only-ad); a kommun-specific ad (municipality
        // present) in a non-preferred kommun of the same län is deliberately excluded here and falls
        // through to NoMatch below (the user narrowed to their kommun — mirrors search).
        if (hasAdRegion && !hasAdMunicipality
            && containmentRegions.Contains(adRegion!, StringComparer.Ordinal))
        {
            return NotAssessed();
        }

        // Stated AND the ad has at least one ort value AND no union hit → NoMatch. Missing =
        // the ad's PRESENT ort value(s) the user did not select (the civic-useful "what the
        // ad offers that you didn't pick" direction, parity ScoreMembership).
        var missing = new List<string>(2);
        if (hasAdRegion)
        {
            missing.Add(adRegion!);
        }

        if (hasAdMunicipality)
        {
            missing.Add(adMunicipality!);
        }

        missing.Sort(StringComparer.Ordinal);
        return new MatchDimension(MatchDimensionVerdict.NoMatch, [], missing);
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

    // The minimal row the scorer needs — title + the three facet values (#841: ordinary,
    // C#-written ingest columns since 2026-07-13; they used to be STORED generated columns that
    // the raw_payload purge silently nulled).
    // A constructor-projected type (EF maps the ctor); shadow values are nullable
    // (NULL ⇒ the ad has no value on that dimension ⇒ NotAssessed).
    private sealed record AdFacetRow(
        string Title,
        string? OccupationGroupConceptId,
        string? RegionConceptId,
        string? EmploymentTypeConceptId,
        string? MunicipalityConceptId);

    // The batch row (F4-13) — AdFacetRow plus the JobAdId key, so ScoreBatchAsync can
    // key the result dictionary. j.Id materializes via its value converter (parity the
    // single-ad equality filter); shadow values are nullable (NULL ⇒ NotAssessed).
    private sealed record AdBatchShadowRow(
        JobAdId Id,
        string Title,
        string? OccupationGroupConceptId,
        string? RegionConceptId,
        string? EmploymentTypeConceptId,
        string? MunicipalityConceptId);

    // The Full row (F4-6) — the Fast inputs plus the extracted_terms VO (materialized
    // via its jsonb ValueConverter). Constructor-projected; ExtractedTerms is nullable
    // (NULL ⇒ never extracted ⇒ the three new dims read NotAssessed).
    private sealed record AdFullRow(
        string Title,
        string? OccupationGroupConceptId,
        string? RegionConceptId,
        string? EmploymentTypeConceptId,
        string? MunicipalityConceptId,
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
        string? MunicipalityConceptId,
        ExtractedTerms? ExtractedTerms);
}
