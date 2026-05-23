using JobbPilot.Application.Common;
using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Queries;
using JobbPilot.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;

namespace JobbPilot.Infrastructure.JobAds;

/// <summary>
/// ADR 0062 — <see cref="IJobAdSearchQuery"/>-implementation. Hela
/// sök-kompositionen (ssyk/region-filter, q-FTS-hybrid, ts_rank-relevans,
/// sortering, paginering, projektion) bor här eftersom PostgreSQL
/// full-text-search-LINQ (<c>websearch_to_tsquery</c> / <c>@@</c> /
/// <c>ts_rank</c>) ligger i Npgsql-assemblyn som arch-testet förbjuder i
/// Application.
/// <para>
/// ADR 0039 Beslut 1 (SPOT) — <see cref="ApplyCriteria"/> är den ENDA
/// filter-vägen; <c>ListJobAds</c> + <c>RunSavedSearch</c> (via
/// <see cref="SearchAsync"/>) och <c>ListRecentSearches</c> (via
/// <see cref="CountAsync"/>) delar den och kan aldrig divergera.
/// Behaviour-preserving flytt av den tidigare
/// <c>JobbPilot.Application.JobAds.Queries.JobAdSearch</c>-modulen (Fowler
/// 2018 — Move Function); befintliga ListJobAds-/RunSavedSearch-tester +
/// FTS-integrationstester är regressions-grind.
/// </para>
/// </summary>
internal sealed class JobAdSearchQuery(IAppDbContext db) : IJobAdSearchQuery
{
    // PostgreSQL text-search-config för svensk stemming. Måste matcha EXAKT
    // den config som search_vector-kolumnen genererades med (JobAdConfiguration
    // — to_tsvector('swedish', …)); annars matchar @@ inte GIN-indexet.
    private const string TextSearchConfig = "swedish";

    public async ValueTask<PagedResult<JobAdDto>> SearchAsync(
        JobAdSearchCriteria criteria, CancellationToken cancellationToken)
    {
        var baseQuery = ApplyCriteria(db.JobAds.AsNoTracking(), criteria.Filter);

        // Separat count-query (CLAUDE.md §3.6). Filter appliceras före count så
        // totalen reflekterar filtrerad mängd, inte hela korpusen.
        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var ordered = ApplySort(baseQuery, criteria.SortBy, criteria.Filter.Q);

        // ADR 0042 Beslut E — IsNew = PublishedAt inom "Ny sedan"-fönstret.
        // Lokalt fångad nullable → EF översätter jämförelsen (false när Since
        // är null, t.ex. RunSavedSearch).
        var since = criteria.Since;

        var items = await ordered
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .Select(j => new JobAdDto(
                j.Id.Value,
                j.Title,
                j.Company.Name,
                j.Description,
                j.Url,
                j.Source.Value,
                j.Status.Value,
                j.PublishedAt,
                j.ExpiresAt,
                j.CreatedAt,
                since != null && j.PublishedAt >= since))
            .ToListAsync(cancellationToken);

        return new PagedResult<JobAdDto>(items, totalCount, criteria.Page, criteria.PageSize);
    }

    public async ValueTask<int> CountAsync(
        JobAdFilterCriteria criteria, CancellationToken cancellationToken)
        // Ren count — ingen sortering, paginering eller projektion. Samma
        // ApplyCriteria-väg som SearchAsync (SPOT). ADR 0060 Beslut 4 N+1
        // capped vid 20; q-FTS gör count-vägen snabb (ADR 0061 Beslut 3).
        => await ApplyCriteria(db.JobAds.AsNoTracking(), criteria)
            .CountAsync(cancellationToken);

    // F2-P9 (TD-70). ssyk/region-filter via Postgres STORED generated columns
    // (B-tree-indexerade, equality-lookup). Shadow-properties refereras via
    // EF.Property<string?>(…) — de är inte top-level Domain-fält (Evans 2003
    // §14 ACL — JobTech-taxonomi är inte JobbPilots ubiquitous language).
    // ADR 0042 Beslut B — Ssyk/Region multi → SQL IN(…) via list.Contains.
    //
    // ADR 0062 — q-FTS-hybrid. FTS-grenen (search_vector @@ websearch_to_tsquery)
    // är den snabba primärvägen: GIN-index på tsvector + svensk stemming
    // (lärare/läraren → samma lexem). title-LIKE-grenen är en billig
    // substring-fallback för mitt-i-ord-matchning ("systemut" →
    // "systemutvecklare") — titlar är korta, ingen TOAST, träffar
    // ix_job_ads_title_lower_trgm. description-LIKE körs ALDRIG i q-grenen:
    // det var perf-rotorsaken (EXPLAIN ANALYZE 2026-05-21 — de-TOAST av ~13k
    // description-texter, trigram-selektivitet 7 581 falska positiva för
    // "lärare"; ADR 0061 → ADR 0062).
    //
    // ADR 0032-amendment 2026-05-23 + ADR 0062-amendment 2026-05-23: Archived-
    // JobAds (snapshot-retention + ExpiresAt-cron + stream-removal) får ALDRIG
    // synas i sök-vägen. SPOT-filter här gör att alla tre konsumenter
    // (ListJobAds, RunSavedSearch, ListRecentSearches CountAsync) ärver
    // Status=Active-disciplinen automatiskt (ADR 0039 Beslut 1).
    private static IQueryable<JobAd> ApplyCriteria(
        IQueryable<JobAd> source, JobAdFilterCriteria criteria)
    {
        // ADR 0032-amendment 2026-05-23 — slutanvändar-vyer ser bara Active.
        source = source.Where(j => j.Status == JobAdStatus.Active);

        if (criteria.Ssyk.Count > 0)
        {
            var ssykValues = criteria.Ssyk;
            source = source.Where(j => ssykValues.Contains(EF.Property<string?>(j, "SsykConceptId")));
        }

        if (criteria.Region.Count > 0)
        {
            var regionValues = criteria.Region;
            source = source.Where(j => regionValues.Contains(EF.Property<string?>(j, "RegionConceptId")));
        }

        if (!string.IsNullOrWhiteSpace(criteria.Q))
        {
            var q = criteria.Q;
            // title-LIKE-fallbacken lower:as redan invariant-side (C#); EF/Npgsql
            // translaterar .ToLower() (utan culture-arg) till SQL LOWER(col).
            // CA1304/CA1311-suppress: LINQ-translation till SQL, inte runtime-
            // string-op — culture är irrelevant. websearch_to_tsquery sköter
            // sin egen normalisering (lexem-tokenisering, robust mot user-input,
            // kastar aldrig på dålig syntax).
            var pattern = $"%{q.ToLowerInvariant()}%";
#pragma warning disable CA1304, CA1311
            source = source.Where(j =>
                EF.Property<NpgsqlTsVector>(j, "SearchVector")
                    .Matches(EF.Functions.WebSearchToTsQuery(TextSearchConfig, q))
                || EF.Functions.Like(j.Title.ToLower(), pattern));
#pragma warning restore CA1304, CA1311
        }

        return source;
    }

    // Alla enum-värden explicit listade + throw på unnamed (CS8524-disciplin).
    // Validators (ListJobAdsQueryValidator / SearchCriteria.Create) skyddar mot
    // invalid values innan denna metod — throw är defense-in-depth (fail-fast
    // vid framtida JobAdSortBy-tillägg: case missas → ArgumentOutOfRangeException
    // → integrationstest fail).
    private static IQueryable<JobAd> ApplySort(
        IQueryable<JobAd> source, JobAdSortBy sortBy, string? q) =>
        sortBy switch
        {
            JobAdSortBy.PublishedAtDesc => source.OrderByDescending(j => j.PublishedAt).ThenBy(j => j.Id),
            JobAdSortBy.PublishedAtAsc => source.OrderBy(j => j.PublishedAt).ThenBy(j => j.Id),
            // NULL-ExpiresAt sorteras sist (har inget slut-datum = pågående).
            JobAdSortBy.ExpiresAtDesc =>
                source.OrderBy(j => j.ExpiresAt == null)
                      .ThenByDescending(j => j.ExpiresAt)
                      .ThenBy(j => j.Id),
            JobAdSortBy.ExpiresAtAsc =>
                source.OrderBy(j => j.ExpiresAt == null)
                      .ThenBy(j => j.ExpiresAt)
                      .ThenBy(j => j.Id),
            // ADR 0062 — ts_rank ersätter den tidigare ILIKE-3-2-1-heuristiken
            // (ADR 0042 Beslut D2). Relevans-rankning av FTS-träffar.
            JobAdSortBy.Relevance => ApplyRelevanceSort(source, q),
            _ => throw new ArgumentOutOfRangeException(
                nameof(sortBy), sortBy, "Unknown JobAdSortBy — validator should reject."),
        };

    // ADR 0062 — relevans-sort via PostgreSQL ts_rank(search_vector,
    // websearch_to_tsquery('swedish', q)). Relevance kräver q non-null
    // (invariant i SearchCriteria.Create + ListJobAdsQueryValidator);
    // null-guarden är defense-in-depth (fallback PublishedAt desc, kastar ej i
    // query-vägen). Rader som matchade enbart via title-LIKE-fallbacken (ej
    // FTS) får ts_rank 0 → de sorteras efter FTS-träffarna, sedan PublishedAt
    // desc, sedan Id.
    private static IQueryable<JobAd> ApplyRelevanceSort(IQueryable<JobAd> source, string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return source.OrderByDescending(j => j.PublishedAt).ThenBy(j => j.Id);

        var query = q;
        return source
            .OrderByDescending(j =>
                EF.Property<NpgsqlTsVector>(j, "SearchVector")
                    .Rank(EF.Functions.WebSearchToTsQuery(TextSearchConfig, query)))
            .ThenByDescending(j => j.PublishedAt)
            .ThenBy(j => j.Id);
    }
}
