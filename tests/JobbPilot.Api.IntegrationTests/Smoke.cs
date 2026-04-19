using Shouldly;

namespace JobbPilot.Api.IntegrationTests;

public class Smoke
{
    [Fact]
    public void Test_project_builds() => true.ShouldBeTrue();
}
