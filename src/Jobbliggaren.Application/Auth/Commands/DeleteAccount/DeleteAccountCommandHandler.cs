using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Auth.Commands.DeleteAccount;

public sealed class DeleteAccountCommandHandler(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
    : ICommandHandler<DeleteAccountCommand, Result<Guid>>
{
    public async ValueTask<Result<Guid>> Handle(DeleteAccountCommand command, CancellationToken cancellationToken)
    {
        // Defense-in-depth: AuthorizationBehavior har normalt kollat
        // IAuthenticatedRequest-markern, men vi tar inte beroendet på pipeline-
        // konfiguration (ADR 0008-policy kan ändras, eller commando refactor:as
        // till intern impersonation-yta i Fas 6). Throw-safe fallback istället
        // för null-forgiving operator.
        if (!currentUser.UserId.HasValue)
            return Result.Failure<Guid>(
                DomainError.Validation(
                    "Auth.NotAuthenticated",
                    "Inloggning krävs för att radera konto."));

        var userId = currentUser.UserId.Value;

        // IgnoreQueryFilters: vi behöver hitta även soft-deletade JobSeekers
        // för idempotency-check (annars hamnar vi i NotFound-grenen vid retry).
        var jobSeeker = await db.JobSeekers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(js => js.UserId == userId, cancellationToken);

        if (jobSeeker is null)
            return Result.Failure<Guid>(
                DomainError.NotFound(
                    "Auth.JobSeekerNotFound",
                    "JobSeeker hittades inte för aktuell användare."));

        // Idempotens: om redan soft-deletat returnera Success utan ny audit-rad
        // (annars skulle AuditBehavior skriva en till Account.Deleted vid varje
        // retry-anrop). DELETE-semantik är tolerant mot multiple deletes.
        if (jobSeeker.DeletedAt is not null)
            return Result.Success(jobSeeker.Id.Value);

        // Hämta alla user-ägda aggregat för cascade. Global query filter
        // exkluderar redan soft-deletade barn — vid första radering är
        // hela ägar-trädet aktivt eftersom JobSeeker själv inte är raderat.
        //
        // .Include(FollowUps/Notes) är LOAD-BEARING (#482/#505): Application.SoftDelete
        // kaskaderar in-memory över _followUps/_notes — utan Include är de tomma i en
        // färsk request-DbContext → barnraderna står deleted_at IS NULL hela 30-dagars-
        // fönstret (paritet Resume-grenens .Include(Versions) nedan).
        var applications = await db.Applications
            .Where(a => a.JobSeekerId == jobSeeker.Id)
            .Include(a => a.FollowUps)
            .Include(a => a.Notes)
            .ToListAsync(cancellationToken);

        var resumes = await db.Resumes
            .Where(r => r.JobSeekerId == jobSeeker.Id)
            .Include(r => r.Versions)
            .ToListAsync(cancellationToken);

        // Cascade soft-delete via aggregate-rooternas egna SoftDelete-metoder.
        // Application.SoftDelete cascadar internt till FollowUp + ApplicationNote.
        // Resume.SoftDelete cascadar internt till ResumeVersions.
        // JobSeeker.SoftDelete har inga barn att cascadera (rotaggregat).
        foreach (var app in applications) app.SoftDelete(clock);
        foreach (var resume in resumes) resume.SoftDelete(clock);
        jobSeeker.SoftDelete(clock);

        // SaveChanges sker via UnitOfWorkBehavior — atomic. AuditBehavior
        // skriver Account.Deleted-raden i samma transaction. JobSeeker.Id
        // returneras till AuditBehavior via ExtractAggregateId.
        return Result.Success(jobSeeker.Id.Value);
    }
}
