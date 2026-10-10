using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.Admin.Backup;
using Jobbliggaren.Application.Admin.Backup.Queries.GetBackupStatus;
using NetArchTest.Rules;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1982 (ADR 0157) — the host sampler that publishes the Backup card's data, and the constants the
/// shell and the C# must agree on. Like <see cref="BackupUnitFilePinTests"/>, these are textual pins on
/// files a test suite cannot run: a unit systemd reads on a host this project cannot reach.
///
/// <para>
/// Every cross-language equality here exists because it has one failure mode: a number or a name changed
/// on one side, both suites green, and the card wrong in production. 26 hours is the backup script's own
/// threshold; a stamp path is the one place the backup writes it; a user name is the one the unit runs as.
/// </para>
/// </summary>
public class ObserveUnitFilePinTests
{
    private const string SystemdDirectory = "deploy/systemd";
    private const string Runbook = "docs/runbooks/host-observations.md";
    private const string ObservationsPath = "/run/jobbliggaren/observations";
    private const string SamplerUser = "jbl-observe";

    // ---- the set ----

    [Fact]
    public void TheSamplerFilesAreExactlyThese_SoAnAdditionCannotArriveUnpinned()
    {
        string[] expected =
        [
            "jobbliggaren-observe-backup.sh",
            "jobbliggaren-observe-lib.sh",
            "jobbliggaren-observe-sysusers.conf",
            "jobbliggaren-observe-tmpfiles.conf",
            "jobbliggaren-observe.service",
            "jobbliggaren-observe.test.sh",
            "jobbliggaren-observe.timer",
        ];

        Directory.GetFiles(Path.Combine(RepositoryRoot(), SystemdDirectory), "jobbliggaren-observe*")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray()
            .ShouldBe(expected,
                "the sampler's file set changed. A new collector needs its own suite step in build.yml, " +
                "this test's cases, and a contract-version review by security-auditor (ADR 0157).");
    }

    // ---- the timer ----

    [Fact]
    public void Timer_FiresEveryMinute_AndNeverCatchesUp()
    {
        var timer = ReadUnit("jobbliggaren-observe.timer");

        DirectiveOf(timer, "OnCalendar").ShouldBe("minutely");
        DirectiveOf(timer, "WantedBy").ShouldBe("timers.target");
        Directives(timer).Any(line => line.StartsWith("Persistent=", StringComparison.Ordinal)).ShouldBeFalse(
            "a catch-up run would publish a sample taken later than the interval it stands for.");
        Directives(timer).Any(line => line.StartsWith("OnUnitActiveSec=", StringComparison.Ordinal)).ShouldBeFalse(
            "a Type=oneshot unit never becomes active, so OnUnitActiveSec would neither re-fire nor clear (systemd#21600).");
    }

    // ---- the service ----

    [Fact]
    public void Service_RunsTheCollectorAsAnUnprivilegedUser_WithNoInstallSection()
    {
        var service = ReadUnit("jobbliggaren-observe.service");

        DirectiveOf(service, "Type").ShouldBe("oneshot");
        DirectiveOf(service, "User").ShouldBe(SamplerUser);
        DirectiveOf(service, "Group").ShouldBe(SamplerUser);
        Directives(service).Any(line => line == "[Install]").ShouldBeFalse(
            "the timer is the only activation path; an [Install] here would also run the sampler at every boot.");

        var execStart = DirectiveOf(service, "ExecStart");
        execStart.ShouldBe("/opt/jobbliggaren/" + SystemdDirectory + "/jobbliggaren-observe-backup.sh");
        File.Exists(Path.Combine(RepositoryRoot(), SystemdDirectory, "jobbliggaren-observe-backup.sh")).ShouldBeTrue();
    }

    [Fact]
    public void Service_IsSandboxed_ToWriteOnlyItsOwnDirectory_AndReachNothingSecret()
    {
        var service = ReadUnit("jobbliggaren-observe.service");

        // `-` on the path lists: a path that does not exist yet must not fail the unit during namespace setup.
        DirectiveOf(service, "ReadWritePaths").ShouldBe("-" + ObservationsPath);
        DirectiveOf(service, "ProtectSystem").ShouldBe("strict");
        DirectiveOf(service, "ProtectHome").ShouldBe("yes");
        DirectiveOf(service, "PrivateTmp").ShouldBe("yes");
        DirectiveOf(service, "PrivateNetwork").ShouldBe("yes");
        DirectiveOf(service, "RestrictAddressFamilies").ShouldBe("AF_UNIX");
        DirectiveOf(service, "CapabilityBoundingSet").ShouldBe("", "an empty bounding set drops every capability");
        DirectiveOf(service, "NoNewPrivileges").ShouldBe("yes");
        DirectiveOf(service, "ProtectProc").ShouldBe("invisible");

        var inaccessible = DirectiveOf(service, "InaccessiblePaths").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        inaccessible.ShouldContain("-/run/jobbliggaren/host-secrets", "the backup upload credential is never the sampler's to read");
        inaccessible.ShouldContain("-/run/jobbliggaren/secrets");
        inaccessible.ShouldContain("-/etc/jobbliggaren");
    }

    [Fact]
    public void Service_TimeoutIsAbove_TheCollectorsOwnBoundOnSystemctl()
    {
        var timeout = int.Parse(DirectiveOf(ReadUnit("jobbliggaren-observe.service"), "TimeoutStartSec"), CultureInfo.InvariantCulture);
        var bound = ReadShellConstant("jobbliggaren-observe-backup.sh", "SYSTEMCTL_TIMEOUT_SECONDS");

        int.Parse(bound, CultureInfo.InvariantCulture).ShouldBeLessThan(timeout,
            "a hung bus must end as `unknown` inside the collector, never as a killed unit on `systemctl --failed`.");
    }

    [Theory]
    [InlineData("jobbliggaren-observe.service")]
    [InlineData("jobbliggaren-observe.timer")]
    public void Units_DocumentationPointsAtARunbookThatExists(string fileName)
    {
        DirectiveOf(ReadUnit(fileName), "Documentation").EndsWith(Runbook, StringComparison.Ordinal).ShouldBeTrue();
        File.Exists(Path.Combine(RepositoryRoot(), Runbook)).ShouldBeTrue(
            $"{fileName} documents {Runbook}, which does not exist.");
    }

    // ---- the directory, the user and the lines that must agree ----

    [Fact]
    public void TheDirectoryAndTheUser_AreTheSameInTheLibrary_TheUnitTheTmpfilesLineAndTheSysusersLine()
    {
        ReadShellConstant("jobbliggaren-observe-lib.sh", "OBSERVATIONS_DIR").ShouldBe(ObservationsPath);

        var tmpfiles = ReadText("jobbliggaren-observe-tmpfiles.conf");
        Directives(tmpfiles).ShouldBe([$"d {ObservationsPath} 0755 {SamplerUser} {SamplerUser} -"],
            "the directory is owned by the unprivileged sampler user, world-readable, and nothing else is created here.");

        Directives(ReadText("jobbliggaren-observe-sysusers.conf")).ShouldBe(
            [$"u {SamplerUser} - \"Jobbliggaren host sampler\" /nonexistent /usr/sbin/nologin"],
            "a system account with no shell and no home, and no other account in this file.");
    }

    [Fact]
    public void TheSamplerNeverTouchesAnythingSecret()
    {
        // A tripwire for the transport's content rule (ADR 0157): metadata fit for any admin to read.
        // A collector that needs one of these is a new contract version, and security-auditor reviews it.
        string[] forbidden = ["host-secrets", "docker", "rclone", "Backup__", "age.recipient", ".env", "/run/jobbliggaren/secrets"];

        foreach (var file in new[] { "jobbliggaren-observe-backup.sh", "jobbliggaren-observe-lib.sh" })
        {
            var code = string.Join('\n', Directives(ReadText(file)));
            foreach (var word in forbidden)
            {
                code.Contains(word, StringComparison.Ordinal).ShouldBeFalse($"{file} must not mention '{word}'");
            }
        }
    }

    [Fact]
    public void TheSamplerIsNotAFloorTimerOfTheHeartbeat_AndTheHeartbeatSaysWhy()
    {
        var heartbeat = ReadText("jobbliggaren-heartbeat.sh");
        var floor = Regex.Match(heartbeat, @"^readonly FLOOR_TIMERS=""([^""]*)""", RegexOptions.Multiline);

        floor.Success.ShouldBeTrue();
        floor.Groups[1].Value.Contains("jobbliggaren-observe.timer", StringComparison.Ordinal).ShouldBeFalse(
            "the card is the sampler's reader and shows a stopped sampler as an old observation; paging on it would " +
            "light the one alarm surface for a cosmetic unit.");
        heartbeat.Contains("jobbliggaren-observe.timer", StringComparison.Ordinal).ShouldBeTrue(
            "the heartbeat's floor-timer comment must name the sampler and why it is not on the list.");
    }

    // ---- shell and C# agree ----

    [Fact]
    public void TheCollectorReadsTheStampThatTheBackupScriptWrites()
    {
        var collector = ReadShellConstant("jobbliggaren-observe-backup.sh", "STAMP_FILE");

        collector.ShouldBe(ReadShellConstant("jobbliggaren-backup.sh", "STAMP_FILE"));
    }

    [Fact]
    public void TheCollectorAsksAboutTheTimerThatTheBackupUnitSetShips()
    {
        var unit = ReadShellConstant("jobbliggaren-observe-backup.sh", "TIMER_UNIT");

        File.Exists(Path.Combine(RepositoryRoot(), SystemdDirectory, unit)).ShouldBeTrue(
            $"the collector asks systemd about {unit}, which this repository does not ship.");
    }

    [Fact]
    public void TheCollectorsFailureToken_IsTheOneTheGoldenErrorFileCarries()
    {
        var call = Regex.Match(ReadText("jobbliggaren-observe-backup.sh"), @"^\s*observe_publish_error backup (\S+)", RegexOptions.Multiline);
        call.Success.ShouldBeTrue("the collector no longer calls `observe_publish_error backup <token>`; this parse reads exactly that.");

        ReadText("fixtures/observations/backup-error.json").ShouldContain($"\"error\":\"{call.Groups[1].Value}\"");
    }

    /// <summary>
    /// The browser-facing schema spells the API's tokens by hand, and a misspelt member makes that state fail
    /// its parse (the card reads "failed" for good) with every other suite green. The C# enums are the source.
    /// </summary>
    [Fact]
    public void TheWebSchemaSpellsTheSameTokensAsTheEnums_OfTheBackupResponse()
    {
        var schema = File.ReadAllText(Path.Combine(RepositoryRoot(), "web/jobbliggaren-web/src/lib/dto/admin-overview.ts"));
        var response = schema[schema.IndexOf("export const backupStatusResponseSchema", StringComparison.Ordinal)..];
        response = response[..response.IndexOf("export type BackupStatusResponse", StringComparison.Ordinal)];

        TokensIn(Regex.Match(schema, @"const backupReason = z\.enum\(\[(.*?)\]\)", RegexOptions.Singleline).Groups[1].Value)
            .ShouldBe(Enum.GetNames<BackupStatusReason>().Order(StringComparer.Ordinal));

        var statuses = Regex.Matches(response, @"status: z\.literal\(""(\w+)""\)").Select(m => m.Groups[1].Value);
        statuses.Order(StringComparer.Ordinal).ShouldBe(Enum.GetNames<BackupStatus>().Order(StringComparer.Ordinal));

        var lastSuccess = response[response.IndexOf("lastSuccess: z.discriminatedUnion", StringComparison.Ordinal)..response.IndexOf("timer: z.discriminatedUnion", StringComparison.Ordinal)];
        StatesIn(lastSuccess).ShouldBe(Enum.GetNames<BackupLastSuccessState>().Order(StringComparer.Ordinal));

        var timer = response[response.IndexOf("timer: z.discriminatedUnion", StringComparison.Ordinal)..response.IndexOf("status: z.literal(\"NotObserved\")", StringComparison.Ordinal)];
        StatesIn(timer).ShouldBe(Enum.GetNames<BackupTimerState>().Order(StringComparer.Ordinal));

        static IEnumerable<string> TokensIn(string quoted) =>
            Regex.Matches(quoted, @"""(\w+)""").Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal);

        static IEnumerable<string> StatesIn(string text) =>
            Regex.Matches(text, @"state: z\.literal\(""(\w+)""\)").Select(m => m.Groups[1].Value)
                .Concat(Regex.Matches(text, @"state: z\.enum\(\[(.*?)\]\)").SelectMany(m => TokensIn(m.Groups[1].Value)))
                .Order(StringComparer.Ordinal);
    }

    [Fact]
    public void Overdue_IsTheBackupScriptsOwnThreshold()
    {
        var expression = Regex.Match(
            ReadText("jobbliggaren-backup.sh"),
            @"^readonly MAX_STAMP_AGE_SECONDS=\$\(\(\s*(\d+)\s*\*\s*(\d+)\s*\)\)", RegexOptions.Multiline);
        expression.Success.ShouldBeTrue("the backup script no longer spells MAX_STAMP_AGE_SECONDS as `N * M`; update this parse with it.");

        var seconds = long.Parse(expression.Groups[1].Value, CultureInfo.InvariantCulture)
                      * long.Parse(expression.Groups[2].Value, CultureInfo.InvariantCulture);

        BackupStatusEvaluator.BackupOverdueAfter.ShouldBe(TimeSpan.FromSeconds(seconds),
            "the card calls a run overdue by the same threshold the box's own `--check` uses.");
    }

    [Fact]
    public void ARunMayTakeLongerInTheEvaluator_ThanTheBackupUnitAllowsItself()
    {
        var timeout = int.Parse(DirectiveOf(ReadUnit("jobbliggaren-backup.service"), "TimeoutStartSec"), CultureInfo.InvariantCulture);

        BackupStatusEvaluator.MaxRunSpan.ShouldBeGreaterThan(TimeSpan.FromSeconds(timeout),
            "a real, successful run that used its whole timeout must not read as an invalid stamp.");
    }

    [Fact]
    public void ObservationsAreStaleAfterTheSameFiveMinutesTheWebAppUses()
    {
        var overview = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "web", "jobbliggaren-web", "src", "lib", "admin", "overview.ts"));
        var match = Regex.Match(overview, @"export const OVERVIEW_STALE_MS = (\d+) \* (\d+)_(\d+);");
        match.Success.ShouldBeTrue("overview.ts no longer spells OVERVIEW_STALE_MS as `N * M_MMM`; update this parse with it.");

        var milliseconds = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)
                           * long.Parse(match.Groups[2].Value + match.Groups[3].Value, CultureInfo.InvariantCulture);

        BackupStatusEvaluator.ObservationStaleAfter.ShouldBe(TimeSpan.FromMilliseconds(milliseconds));
    }

    // ---- the port ----

    [Fact]
    public void TheBackupSampleSource_IsInjectedOnlyIntoItsOneQueryHandler()
    {
        const BindingFlags declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        Assembly[] owned =
        [
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly,
            typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly,
            typeof(Jobbliggaren.Api.Endpoints.AdminJobAdsEndpoints).Assembly,
            typeof(Jobbliggaren.Worker.Auditing.WorkerSystemUser).Assembly,
            typeof(Jobbliggaren.Migrate.ConnectionStringFactory).Assembly,
        ];

        var consumers = owned
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetConstructors(declared).Cast<MethodBase>().Concat(type.GetMethods(declared))
                .Any(member => member.GetParameters().Any(parameter => parameter.ParameterType == typeof(IBackupSampleSource))))
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToList();

        consumers.ShouldBe([typeof(GetBackupStatusQueryHandler).FullName!],
            "a new reader of the host bridge sees what the sampler publishes; it needs the admin gate and ADR 0157's review. Found: "
            + string.Join(", ", consumers));
    }

    [Fact]
    public void TheBackupApplicationNamespace_DoesNotTouchTheFilesystemOrParseJson()
    {
        var result = Types.InAssembly(typeof(Jobbliggaren.Application.AssemblyMarker).Assembly)
            .That().ResideInNamespaceStartingWith("Jobbliggaren.Application.Admin.Backup")
            .ShouldNot().HaveDependencyOnAny(
                "System.IO.File", "System.IO.FileInfo", "System.IO.Directory", "System.IO.FileStream",
                "System.Text.Json.JsonDocument", "System.Text.Json.JsonElement")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            "the Application layer judges a sample; reading and parsing the host's file is Infrastructure's. Found: "
            + string.Join(", ", result.FailingTypeNames ?? []));
    }

    // ---- reading ----

    private static string ReadText(string fileName)
    {
        var path = Path.Combine(RepositoryRoot(), SystemdDirectory, fileName);
        File.Exists(path).ShouldBeTrue($"expected a file at {SystemdDirectory}/{fileName}");
        return File.ReadAllText(path);
    }

    private static string ReadUnit(string fileName) => ReadText(fileName);

    /// <summary>A comment-free, blank-free view: absence assertions must never run against prose that explains the absence.</summary>
    private static List<string> Directives(string text) =>
        text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')).ToList();

    private static string DirectiveOf(string unitText, string directive)
    {
        var matches = Directives(unitText)
            .Where(line => line.StartsWith(directive + "=", StringComparison.Ordinal))
            .Select(line => line[(directive.Length + 1)..].Trim())
            .ToList();

        matches.Count.ShouldBe(1, $"expected exactly one uncommented `{directive}=` line, found {matches.Count}.");
        return matches[0];
    }

    private static string ReadShellConstant(string fileName, string name)
    {
        var match = Regex.Match(ReadText(fileName), $@"^readonly {name}=(\S+)\s*$", RegexOptions.Multiline);
        match.Success.ShouldBeTrue($"{fileName} no longer declares `readonly {name}=<literal>`; this parse reads exactly that.");
        return match.Groups[1].Value;
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null
               && !(File.Exists(Path.Combine(dir.FullName, "Jobbliggaren.sln"))
                    && Directory.Exists(Path.Combine(dir.FullName, "src"))))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("could not locate the repository root from " + AppContext.BaseDirectory);
        return dir.FullName;
    }
}
