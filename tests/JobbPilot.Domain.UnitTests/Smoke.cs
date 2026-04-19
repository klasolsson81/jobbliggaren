using Shouldly;

namespace JobbPilot.Domain.UnitTests;

public class Smoke
{
    [Fact]
    public void Test_project_builds() => true.ShouldBeTrue();
}
