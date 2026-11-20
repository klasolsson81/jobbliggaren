using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1663 PR 3 (security-auditor Minor 1, senior-cto-advisor's ruling of 2026-11-20) — who may write the bell's two flags,
/// <c>ReadInNotificationFeed</c> and <c>DismissedFromNotificationFeed</c>, on any of the three source tables. They are written
/// only by the source classes, each write triggered by the user in the bell or by their own switch (ADR 0168 D1, ADR 0169
/// V20, DPIA 560 §D9 C-D9 point 3): a system writer, such as a job, the scan, the purge or a criterion edit, is Blocker class.
/// A set-based write passes a <c>private set</c>, so this is what makes a second writer, or a job that calls a source's
/// marker, a red build instead of a review comment. A source scan over <c>src/</c>, comments stripped, in the form of
/// <c>JuniorClassificationWriterGuardTests</c>.
/// </summary>
public sealed class NotificationFeedFlagWriterGuardTests
{
    private const string Sources = "Jobbliggaren.Application/NotificationFeed/Sources/";
    private const string MatchSource = Sources + "MatchFeedSource.cs";
    private const string FollowSource = Sources + "FollowedCompanyAdFeedSource.cs";
    private const string CriterionSource = Sources + "CompanyWatchCriterionAdFeedSource.cs";
    private const string Commands = "Jobbliggaren.Application/NotificationFeed/Commands/";
    private const string Endpoints = "Jobbliggaren.Api/Endpoints/MeNotificationFeedEndpoints.cs";

    private const string Flag = @"(ReadInNotificationFeed|DismissedFromNotificationFeed)";

    // (a) A set-based write of either flag stands in a source class and nowhere else.
    [Fact]
    public void Only_the_source_classes_write_a_bell_flag_set_based() =>
        FilesMatching($@"SetProperty\s*\(\s*\w+\s*=>\s*\w+\s*\.\s*{Flag}\b")
            .ShouldBe([CriterionSource, FollowSource, MatchSource]);

    // (b) The follow hit's aggregate methods are called by the follow source alone.
    [Fact]
    public void Only_the_follow_source_calls_the_follow_hits_flag_methods() =>
        FilesMatching(@"\.\s*(MarkReadInNotificationFeed|DismissFromNotificationFeed)\s*\(")
            .ShouldBe([FollowSource]);

    // (c) One-way (ADR 0169 D3'): no writer ever sets a flag back to false.
    [Fact]
    public void No_writer_clears_a_bell_flag() =>
        FilesMatching($@"SetProperty\s*\(\s*\w+\s*=>\s*\w+\s*\.\s*{Flag}\s*,\s*false\s*\)")
            .ShouldBeEmpty();

    // (d) Raw SQL never names the two columns outside the schema's own home.
    [Fact]
    public void Only_the_schema_names_the_bell_flag_columns() =>
        FilesMatching(@"\b(read_in_notification_feed|dismissed_from_notification_feed)\b")
            .Where(path => !path.StartsWith("Jobbliggaren.Infrastructure/Persistence/Migrations/", StringComparison.Ordinal)
                           && !path.StartsWith("Jobbliggaren.Infrastructure/Persistence/Configurations/", StringComparison.Ordinal))
            .ShouldBeEmpty();

    // (e) A source's marker is reached only through the four mark handlers, so no job or other path can call it.
    [Fact]
    public void Only_the_four_mark_handlers_call_a_marker() =>
        FilesMatching(@"\.\s*(MarkRowAsync|DismissRowAsync|MarkThroughAsync|MarkBacklogAsync)\s*\(")
            .ShouldBe(
            [
                Commands + "DismissNotification/DismissNotificationCommandHandler.cs",
                Commands + "MarkNotificationFeedBacklogRead/MarkNotificationFeedBacklogReadCommandHandler.cs",
                Commands + "MarkNotificationRead/MarkNotificationReadCommandHandler.cs",
                Commands + "MarkNotificationsReadThrough/MarkNotificationsReadThroughCommandHandler.cs",
            ]);

    // (f) The four mark commands are sent from the bell's own endpoints and nowhere else: the user's request is the trigger.
    [Fact]
    public void Only_the_bells_endpoints_build_a_mark_command() =>
        FilesMatching(
                @"\bnew\s+(MarkNotificationReadCommand|DismissNotificationCommand|MarkNotificationsReadThroughCommand"
                + @"|MarkNotificationFeedBacklogReadCommand)\s*\(")
            .ShouldBe([Endpoints]);

    private static List<string> FilesMatching(string pattern)
    {
        var srcRoot = Path.Combine(RepoRoot(), "src");
        Directory.Exists(srcRoot).ShouldBeTrue($"src root not found: {srcRoot}");

        return Directory
            .EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(path => (Path: path, Code: StripComments(File.ReadAllText(path))))
            .Where(file => Regex.IsMatch(file.Code, pattern))
            .Select(file => Path.GetRelativePath(srcRoot, file.Path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    private static string StripComments(string code) =>
        Regex.Replace(code, @"//[^\n]*|/\*[\s\S]*?\*/", string.Empty);

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    // thisFile = <repo>/tests/Jobbliggaren.Architecture.Tests/<this file> → up two = repo root.
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
