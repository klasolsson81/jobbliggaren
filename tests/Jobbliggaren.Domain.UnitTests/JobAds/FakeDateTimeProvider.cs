using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.UnitTests.JobAds;

internal sealed class FakeDateTimeProvider(DateTimeOffset utcNow) : IDateTimeProvider
{
    public DateTimeOffset UtcNow => utcNow;

    public static FakeDateTimeProvider At(DateTimeOffset time) => new(time);
    public static FakeDateTimeProvider Default => new(new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));
}
