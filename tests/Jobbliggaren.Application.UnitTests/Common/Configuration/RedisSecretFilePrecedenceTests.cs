using Jobbliggaren.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Common.Configuration;

public class RedisSecretFilePrecedenceTests
{
    [Theory]
    [InlineData("Redis", "")]
    [InlineData("Redis", " \n")]
    [InlineData("VolatileRedis", "")]
    [InlineData("VolatileRedis", " \n")]
    public void EmptyFile_WithEarlierValidConnection_RefusesFallback(string name, string content)
    {
        var identity = name == "Redis" ? RedisClientIdentity.ApiPersistent : RedisClientIdentity.ApiVolatile;
        var user = name == "Redis" ? "api-persistent" : "api-volatile";
        var key = $"ConnectionStrings:{name}";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new(key, $"localhost:6379,user={user},password=synthetic")])
            .Add(new EnvFileSecretsConfigurationSource(
                () => [new($"ConnectionStrings__{name}_FILE", "/synthetic/redis")], _ => content))
            .Build();

        config[key].ShouldBe(string.Empty);
        Should.Throw<InvalidOperationException>(() => RedisClientConfiguration.Create(config[key], identity));
    }

    [Theory]
    [InlineData("ConnectionStrings__Redis_FILE")]
    [InlineData("ConnectionStrings__VolatileRedis_FILE")]
    [InlineData("connectionstrings__redis_FILE")]
    public void BlankRedisPointer_WithEarlierSource_RefusesConfiguration(string variable)
    {
        Should.Throw<InvalidOperationException>(() => new ConfigurationBuilder()
            .AddInMemoryCollection([new("ConnectionStrings:Redis", "localhost:6379")])
            .Add(new EnvFileSecretsConfigurationSource(
                () => [new(variable, " ")], _ => throw new InvalidOperationException("must not read")))
            .Build());
    }

    [Fact]
    public void EmptyCryptoFile_WithEarlierValue_MasksThatValue()
    {
        const string key = "FieldEncryption:LocalMasterKeyBase64";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new(key, "earlier-value")])
            .Add(new EnvFileSecretsConfigurationSource(
                () => [new("FieldEncryption__LocalMasterKeyBase64_FILE", "/synthetic/master")], _ => "\n"))
            .Build();

        config[key].ShouldBe(string.Empty);
    }
}
