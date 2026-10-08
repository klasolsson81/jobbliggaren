using System.Text.RegularExpressions;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

public sealed class ImageSharpLicenseWorkflowTests
{
    private const string LicenseReferencePattern =
        "\\bsecrets\\s*(?:\\.\\s*SIXLABORS_LICENSE_KEY\\b|\\[\\s*['\"]SIXLABORS_LICENSE_KEY['\"]\\s*\\])";

    [Fact]
    public void LicensedJobs_ShouldUseProtectedEnvironmentsAndPinnedCheckouts_ForEverySecretConsumer()
    {
        var jobs = ReadWorkflowJobs();
        var licensed = jobs.Where(job => LicenseReferences(job.Source) > 0).ToList();
        licensed.ShouldNotBeEmpty();

        foreach (var job in licensed)
        {
            var environment = Unwrap(Scalar(job.Source, 4, "environment"));
            var steps = Steps(job);
            LicenseReferences(job.Source[..job.Source.IndexOf("    steps:", StringComparison.Ordinal)])
                .ShouldBe(0, job.Identity);
            var checkout = steps.Where(IsCheckout).ShouldHaveSingleItem();
            Scalar(checkout, 8, "with").ShouldBeEmpty(job.Identity);
            var checkoutRef = Unwrap(Scalar(checkout, 10, "ref"));

            if (job.Identity == "release-images.yml/release")
            {
                environment.ShouldBe("sixlabors-main-build");
                AssertMainOnlyGate(Unwrap(Scalar(job.Source, 4, "if")));
                checkoutRef.ShouldBe("needs.prepare.outputs.sha");
            }
            else
            {
                var selector = Regex.Match(environment,
                    @"^(?<gate>.+?)\s*&&\s*'sixlabors-main-build'\s*\|\|\s*'sixlabors-pr-build'$",
                    RegexOptions.CultureInvariant);
                selector.Success.ShouldBeTrue(job.Identity);
                AssertMainOnlyGate(selector.Groups["gate"].Value);
                checkoutRef.ShouldBe("github.sha", job.Identity);
            }

            var licenseSteps = steps.Where(step => LicenseReferences(step) > 0).ToList();
            licenseSteps.ShouldNotBeEmpty(job.Identity);
            foreach (var step in licenseSteps)
            {
                Scalar(step, 8, "env").ShouldBeEmpty(job.Identity);
                var value = Scalar(step, 10, "SIXLABORS_LICENSE_KEY");
                LicenseReferences(value).ShouldBe(LicenseReferences(step), job.Identity);
                var expression = Unwrap(value);
                if (expression == "secrets.SIXLABORS_LICENSE_KEY")
                    continue;
                var matrix = Regex.Match(expression,
                    @"^\((?<images>matrix\.name\s*==\s*'[a-z]+'(?:\s*\|\|\s*matrix\.name\s*==\s*'[a-z]+')*)\)\s*&&\s*secrets\.SIXLABORS_LICENSE_KEY\s*\|\|\s*''$",
                    RegexOptions.CultureInvariant);
                matrix.Success.ShouldBeTrue(job.Identity);
                QuotedValues(matrix.Groups["images"].Value).ShouldBe(["api", "worker", "migrate"], ignoreOrder: true);
            }
        }
    }

    [Fact]
    public void ReleaseLicenseJob_ShouldRejectInputRefsAndDifferentPreparedCommits_BeforeTheLicensedBuild()
    {
        var jobs = ReadWorkflowJobs();
        var prepare = jobs.Single(job => job.Identity == "release-images.yml/prepare");
        var release = jobs.Single(job => job.Identity == "release-images.yml/release");
        LicenseReferences(release.Source).ShouldBeGreaterThan(0);
        Scalar(release.Source, 4, "environment").ShouldBe("sixlabors-main-build");
        AssertMainOnlyGate(Unwrap(Scalar(release.Source, 4, "if")));
        Scalar(release.Source, 4, "needs").ShouldBe("prepare");
        Unwrap(Scalar(Steps(prepare).Where(IsCheckout).ShouldHaveSingleItem(), 10, "ref")).ShouldBe("github.sha");
        var steps = Steps(release);
        var checkoutIndex = steps.FindIndex(IsCheckout);
        checkoutIndex.ShouldBeGreaterThanOrEqualTo(0);
        Unwrap(Scalar(steps[checkoutIndex], 10, "ref")).ShouldBe("needs.prepare.outputs.sha");
        const string refusalPattern =
            "if\\s+!\\s*\\{\\s*\\[\\s*\"\\$\\(git rev-parse HEAD\\)\"\\s*=\\s*\"\\$GITHUB_SHA\"\\s*\\]\\s*&&\\s*\\[\\s*\"\\$PREPARED\"\\s*=\\s*\"\\$GITHUB_SHA\"\\s*\\];\\s*\\};\\s*then(?<refusal>.*?)\\bfi\\b";
        var guardIndex = steps.FindIndex(step => Regex.IsMatch(step, refusalPattern,
            RegexOptions.Singleline | RegexOptions.CultureInvariant));
        guardIndex.ShouldBeGreaterThan(checkoutIndex);
        Unwrap(Scalar(steps[guardIndex], 10, "PREPARED")).ShouldBe("needs.prepare.outputs.sha");
        var refusal = Regex.Match(steps[guardIndex], refusalPattern,
            RegexOptions.Singleline | RegexOptions.CultureInvariant).Groups["refusal"].Value;
        Regex.IsMatch(refusal, @"(?m)^\s*exit\s+1\s*$", RegexOptions.CultureInvariant).ShouldBeTrue();
        var firstLicensedStep = steps.FindIndex(step => LicenseReferences(step) > 0);
        firstLicensedStep.ShouldBeGreaterThan(guardIndex);
    }

    private static void AssertMainOnlyGate(string expression)
    {
        var gate = Regex.Match(expression,
            @"^github\.ref\s*==\s*'refs/heads/main'\s*&&\s*\(\s*(?<events>github\.event_name\s*==\s*'[a-z_]+'(?:\s*\|\|\s*github\.event_name\s*==\s*'[a-z_]+')*)\s*\)$",
            RegexOptions.CultureInvariant);
        gate.Success.ShouldBeTrue("main access requires the literal main ref and an explicit event allow-list");
        QuotedValues(gate.Groups["events"].Value).ShouldBe(["push", "schedule", "workflow_dispatch"], ignoreOrder: true);
    }

    private static List<string> QuotedValues(string source) =>
        Regex.Matches(source, @"'([^']*)'", RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Value).ToList();

    private static int LicenseReferences(string source) =>
        Regex.Count(source, LicenseReferencePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsCheckout(string step) =>
        Regex.IsMatch(step, @"(?m)^ {6,8}(?:- )?uses:[ \t]*actions/checkout@", RegexOptions.CultureInvariant);

    private static string Scalar(string source, int indent, string key)
    {
        var matches = Regex.Matches(source,
            "(?m)^" + new string(' ', indent) + Regex.Escape(key) + @":[ \t]*(?<value>[^\n]*)$",
            RegexOptions.CultureInvariant);
        matches.Count.ShouldBe(1, $"expected one {key} at indentation {indent}");
        return matches[0].Groups["value"].Value.Trim();
    }

    private static string Unwrap(string value) =>
        value.StartsWith("${{", StringComparison.Ordinal) && value.EndsWith("}}", StringComparison.Ordinal)
            ? value[3..^2].Trim() : value;

    private static List<string> Steps(WorkflowJob job)
    {
        var section = Regex.Matches(job.Source, @"(?m)^ {4}steps:[ \t]*$", RegexOptions.CultureInvariant);
        section.Count.ShouldBe(1, job.Identity);
        var source = job.Source[(section[0].Index + section[0].Length)..];
        var starts = Regex.Matches(source, @"(?m)^ {6}-[ \t]+", RegexOptions.CultureInvariant);
        starts.Count.ShouldBeGreaterThan(0, job.Identity);
        var steps = new List<string>();
        for (var index = 0; index < starts.Count; index++)
        {
            var end = index + 1 < starts.Count ? starts[index + 1].Index : source.Length;
            steps.Add(source[starts[index].Index..end]);
        }
        return steps;
    }

    private static List<WorkflowJob> ReadWorkflowJobs()
    {
        var jobs = new List<WorkflowJob>();
        var directory = Path.Combine(RepositoryRoot(), ".github", "workflows");
        foreach (var file in Directory.GetFiles(directory).Where(path => Path.GetExtension(path) is ".yml" or ".yaml"))
        {
            var source = string.Join('\n', File.ReadAllLines(file).Select(StripComment));
            if (LicenseReferences(source) == 0)
                continue;
            var section = Regex.Matches(source, @"(?m)^jobs:[ \t]*$", RegexOptions.CultureInvariant);
            section.Count.ShouldBe(1, file);
            var body = source[(section[0].Index + section[0].Length)..];
            var nextRoot = Regex.Match(body, @"(?m)^[A-Za-z_][A-Za-z0-9_-]*:", RegexOptions.CultureInvariant);
            if (nextRoot.Success)
                body = body[..nextRoot.Index];
            var starts = Regex.Matches(body, @"(?m)^ {2}(?<id>[A-Za-z_][A-Za-z0-9_-]*):[ \t]*$",
                RegexOptions.CultureInvariant);
            var workflowJobs = new List<WorkflowJob>();
            for (var index = 0; index < starts.Count; index++)
            {
                var end = index + 1 < starts.Count ? starts[index + 1].Index : body.Length;
                workflowJobs.Add(new WorkflowJob(Path.GetFileName(file) + "/" + starts[index].Groups["id"].Value,
                    body[starts[index].Index..end]));
            }
            workflowJobs.Sum(job => LicenseReferences(job.Source)).ShouldBe(LicenseReferences(source),
                $"{file}: a license reference lies outside a recognized job");
            jobs.AddRange(workflowJobs);
        }
        return jobs;
    }

    private static string StripComment(string line)
    {
        var quote = '\0';
        for (var index = 0; index < line.Length; index++)
        {
            var value = line[index];
            if (quote != '\0')
            {
                if (quote == '"' && value == '\\')
                    index++;
                else if (value == quote)
                {
                    if (quote == '\'' && index + 1 < line.Length && line[index + 1] == '\'')
                        index++;
                    else
                        quote = '\0';
                }
            }
            else if (value is '\'' or '"')
                quote = value;
            else if (value == '#' && (index == 0 || char.IsWhiteSpace(line[index - 1])))
                return line[..index];
        }
        return line;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !(File.Exists(Path.Combine(directory.FullName, "Jobbliggaren.sln"))
                    && Directory.Exists(Path.Combine(directory.FullName, "src"))))
            directory = directory.Parent;
        directory.ShouldNotBeNull();
        return directory.FullName;
    }

    private sealed record WorkflowJob(string Identity, string Source);
}
