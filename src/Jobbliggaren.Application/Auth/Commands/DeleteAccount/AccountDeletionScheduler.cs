using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Application.Auth.Jobs.HardDeleteAccounts;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Auth.Commands.DeleteAccount;

/// <summary>Schedules an explicit target within the caller's protected lifecycle transaction.</summary>
public sealed class AccountDeletionScheduler(
    IAppDbContext db,
    IDateTimeProvider clock,
    IAccountAccessReader reader,
    IAccountAccessWriter writer,
    IExternalLoginEraser externalLogins)
{
    public async Task<Result<AccountDeletionScheduled>> ScheduleAsync(
        Guid targetId, bool administratorInitiated, CancellationToken cancellationToken)
    {
        var target = await reader.ReadAsync(targetId, cancellationToken);
        if (target is null && administratorInitiated)
            return Result.Failure<AccountDeletionScheduled>(DomainError.NotFound(
                AccountAccessErrors.AccountNotFound, "Kontot hittades inte."));

        var jobSeeker = await db.JobSeekers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(seeker => seeker.UserId == targetId, cancellationToken);
        if (jobSeeker is null)
            return Result.Failure<AccountDeletionScheduled>(administratorInitiated
                ? DomainError.Gone(AccountAccessErrors.ProfileUnavailable, "Kontots profil finns inte längre.")
                : DomainError.NotFound("Auth.JobSeekerNotFound", "Profilen hittades inte för aktuell användare."));
        if (jobSeeker.DeletedAt is not null)
            return Result.Failure<AccountDeletionScheduled>(administratorInitiated
                ? DomainError.Conflict(AccountAccessErrors.AlreadyPendingDeletion, "Kontot väntar redan på radering.")
                : DomainError.Gone("Auth.AccountAlreadyDeleted", "Kontot väntar redan på radering."));
        if (!await writer.CanRemoveAccessAsync(targetId, cancellationToken))
            return Result.Failure<AccountDeletionScheduled>(DomainError.Conflict(
                AccountAccessErrors.LastAdministrator, "Den sista administratörens konto kan inte raderas."));

        var access = await writer.AdvanceDeletionAsync(targetId, cancellationToken);
        var applications = await db.Applications
            .Where(application => application.JobSeekerId == jobSeeker.Id)
            .Include(application => application.FollowUps)
            .Include(application => application.Notes)
            .Include(application => application.StatusChanges)
            .ToListAsync(cancellationToken);
        var resumes = await db.Resumes
            .Where(resume => resume.JobSeekerId == jobSeeker.Id)
            .Include(resume => resume.Versions)
            .ToListAsync(cancellationToken);

        var deletionClock = new DeletionClock(DateTimeOffset.FromUnixTimeMilliseconds(clock.UtcNow.ToUnixTimeMilliseconds()));
        foreach (var application in applications) application.SoftDelete(deletionClock);
        foreach (var resume in resumes) resume.SoftDelete(deletionClock);
        jobSeeker.SoftDelete(deletionClock);
        await externalLogins.EraseAllAsync(targetId, cancellationToken);

        var deletedAt = jobSeeker.DeletedAt!.Value;
        var eligibleAt = AccountRestoreWindow.EligibleAt(deletedAt);
        return Result.Success(new AccountDeletionScheduled(
            targetId, jobSeeker.Id.Value, deletedAt, eligibleAt,
            AccountRestoreWindow.FirstScheduledRunAfter(eligibleAt), access.IsSuspended, access.AccessRevision));
    }

    private sealed class DeletionClock(DateTimeOffset utcNow) : IDateTimeProvider
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
