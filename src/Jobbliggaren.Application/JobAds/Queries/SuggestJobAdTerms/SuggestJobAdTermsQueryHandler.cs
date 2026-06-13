using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobAds.Queries.SuggestJobAdTerms;

/// <summary>
/// ADR 0042 Beslut C + ADR 0067 Beslut 5a — utökad typeahead-union. Slår ihop
/// taxonomi-snapshot-prefix (<see cref="ITaxonomyReadModel.SuggestByPrefixAsync"/>,
/// in-memory ACL) och lokal <c>job_ads.Title</c> ILIKE-prefix (befintlig gren).
/// Tunn adapter — union + dedup + Take(limit) i Application; ingen Npgsql-specifik
/// LINQ (titel-grenen är provider-agnostisk <c>EF.Functions.Like</c>).
/// </summary>
public sealed class SuggestJobAdTermsQueryHandler(
    IAppDbContext db, ITaxonomyReadModel taxonomy)
    : IQueryHandler<SuggestJobAdTermsQuery, IReadOnlyList<SuggestionDto>>
{
    public async ValueTask<IReadOnlyList<SuggestionDto>> Handle(
        SuggestJobAdTermsQuery query, CancellationToken cancellationToken)
    {
        // (i) Taxonomi-prefix — in-memory ACL-snapshot (Län/Kommun/Yrkesområde/
        // Yrkesgrupp; occupation-name utesluts, VAL 4). Bryter EJ ADR 0043:s
        // extern-hop-förbud. Hela limit:en begärs; union cappar sedan totalen.
        var taxonomyHits = await taxonomy.SuggestByPrefixAsync(
            query.Prefix, query.Limit, cancellationToken);

        // (ii) Titel-prefix (ADR 0042 Beslut C, oförändrad gren). LIKE-metatecken
        // escapas så left-anchor bevaras (btree functional partial-index
        // användbart; ej seq-scan-DoS). Explicit ESCAPE '\'. .ToLower() →
        // SQL LOWER(col) (CA1304/CA1311-suppress: LINQ-translation, ej runtime).
        const string escapeChar = "\\";
        var pattern = LikePattern.EscapePrefix(query.Prefix).ToLowerInvariant() + "%";

#pragma warning disable CA1304, CA1311
        var titles = await db.JobAds
            .AsNoTracking()
            .Where(j => j.Status == JobAdStatus.Active)
            .Where(j => EF.Functions.Like(j.Title.ToLower(), pattern, escapeChar))
            .Select(j => j.Title)
            .Distinct()
            .OrderBy(t => t)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);
#pragma warning restore CA1304, CA1311

        // Union: taxonomi först (deterministisk enum→label-ordning från porten),
        // sedan titlar. Dedup-nyckel = (Kind, ConceptId) för taxonomi-noder
        // (ConceptId alltid satt) och (Title, Label) för titlar (Title saknar
        // ConceptId) — en taxonomi-nod och en titel kan dela label utan att vara
        // samma förslag (olika Kind). Cap till limit (validator garanterar 1–20).
        var result = new List<SuggestionDto>(query.Limit);
        var seen = new HashSet<(SuggestionKind, string)>();

        foreach (var hit in taxonomyHits)
        {
            if (result.Count >= query.Limit)
                break;
            if (seen.Add((hit.Kind, hit.ConceptId)))
                result.Add(new SuggestionDto(hit.Kind, hit.ConceptId, hit.Label));
        }

        foreach (var title in titles)
        {
            if (result.Count >= query.Limit)
                break;
            if (seen.Add((SuggestionKind.Title, title)))
                result.Add(new SuggestionDto(SuggestionKind.Title, null, title));
        }

        return result;
    }
}
