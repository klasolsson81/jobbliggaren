using Shouldly;

namespace JobbPilot.Application.UnitTests;

public class Smoke
{
    [Fact]
    public void Test_project_builds() => true.ShouldBeTrue();
}
