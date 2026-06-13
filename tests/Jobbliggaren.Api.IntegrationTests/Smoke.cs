using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests;

public class Smoke
{
    [Fact]
    public void Test_project_builds() => true.ShouldBeTrue();
}
