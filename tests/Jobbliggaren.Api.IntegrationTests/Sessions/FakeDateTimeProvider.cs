using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Api.IntegrationTests.Sessions;

internal sealed class FakeDateTimeProvider(DateTimeOffset utcNow) : IDateTimeProvider
{
    public DateTimeOffset UtcNow => utcNow;

    public static FakeDateTimeProvider Now =>
        new(DateTimeOffset.UtcNow);
}
