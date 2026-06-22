using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Domain.Resumes.Parsing;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Resumes.Queries.GetParsedResumeSkills;

/// <summary>
/// Returns the OWNING job seeker's non-PII JobTech skill proposals for a PendingReview
/// parsed-CV staging artifact (ADR 0079 STEG 3). Mirrors
/// <c>GetParsedResumeOccupationsQueryHandler</c>'s fail-closed IDOR shape EXACTLY
/// (resolve owner → owner-scoped find → cross-user/not-found → null + audit, no
/// enumeration oracle), with the same deliberate difference: it PROJECTS the plain-jsonb
/// <c>skill_proposals</c> column instead of materialising the aggregate. Materialising
/// would (a) hit the <c>FieldDecryptionMaterializationInterceptor</c> on the CV-PII
/// shadows with no warmed DEK (the query is intentionally NOT
/// <c>IRequiresFieldEncryptionKey</c>) → throw, and (b) decrypt PII this read never uses
/// (PII-minimisation, CLAUDE.md §5). The anonymous projection wrapper distinguishes "row
/// found, no proposals" (empty list) from "no row" (null → drives the cross-user probe).
/// </summary>
public sealed class GetParsedResumeSkillsQueryHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IFailedAccessLogger failedAccessLogger)
    : IQueryHandler<GetParsedResumeSkillsQuery, IReadOnlyList<SkillProposalDto>?>
{
    public async ValueTask<IReadOnlyList<SkillProposalDto>?> Handle(
        GetParsedResumeSkillsQuery query, CancellationToken cancellationToken)
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
        var found = await db.ParsedResumes
            .AsNoTracking()
            .Where(r => r.Id == parsedResumeId && r.JobSeekerId == jobSeekerId)
            .Select(r => new
            {
                Proposals = EF.Property<List<ProposedSkill>>(r, "_skillProposals"),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (found is null)
        {
            // Identical NotFound for cross-user and unknown — no enumeration oracle. A
            // promoted/discarded artifact is excluded by the global DeletedAt filter from BOTH
            // the owner-scoped find AND this probe → plain null, no false cross-user audit on a
            // legitimate own-promote (parity GetParsedResumeOccupationsQueryHandler).
            var exists = await db.ParsedResumes
                .AsNoTracking()
                .AnyAsync(r => r.Id == parsedResumeId, cancellationToken);
            if (exists)
            {
                failedAccessLogger.LogCrossUserAttempt(
                    "ParsedResume", parsedResumeId.Value, currentUser.UserId.Value,
                    "GetParsedResumeSkills");
            }
            return null;
        }

        return found.Proposals
            .Select(p => new SkillProposalDto(p.ConceptId, p.Label))
            .ToList();
    }
}
