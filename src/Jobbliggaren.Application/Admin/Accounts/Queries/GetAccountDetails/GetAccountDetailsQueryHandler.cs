using Jobbliggaren.Application.Common.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Admin.Accounts.Queries.GetAccountDetails;

/// <summary>
/// The account comes from the directory, and an active account's three counts from one projection over
/// <see cref="IAppDbContext"/>, whose query filters define what is live.
/// </summary>
public sealed class GetAccountDetailsQueryHandler(IAccountDirectory directory, IAppDbContext db)
    : IQueryHandler<GetAccountDetailsQuery, AccountDetailsDto?>
{
    public async ValueTask<AccountDetailsDto?> Handle(GetAccountDetailsQuery query, CancellationToken cancellationToken)
    {
        if (await directory.FindAsync(query.UserId, cancellationToken) is not { } entry)
            return null;

        var role = entry.IsAdmin ? AccountRole.Admin : AccountRole.User;
        var deletionEarliest = entry.PermanentDeletionEarliest;

        if (entry.Status != AccountStatus.Active || entry.JobSeekerId is not { } jobSeekerId)
        {
            return new AccountDetailsDto(
                entry.UserId, entry.Email, role, entry.Status, entry.EmailConfirmed,
                entry.RegisteredAt, deletionEarliest, null, null, null);
        }

        var applications = db.Applications;
        var resumes = db.Resumes;
        var savedSearches = db.SavedSearches;
        var counts = await db.JobSeekers
            .AsNoTracking()
            .Where(seeker => seeker.Id == jobSeekerId)
            .Select(seeker => new
            {
                Applications = applications.Count(application => application.JobSeekerId == seeker.Id),
                Resumes = resumes.Count(resume => resume.JobSeekerId == seeker.Id),
                SavedSearches = savedSearches.Count(search => search.JobSeekerId == seeker.Id),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return new AccountDetailsDto(
            entry.UserId, entry.Email, role, entry.Status, entry.EmailConfirmed,
            entry.RegisteredAt, deletionEarliest,
            counts?.Applications, counts?.Resumes, counts?.SavedSearches);
    }
}
