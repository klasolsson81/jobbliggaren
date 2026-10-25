using System.Text.RegularExpressions;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// #1981 PR 4 — the DMARC mailbox password reaches the worker and nothing else. It logs in to the mailbox the
/// worker's DMARC ingestion reads and api never does, so the pointer is the worker's alone, and the file lies in a
/// host directory bound into the worker and no other service, never in the secrets directory api shares
/// (security-auditor 2026-10-24, ruling 2).
///
/// <para>
/// The mount carries <c>create_host_path: false</c>, so a box the password never reached starts the worker only
/// because something creates that directory first: the reconcile before every apply, the tmpfiles line at boot, the
/// injection on every run. The last pin holds the four to one path.
/// </para>
/// </summary>
public class DeployComposeDmarcMailboxTests
{
    private const string ComposePath = "deploy/docker-compose.yml";
    private const string Pointer = "Dmarc__Mailbox__Password_FILE";
    private const string Variable = "DMARC_MAILBOX_PASSWORD_FILE";
    private const string PasswordFile = "Dmarc__Mailbox__Password";
    private const string Source = "/run/jobbliggaren/worker-secrets";
    private const string Target = "/run/worker-secrets";

    private static ComposeFile Compose => new(ComposePath);

    private static string[] Read(string relativePath) =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, relativePath));

    private static List<(string Line, int Index)> Uncommented(string[] lines) =>
        lines.Select((line, index) => (Line: line, Index: index)).Where(l => !l.Line.TrimStart().StartsWith('#')).ToList();

    private static string UncommentedText(IEnumerable<string> block) =>
        string.Join('\n', block.Where(l => !l.TrimStart().StartsWith('#')));

    [Fact]
    public void ThePointer_DefaultsToEmpty_AndOnlyTheWorkerBlockCarriesIt()
    {
        var lines = Read(ComposePath);
        var worker = Array.FindIndex(lines, l => l.TrimEnd() == "  worker:");
        var next = Array.FindIndex(lines, worker + 1, l => l.Length > 2 && l.StartsWith("  ", StringComparison.Ordinal)
                                                            && !l.StartsWith("   ", StringComparison.Ordinal)
                                                            && !l.TrimStart().StartsWith('#')
                                                            && l.TrimEnd().EndsWith(':'));
        worker.ShouldBeGreaterThan(-1, "no worker service header");
        next.ShouldBeGreaterThan(worker, "no service header after worker");

        // Across the whole file, the x-* anchors included, and in any case: an anchor api merges would carry it there,
        // and a second DMARC key (a username, a host) is configuration only a commit may change (CTO conflict 11).
        var carriers = Uncommented(lines)
            .Where(l => l.Line.Contains("dmarc", StringComparison.OrdinalIgnoreCase))
            .ToList();

        carriers.Count.ShouldBe(1);
        carriers[0].Index.ShouldBeInRange(worker + 1, next - 1);
        carriers[0].Line.Trim().ShouldBe($"{Pointer}: ${{{Variable}:-}}", "empty by default and never `:?`");
    }

    [Fact]
    public void TheWorker_MountsTheDirectory_ReadOnly_AndNeverCreatesItThroughDocker()
    {
        var mounts = ComposeMounts.Of(Compose.ServiceBlock("worker"))
            .Where(m => ComposeMounts.SourceOf(m) == Source)
            .ToList();

        mounts.Count.ShouldBe(1, $"the worker must mount {Source} exactly once");
        mounts[0].ShouldContain("type: bind");
        mounts[0].ShouldContain($"target: {Target}");
        mounts[0].ShouldContain("read_only: true", "the worker reads the password and can never write to it");
        mounts[0].ShouldContain("create_host_path: false",
            "a secret directory Docker created would be 0755 rather than closed; the reconcile, tmpfiles and the injection create it");
    }

    [Fact]
    public void TheApi_HasNeitherThePointerNorTheMount()
    {
        var api = UncommentedText(Compose.ServiceBlock("api"));

        api.Contains("dmarc", StringComparison.OrdinalIgnoreCase).ShouldBeFalse("api never reads the mailbox password");
        api.Contains("worker-secrets", StringComparison.Ordinal).ShouldBeFalse("api must not mount the worker-only directory");
    }

    [Fact]
    public void NoOtherBindSource_IsTheDirectory_ItsParent_OrItsChild()
    {
        // Every host path the file binds, the x-* anchors included, so the alias migrate-rewrap mounts through
        // is read as well: long syntax (`source: /host`) and short (`/host:/container`).
        var sources = Uncommented(Read(ComposePath))
            .SelectMany(l => LongSource.Matches(l.Line).Concat(ShortSource.Matches(l.Line)))
            .Select(m => m.Groups["path"].Value.TrimEnd('/'))
            .ToList();

        sources.ShouldContain("/run/jobbliggaren/secrets", "the reader must see the shared secrets mount, or it sees nothing");
        sources.ShouldContain("/run/jobbliggaren/api-secrets");
        sources.Count(path => path == Source).ShouldBe(1, "only the worker's own mount binds the directory");
        foreach (var path in sources.Where(path => path != Source))
        {
            Source.StartsWith(path + "/", StringComparison.Ordinal)
                .ShouldBeFalse($"{path} is a parent of {Source}, so whatever mounts it reaches the mailbox password");
            path.StartsWith(Source + "/", StringComparison.Ordinal).ShouldBeFalse($"{path} lies inside {Source}");
        }

        Compose.ServiceBlock("worker").Count(l => !l.TrimStart().StartsWith('#') && l.Contains(Source, StringComparison.Ordinal))
            .ShouldBe(1, "the one bind of the directory is the worker's");
        var others = ComposeMounts.ServiceNames(ComposePath).Where(name => name != "worker").ToList();
        others.ShouldContain("api");
        foreach (var service in others)
        {
            UncommentedText(Compose.ServiceBlock(service)).Contains("worker-secrets", StringComparison.Ordinal)
                .ShouldBeFalse($"{service} must not mount or name the worker-only directory");
        }
    }

    [Fact]
    public void TheEnvExample_OffersThePointer_IntoTheWorkerOnlyMountsTarget() =>
        Read("deploy/.env.example").Select(l => l.Trim())
            .ShouldContain($"#{Variable}={Target}/{PasswordFile}");

    [Fact]
    public void TheDirectoryItsCreatorsAndTheInjection_AreOnePathAndOneFileName()
    {
        // The compose source, the two scripts that create it before an `up`, the boot-time line and the script's own
        // test name one directory and one file. A creator that drifted would leave the source absent, and with
        // create_host_path: false that is a refused worker start.
        var script = string.Join('\n', Read("deploy/systemd/jobbliggaren-inject-secrets.sh"));
        var scriptTest = string.Join('\n', Read("deploy/systemd/jobbliggaren-inject-secrets.test.sh"));

        script.ShouldContain($"readonly WORKER_SECRETS_DIR={Source}\n");
        script.ShouldContain($"readonly DMARC_MAILBOX_PASSWORD=\"{PasswordFile}\"\n");
        script.ShouldContain($"[[ -n \"$(env_value {Variable})\" ]]");
        script.Contains("resolve_runtime_ids jobbliggaren-worker", StringComparison.Ordinal).ShouldBeTrue(
            "the file's owner is the worker image's, measured from that image and never assumed equal to the api's");
        Read("deploy/systemd/jobbliggaren-reconcile.sh").ShouldContain($"readonly WORKER_SECRETS_DIR={Source}");

        Read("deploy/systemd/jobbliggaren-tmpfiles.conf")
            .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'))
            .Select(l => string.Join(' ', l.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .ShouldContain($"d {Source} :0700 :root :root -");

        scriptTest.ShouldContain($"readonly PASSWORD_FILE=\"{PasswordFile}\"\n");
        scriptTest.ShouldContain($"readonly MAILBOX_POINTER=\"{Variable}={Target}/$PASSWORD_FILE\"\n");
        scriptTest.ShouldContain("inject_dmarc_mailbox_password");
    }

    private static readonly Regex LongSource = new(@"^\s*source:\s*(?<path>/\S+)\s*$", RegexOptions.CultureInvariant);

    private static readonly Regex ShortSource = new(@"(?:^|\s)(?<path>/[^\s:]+):/", RegexOptions.CultureInvariant);
}
