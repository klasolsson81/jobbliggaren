using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.QA.Corpus.Generation;

/// <summary>
/// A fixed <see cref="IDateTimeProvider"/> for deterministic <c>ParsedResume</c> timestamps
/// (CLAUDE.md §5 — never <c>DateTime.UtcNow</c> in generated data; parity with
/// <c>CvReviewFixtures.FixedClock</c>).
/// </summary>
public sealed class FixedClock(DateTimeOffset utcNow) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; } = utcNow;

    public static FixedClock Default { get; } =
        new(new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero));
}
