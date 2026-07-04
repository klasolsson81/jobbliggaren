using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Api.IntegrationTests.Sessions;

// Time-travelable clock for the absolute-cap tests (#620): the cap is enforced from
// the injected IDateTimeProvider, so advancing UtcNow past CreatedAt + AbsoluteTtl
// drives the ceiling deterministically while the real Redis key TTL (wall-clock,
// 14d) keeps the key alive for the sub-second test run.
internal sealed class MutableFakeDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; set; } =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
