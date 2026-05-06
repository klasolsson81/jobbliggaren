using JobbPilot.Domain.Common;

namespace JobbPilot.Api.IntegrationTests.Sessions;

internal sealed class FakeDateTimeProvider(DateTimeOffset utcNow) : IDateTimeProvider
{
    public DateTimeOffset UtcNow => utcNow;

    public static FakeDateTimeProvider Now =>
        new(DateTimeOffset.UtcNow);
}
