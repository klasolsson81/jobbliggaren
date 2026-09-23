using Jobbliggaren.Domain.JobSeekers;
using Microsoft.EntityFrameworkCore;

namespace Jobbliggaren.TestSupport;

/// <summary>
/// Writes a name into <c>job_seekers.display_name</c> for a profile the context tracks.
/// <para>
/// No path in <c>src/</c> writes that column since #1741 PR B (ADR 0142 D7), and
/// <c>JobSeekerTests.DisplayName_HasNoWritePathOnTheAggregate</c> pins that the aggregate cannot produce
/// the state. A test that needs a named account therefore asserts about a row a RETIRED actor wrote:
/// <c>JobSeeker.Register</c> with a name, or <c>UpdateDisplayName</c>, both before #1741 PR B; for a
/// personnummer-shaped name, either of them before #1117's invariant. The column is mapped until 4b
/// (#1742) drops it.
/// </para>
/// </summary>
public static class LegacyAccountName
{
    public static void Write(DbContext db, JobSeeker seeker, string name) =>
        db.Entry(seeker).Property(js => js.DisplayName).CurrentValue = name;
}
