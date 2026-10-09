using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1979 PR3 — the app version a feedback submission carries is the commit the web image was built
/// from. The feedback BFF reads <c>APP_VERSION</c> at runtime and stamps it (ADR 0156 D2); the opted-in
/// device context is sent only when the page was rendered by that same version, so the version is also
/// what names the consent label the user saw. Two producers keep it true and neither is .NET code:
/// the release workflow's build argument and the web Dockerfile's runtime stage. Text assertions, the
/// form of <see cref="ImageSharpLicenseWorkflowTests"/>.
/// </summary>
public sealed class AppVersionStampWorkflowTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !(File.Exists(Path.Combine(directory.FullName, "Jobbliggaren.sln"))
                    && Directory.Exists(Path.Combine(directory.FullName, "src"))))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not find the repository root.");
    }

    [Fact]
    public void TheReleaseWorkflow_StampsTheReleasedCommit()
    {
        // The released commit is the one the release job checks out (needs.prepare.outputs.sha), so the
        // stamp names the code that actually runs. github.sha would name whatever triggered the run.
        var workflow = File.ReadAllLines(Path.Combine(RepoRoot(), ".github", "workflows", "release-images.yml"));
        workflow.Count(line => line.Trim() == "APP_VERSION=${{ needs.prepare.outputs.sha }}").ShouldBe(1);
    }

    [Fact]
    public void TheWebImage_CarriesTheVersionIntoItsRuntimeStage()
    {
        // An ENV set in the build stage does not survive into the runtime stage's fresh FROM, which is where
        // `node server.js` reads it.
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), "web", "jobbliggaren-web", "Dockerfile"))
            .Select(line => line.Trim()).ToArray();
        var runtime = Array.FindIndex(lines, line => line.StartsWith("FROM ", StringComparison.Ordinal) && line.EndsWith(" AS runtime", StringComparison.Ordinal));
        runtime.ShouldBeGreaterThan(-1, "the web Dockerfile no longer has a stage named runtime");

        var argument = Array.FindIndex(lines, line => line == "ARG APP_VERSION=");
        var environment = Array.FindIndex(lines, line => line == "ENV APP_VERSION=$APP_VERSION");
        argument.ShouldBeGreaterThan(runtime);
        environment.ShouldBeGreaterThan(argument);
        lines.Count(line => line.Contains("APP_VERSION", StringComparison.Ordinal)).ShouldBe(2);
    }
}
