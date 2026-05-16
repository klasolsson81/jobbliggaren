using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.JobAds.Queries.SuggestJobAdTerms;

/// <summary>
/// ADR 0042 Beslut C — typeahead C1. Tunn adapter (speglar
/// ListJobAdsQueryHandler). Lokal <c>job_ads.Title</c> ILIKE-prefix mot
/// aktiva annonser. Index: btree functional <c>lower(title)
/// text_pattern_ops</c> partial <c>WHERE status='Active' AND deleted_at IS
/// NULL</c> (senior-cto-advisor Variant A 2026-05-16 — query-predikatet
/// matchar partial-index-predikatet exakt).
/// </summary>
public sealed class SuggestJobAdTermsQueryHandler(IAppDbContext db)
    : IQueryHandler<SuggestJobAdTermsQuery, IReadOnlyList<string>>
{
    public async ValueTask<IReadOnlyList<string>> Handle(
        SuggestJobAdTermsQuery query, CancellationToken cancellationToken)
    {
        // LIKE-metatecken i användarprefix escapas (left-anchor bevaras →
        // btree-prefix-index användbart; ej seq-scan-DoS). EF.Functions.Like
        // parametriserar värdet (ingen SQL-injektion). Provider-agnostiskt
        // .ToLower()-mönster + CA-suppress = samma som JobAdSearch.ApplyCriteria
        // (EF.Functions.ILike ligger i Npgsql-extension → Clean Arch-brott).
        // Explicit ESCAPE '\' (3-arg EF.Functions.Like) — förlita inte på
        // implicit default-escape genom EF→Npgsql-översättningen (overifierbart;
        // gör \%/\_ deterministiskt literala). LikePattern escapar metatecknen.
        const string escapeChar = "\\";
        var pattern = LikePattern.EscapePrefix(query.Prefix).ToLowerInvariant() + "%";

        // DeletedAt == null via global query filter (JobAdConfiguration).
        // Status == Active explicit (ADR 0042 — typeahead får ej föreslå
        // arkiverade/expirerade titlar; matchar partial-index-predikatet).
#pragma warning disable CA1304, CA1311
        return await db.JobAds
            .AsNoTracking()
            .Where(j => j.Status == JobAdStatus.Active)
            .Where(j => EF.Functions.Like(j.Title.ToLower(), pattern, escapeChar))
            .Select(j => j.Title)
            .Distinct()
            .OrderBy(t => t)
            .Take(query.Limit)
            .ToListAsync(cancellationToken);
#pragma warning restore CA1304, CA1311
    }
}
