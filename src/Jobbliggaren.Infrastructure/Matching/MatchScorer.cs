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
    private MatchDimension ScoreTitle(string cvTitle, string adTitle)
    {
        IReadOnlyList<string> cvLexemes;
        IReadOnlyList<string> adLexemes;
        try
        {
            cvLexemes = analyzer.ToLexemes(cvTitle, TextLanguage.Swedish);
            adLexemes = analyzer.ToLexemes(adTitle, TextLanguage.Swedish);
        }
        catch (NotSupportedException)
        {
            // Forward-compat dormant guard (CTO re-ruling 2026-06-15): F4-5 always
            // requests Swedish (the Platsbanken corpus is Swedish; the profile has
            // no language signal and language detection is F4-8/9, ADR 0074
            // amendment) — so this does not fire in F4-5. When the language-aware
            // matcher contract arrives at F4-8/9 (ParsedResume carries a detected
            // language), a non-Swedish request degrades the title dimension to
            // NotAssessed instead of crashing. Caught NARROWLY around the analyzer
            // calls only (CLAUDE.md §5 — no catch-all).
            return NotAssessed();
        }

        var cv = cvLexemes.ToHashSet(StringComparer.Ordinal);
        var ad = adLexemes.ToHashSet(StringComparer.Ordinal);

        if (cv.Count == 0 || ad.Count == 0)
        {
            return NotAssessed();
        }

        var matched = ad.Where(cv.Contains).OrderBy(l => l, StringComparer.Ordinal).ToList();
        var missing = ad.Where(l => !cv.Contains(l)).OrderBy(l => l, StringComparer.Ordinal).ToList();

        var verdict = matched.Count == 0
            ? MatchDimensionVerdict.NoMatch
            : missing.Count == 0
                ? MatchDimensionVerdict.Match
                : MatchDimensionVerdict.Partial;

        return new MatchDimension(verdict, matched, missing);
    }

    private static MatchDimension NotAssessed() =>
        new(MatchDimensionVerdict.NotAssessed, [], []);

    // The minimal row the scorer needs — title + the three STORED shadow values.
    // A constructor-projected type (EF maps the ctor); shadow values are nullable
    // (NULL ⇒ the ad has no value on that dimension ⇒ NotAssessed).
    private sealed record AdShadowRow(
        string Title,
        string? OccupationGroupConceptId,
        string? RegionConceptId,
        string? EmploymentTypeConceptId);
}
