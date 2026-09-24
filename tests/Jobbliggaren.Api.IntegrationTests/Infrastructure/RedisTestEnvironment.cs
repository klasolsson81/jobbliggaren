namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

internal sealed class RedisTestEnvironment : IDisposable
{
    private readonly Dictionary<string, string?> _previous = new(StringComparer.Ordinal);

    internal RedisTestEnvironment(string persistent, string volatileConnection)
    {
        foreach (var (key, value) in new Dictionary<string, string?>
        {
            ["ConnectionStrings__Redis"] = persistent,
            [VolatileRedisContainer.ConnectionStringVariable] = volatileConnection,
            ["ConnectionStrings__Redis_FILE"] = null,
            [VolatileRedisContainer.ConnectionStringVariable + "_FILE"] = null,
        })
        {
            _previous[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }
    }

    public void Dispose()
    {
        foreach (var (key, value) in _previous)
            Environment.SetEnvironmentVariable(key, value);
        _previous.Clear();
    }
}
