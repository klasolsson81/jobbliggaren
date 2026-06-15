using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Domain.JobAds;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.JobAds.Queries.GetJobAdExtractedTerms;

/// <summary>
/// Reads the persisted <see cref="ExtractedTerms"/> for one job ad and maps the
/// Domain value object to a transport DTO at the boundary (CLAUDE.md §2.3). No
/// extraction logic here — the deterministic extraction lives in the Infrastructure
/// extractor and is materialized at ingest/backfill. Returns <c>null</c> when the
/// ad does not exist; a not-yet-extracted ad yields an empty term list.
/// </summary>
public sealed class GetJobAdExtractedTermsQueryHandler(IAppDbContext db)
    : IQueryHandler<GetJobAdExtractedTermsQuery, JobAdExtractionDto?>
{
    public async ValueTask<JobAdExtractionDto?> Handle(
        GetJobAdExtractedTermsQuery query, CancellationToken cancellationToken)
    {
        var row = await db.JobAds
            .AsNoTracking()
            .Where(j => j.Id == new JobAdId(query.JobAdId))
            .Select(j => new { j.Id, j.ExtractedTerms })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return null;

        var terms = (row.ExtractedTerms ?? ExtractedTerms.Empty).Terms
            .Select(t => new ExtractedTermDto(
                t.Lexeme,
                t.Display,
                t.Kind.ToString(),
                t.Source.ToString(),
                t.MatchedOn,
                t.ConceptId,
                t.Weight))
            .ToList();

        return new JobAdExtractionDto(row.Id.Value, terms);
    }
}
