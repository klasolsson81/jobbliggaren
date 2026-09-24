using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

public sealed class RedisTestEnvironmentTests
{
    [Fact]
    public void InheritedDevSecretFiles_AreNeverOpened_AndAreRestoredAfterTheFixture()
    {
        const string persistentKey = "ConnectionStrings__Redis_FILE";
        const string volatileKey = "ConnectionStrings__VolatileRedis_FILE";
        var previousPersistent = Environment.GetEnvironmentVariable(persistentKey);
        var previousVolatile = Environment.GetEnvironmentVariable(volatileKey);
        var absent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "must-not-read");
        try
        {
            Environment.SetEnvironmentVariable(persistentKey, absent);
            Environment.SetEnvironmentVariable(volatileKey, absent);
            using (new RedisTestEnvironment("fixture-persistent", "fixture-volatile"))
            {
                var configuration = new ConfigurationBuilder().AddEnvironmentVariables().AddEnvFileSecrets().Build();
                configuration.GetConnectionString("Redis").ShouldBe("fixture-persistent");
                configuration.GetConnectionString("VolatileRedis").ShouldBe("fixture-volatile");
            }
            Environment.GetEnvironmentVariable(persistentKey).ShouldBe(absent);
            Environment.GetEnvironmentVariable(volatileKey).ShouldBe(absent);
        }
        finally
        {
            Environment.SetEnvironmentVariable(persistentKey, previousPersistent);
            Environment.SetEnvironmentVariable(volatileKey, previousVolatile);
        }
    }
}
