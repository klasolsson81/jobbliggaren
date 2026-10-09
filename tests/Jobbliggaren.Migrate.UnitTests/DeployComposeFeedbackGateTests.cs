using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// Pins that the deploy stack keeps feedback CLOSED unless an operator opens it (#1979, ADR 0156 D4).
///
/// <para>
/// <c>FeedbackOptions</c>' own default is <see langword="false"/> and the gate refuses an empty
/// recipient, but neither sees what the box feeds the containers: an interpolation default here.
/// Flip <c>${FEEDBACK_ENABLED:-false}</c> to <c>:-true</c> and nothing goes red, while the box opens
/// submissions the moment a recipient is set. Opening is Klas's GO and a <c>.env</c> write
/// (ADR 0154 §4), so it stays an act, never a default.
/// </para>
///
/// <para>
/// The forward-scanning form of <see cref="DeployComposeIngestGateTests"/>: anchor on the service key,
/// then assert position inside that service's block. Text assertions because a compose file is not
/// .NET configuration and the value under test is a literal.
/// </para>
/// </summary>
public class DeployComposeFeedbackGateTests
{
    private const string SwitchKey = "Feedback__Enabled:";
    private const string RecipientKey = "Feedback__NotificationRecipient:";
    private const string MergePrefix = "<<: [";
    private const string Anchor = "*app-feedback";

    private static string[] ComposeLines =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "deploy", "docker-compose.yml")).Split('\n');

    private static string[] EnvTemplateLines =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "deploy", ".env.example")).Split('\n');

    private static bool IsTwoSpaceKey(string line) =>
        line.Length > 2 && line.StartsWith("  ", StringComparison.Ordinal) && line[2] != ' ' && line.TrimEnd().EndsWith(':');

    /// <summary>The line range of one service's block: from its key to the next two-space key.</summary>
    private static (int Start, int End) ServiceBlock(string[] lines, string service)
    {
        var start = Array.FindIndex(lines, l => l.TrimEnd() == $"  {service}:");
        start.ShouldBeGreaterThan(-1, $"the compose file no longer declares a `{service}` service");
        var end = Array.FindIndex(lines, start + 1, IsTwoSpaceKey);
        return (start, end < 0 ? lines.Length : end);
    }

    [Fact]
    public void FeedbackSwitch_DefaultsToClosed_WhenTheBoxSetsNothing()
    {
        var matches = ComposeLines.Where(l => l.Contains(SwitchKey, StringComparison.Ordinal)).ToList();
        matches.Count.ShouldBe(1, "deploy/docker-compose.yml must carry exactly one feedback-switch line.");
        matches[0].Trim().ShouldBe($"{SwitchKey} ${{FEEDBACK_ENABLED:-false}}",
            customMessage: "The switch must default CLOSED; opening feedback is an operator's .env write with Klas's GO.");
    }

    [Fact]
    public void FeedbackSwitch_ReachesTheApiOnly()
    {
        // The worker's dispatch does not read the switch, which is what lets turning it off leave queued
        // notices going out. In an anchor it would also reach the worker, which nothing there consumes.
        var lines = ComposeLines;
        var (apiStart, apiEnd) = ServiceBlock(lines, "api");
        var at = Array.FindIndex(lines, l => l.Contains(SwitchKey, StringComparison.Ordinal));
        at.ShouldBeInRange(apiStart, apiEnd - 1, $"'{SwitchKey}' must sit inside the api service block.");
    }

    [Fact]
    public void FeedbackRecipient_DefaultsToEmpty_AndIsDeclaredOnce()
    {
        // A non-empty default would put an address in the deploy file and, with the switch on, open feedback
        // and mail it; never `:?`, which would stop the hourly apply on a box that has not set it.
        var matches = ComposeLines.Where(l => l.Contains(RecipientKey, StringComparison.Ordinal)).ToList();
        matches.Count.ShouldBe(1, "The recipient must be declared once, in the x-app-feedback anchor.");
        matches[0].Trim().ShouldBe($"{RecipientKey} ${{FEEDBACK_NOTIFICATION_RECIPIENT:-}}");
    }

    [Theory]
    [InlineData("api")]
    [InlineData("worker")]
    public void FeedbackRecipient_ReachesBothHosts(string service)
    {
        // The api's gate opens submissions only with a recipient; the worker's dispatch sends only to one.
        var lines = ComposeLines;
        var (start, end) = ServiceBlock(lines, service);
        var merge = lines[start..end].Select(l => l.Trim())
            .Where(l => l.StartsWith(MergePrefix, StringComparison.Ordinal)).ShouldHaveSingleItem();
        merge.TrimStart('<', ':', ' ', '[').TrimEnd(']').Split(',').Select(a => a.Trim())
            .Count(a => a == Anchor).ShouldBe(1, $"The `{service}` environment must merge the x-app-feedback anchor.");
    }

    [Fact]
    public void EnvTemplate_DocumentsBothKeys_Commented()
    {
        // The template documents the knobs without opening anything: a live line here would be a default.
        var lines = EnvTemplateLines.Select(l => l.TrimEnd('\r')).ToArray();
        lines.ShouldContain("#FEEDBACK_ENABLED=true");
        lines.ShouldContain("#FEEDBACK_NOTIFICATION_RECIPIENT=");
        lines.Where(l => l.StartsWith("FEEDBACK_", StringComparison.Ordinal)).ShouldBeEmpty();
    }
}
