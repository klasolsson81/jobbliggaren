using Jobbliggaren.Infrastructure.Identity;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.IdentityBootstrap;

/// <summary>
/// N-2 anti-regression: <c>IdempotentAdminRoleSeeder</c> catchar bara 42P01
/// (undefined_table) i Development/Test-environment. I prod/staging ska
/// undefined-table-fel bubbla så ECS deployment_circuit_breaker triggar
/// rollback (CLAUDE.md §3.4, §5.1 — fail-loud).
/// </summary>
public class IdempotentAdminRoleSeederTests
{
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Test", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData("DEV", false)] // case-sensitive miljö-namn — bara "Development" exakt
    public void IsSchemaInitGracePeriod_GatesOnEnvironmentName(string envName, bool expected)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(envName);

        var actual = IdempotentAdminRoleSeeder.IsSchemaInitGracePeriod(env);

        actual.ShouldBe(expected);
    }
}
