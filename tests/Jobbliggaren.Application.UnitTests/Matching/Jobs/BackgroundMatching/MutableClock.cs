using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Application.UnitTests.Matching.Jobs.BackgroundMatching;

/// <summary>A wall clock the test moves by hand, between runs or from inside a collaborator.</summary>
internal sealed class MutableClock(DateTimeOffset start) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = start;

    public void Advance(TimeSpan by) => UtcNow += by;
}
