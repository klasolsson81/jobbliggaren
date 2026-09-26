using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.TestSupport;

/// <summary>
/// Writes a name into <c>job_seekers.display_name</c> for a saved profile, with raw SQL.
/// <para>
/// No path in <c>src/</c> writes that column since #1741 PR B (ADR 0142 D7), and from #1742 on the
/// model does not map it; the pin is <c>JobSeekerTests.DisplayName_HasNoWritePathOnTheAggregate</c>.
/// A test that needs a named account therefore asserts about a row a RETIRED actor wrote:
/// <c>JobSeeker.Register</c> with a name, or <c>UpdateDisplayName</c>, both before #1741 PR B.
/// </para>
/// </summary>
public static class LegacyAccountName
{
    public static async Task WriteAsync(DbContext db, Guid jobSeekerId, string name, CancellationToken ct)
    {
        var rows = await db.Database.ExecuteSqlAsync(
            $"UPDATE job_seekers SET display_name = {name} WHERE id = {jobSeekerId}", ct);
        if (rows != 1)
            throw new InvalidOperationException($"Expected one job_seekers row {jobSeekerId}, updated {rows}.");
    }
}
