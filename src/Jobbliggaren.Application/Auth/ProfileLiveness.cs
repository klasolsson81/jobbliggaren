using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.Application.Auth;

/// <summary>
/// #1349's predicate in one place: an account is live only while its JobSeeker profile exists and is not soft-deleted,
/// the rule <c>LoginSubjectResolver</c> states as "a row with no JobSeeker is granted nothing". The re-authentication
/// gate and the completion of an administrator-initiated address change (#1975) both ask it.
/// </summary>
internal static class ProfileLiveness
{
    // IgnoreQueryFilters, because the global DeletedAt filter would hide the soft-deleted row this tells apart. Projected
    // to a ROW, not to a nullable value, and that is #1349's whole repair: `Select(js => (DateTimeOffset?)js.DeletedAt)`
    // made FirstOrDefaultAsync answer null for both "no row at all" and "a live row".
    internal static async Task<bool> HasLiveProfileAsync(this IAppDbContext db, Guid userId, CancellationToken ct)
    {
        var profile = await db.JobSeekers
            .IgnoreQueryFilters()
            .Where(js => js.UserId == userId)
            .Select(js => new { js.DeletedAt })
            .FirstOrDefaultAsync(ct);

        return profile is { DeletedAt: null };
    }
}
