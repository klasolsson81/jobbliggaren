using JobbPilot.Domain.Common;

namespace JobbPilot.Application.UnitTests.Sessions;

internal sealed class MutableFakeDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; set; } =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
}
