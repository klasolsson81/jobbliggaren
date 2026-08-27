using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.Resumes.Queries.GetParsedResume;
using Jobbliggaren.Domain.Resumes.Parsing;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Queries.GetParsedResumeOccupations;

/// <summary>
/// Returns the OWNING job seeker's non-PII SSYK occupation proposals for a PendingReview or
/// Promoted parsed-CV staging artifact (F4-8). Mirrors <c>GetParsedResumeQueryHandler</c>'s fail-closed
/// IDOR shape (resolve owner → owner-scoped find → cross-user/not-found → null + audit,
/// no enumeration oracle). It PROJECTS the plain-jsonb
/// <c>occupation_proposals</c> column instead of materialising the aggregate. Materialising
/// would (a) hit the <c>FieldDecryptionMaterializationInterceptor</c> on the CV-PII shadows with
/// no warmed DEK (the query is intentionally NOT <c>IRequiresFieldEncryptionKey</c>) → throw, and
/// (b) decrypt PII this read never uses (PII-minimisation, CLAUDE.md §5; CTO Variant B
/// 2026-06-21). The anonymous projection wrapper distinguishes "row found, no proposals" (empty
/// list) from "no row" (null → drives the cross-user probe).
/// </summary>
public sealed class GetParsedResumeOccupationsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger)
    : IQueryHandler<GetParsedResumeOccupationsQuery, IReadOnlyList<OccupationProposalDto>?>
{
    public async ValueTask<IReadOnlyList<OccupationProposalDto>?> Handle(
        GetParsedResumeOccupationsQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.UserId.HasValue)
            return null;

        var jobSeekerId = await db.JobSeekers
            .AsNoTracking()
            .Where(js => js.UserId == currentUser.UserId.Value)
            .Select(js => js.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobSeekerId == default)
            return null;

        var parsedResumeId = new ParsedResumeId(query.ParsedResumeId);

        // PROJECT the plain-jsonb proposals — never materialise the aggregate (its encrypted
        // CV-PII shadows would otherwise hit the decryption interceptor with no warmed DEK and
        // throw, and decrypting PII we never read violates §5). The anonymous wrapper lets us
        // tell "row found, no proposals" (empty list) from "no row" (null → cross-user probe).
        //
        // IgnoreQueryFilters + an explicit status ALLOW-LIST, not the global DeletedAt filter.
        // Promote() soft-deletes the artifact, so the filter alone made a promoted CV's
        // proposals unreadable — and since the import endpoint ATTEMPTS auto-promote on every
        // upload, that is every ordinary upload. The allow-list is fail-closed by shape: a status added later
        // is unreadable until someone names it here, which `!= Discarded` would not have been.
        // Discarded stays out on its own merit: the user rejected that import.
        var found = await db.ParsedResumes
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(r => r.Id == parsedResumeId
                        && r.JobSeekerId == jobSeekerId
                        && (r.Status == ParsedResumeStatus.PendingReview
                            || r.Status == ParsedResumeStatus.Promoted))
            .Select(r => new
            {
                Proposals = EF.Property<List<ProposedOccupation>>(r, "_occupationProposals"),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (found is null)
        {
            // Identical NotFound for cross-user and unknown — no enumeration oracle. The probe
            // is scoped to rows this caller does NOT own, and that scope is load-bearing now
            // that the find above ignores the query filter: an own DISCARDED artifact reaches
            // here, and an unscoped probe would see it and log its own owner as a cross-user
            // attempt. Filtering by ownership reports only what the name says.
            var exists = await db.ParsedResumes
                .AsNoTracking()
                .IgnoreQueryFilters()
                .AnyAsync(
                    r => r.Id == parsedResumeId && r.JobSeekerId != jobSeekerId,
                    cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "ParsedResume", parsedResumeId.Value, currentUser.UserId.Value,
                    "GetParsedResumeOccupations");
            }
            return null;
        }

        return found.Proposals
            .Select(p => new OccupationProposalDto(p.ConceptId, p.Label, p.MatchedOn, p.ApproximateYears))
            .ToList();
    }
}
