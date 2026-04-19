using Shouldly;

namespace JobbPilot.Architecture.Tests;

public class Smoke
{
    [Fact]
    public void Test_project_builds() => true.ShouldBeTrue();
}
