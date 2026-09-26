using Jobbliggaren.Infrastructure.Configuration;
using Shouldly;
using StackExchange.Redis;

namespace Jobbliggaren.Application.UnitTests.Common.Configuration;

public sealed class RedisClientConfigurationTests
{
    [Theory]
    [InlineData("api-persistent", 0)]
    [InlineData("worker-persistent", 1)]
    [InlineData("api-volatile", 2)]
    public void Create_ExpectedIdentity_AppliesRestrictedStandalonePolicy(string user, int identity)
    {
        var options = RedisClientConfiguration.Create(
            $"localhost:6379,user={user},password=synthetic-only,allowAdmin=true,abortConnect=true,defaultDatabase=0",
            (RedisClientIdentity)identity);

        options.User.ShouldBe(user);
        options.DefaultDatabase.ShouldBe(0);
        options.Protocol.ShouldBe(RedisProtocol.Resp2);
        options.DefaultVersion.ShouldBe(new Version(8, 6));
        options.ConfigurationChannel.ShouldBeEmpty();
        options.TieBreaker.ShouldBeEmpty();
        options.AllowAdmin.ShouldBeFalse();
        options.AbortOnConnectFail.ShouldBeFalse();
        options.IncludeDetailInExceptions.ShouldBeFalse();
        options.IncludePerformanceCountersInExceptions.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("localhost:6379")]
    [InlineData("localhost:6379,user=api-persistent")]
    [InlineData("localhost:6379,user=api-persistent,password=   ")]
    [InlineData("localhost:6379,user=worker-persistent,password=synthetic-only")]
    [InlineData("localhost:6379,user=api-volatile,password=synthetic-only")]
    [InlineData("localhost:6379,user=API-PERSISTENT,password=synthetic-only")]
    [InlineData("user=api-persistent,password=synthetic-only")]
    [InlineData("localhost:6379,other:6379,user=api-persistent,password=synthetic-only")]
    [InlineData("localhost:6379,user=api-persistent,password=synthetic-only,defaultDatabase=1")]
    [InlineData("localhost:6379,user=api-persistent,password=synthetic-only,defaultDatabase=-1")]
    [InlineData("localhost:6379,user=api-persistent,password=synthetic-only,serviceName=sentinel")]
    [InlineData("localhost:6379,user=api-persistent,password=synthetic-only,proxy=Twemproxy")]
    [InlineData("localhost:6379,user=api-persistent,password=synthetic-only,connectTimeout=not-a-number")]
    [InlineData("localhost:6379,user=api-persistent,password=synthetic-only,unknown=private-option")]
    public void Create_InvalidConfiguration_RefusesWithoutLeakingInput(string? value)
    {
        var error = Should.Throw<InvalidOperationException>(() =>
            RedisClientConfiguration.Create(value, RedisClientIdentity.ApiPersistent));

        error.Message.ShouldBe("Redis configuration for 'api-persistent' is invalid.");
        error.InnerException.ShouldBeNull();
        error.ToString().ShouldNotContain("synthetic-only");
        error.ToString().ShouldNotContain("private-option");
    }

    [Fact]
    public void Create_UnknownIdentity_RefusesBeforeParsingCredentials()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            RedisClientConfiguration.Create("private-input", (RedisClientIdentity)int.MaxValue))
            .ToString().ShouldNotContain("private-input");
    }
}
