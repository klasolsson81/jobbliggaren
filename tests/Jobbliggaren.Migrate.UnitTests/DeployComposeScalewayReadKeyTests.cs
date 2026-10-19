using System.Text.RegularExpressions;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Migrate.UnitTests;

/// <summary>
/// #1981 PR 2 — the delivery read key reaches the api and nothing else. The key reads every recipient's address at
/// the provider and the worker egresses, so the pointer is the api's alone, and the file lies in a host directory
/// bound into the api and no other service, never in the secrets directory the worker shares (security-auditor
/// 2026-10-19, Major 1).
///
/// <para>
/// The mount carries <c>create_host_path: false</c>, so a box the key never reached starts the api only because
/// something creates that directory first: the reconcile before every apply, the tmpfiles line at boot, the
/// injection on every run. The last pin holds the four to one path.
/// </para>
/// </summary>
public class DeployComposeScalewayReadKeyTests
{
    private const string ComposePath = "deploy/docker-compose.yml";
    private const string Pointer = "Email__ScalewayRead__SecretKey_FILE";
    private const string Variable = "EMAIL_SCALEWAY_READ_SECRET_KEY_FILE";
    private const string KeyFile = "Email__ScalewayRead__SecretKey";
    private const string Source = "/run/jobbliggaren/api-secrets";
    private const string Target = "/run/api-secrets";

    private static ComposeFile Compose => new(ComposePath);

    private static string[] Read(string relativePath) =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, relativePath));

    private static List<(string Line, int Index)> Uncommented(string[] lines) =>
        lines.Select((line, index) => (Line: line, Index: index)).Where(l => !l.Line.TrimStart().StartsWith('#')).ToList();

    [Fact]
    public void ThePointer_DefaultsToEmpty_AndOnlyTheApiBlockCarriesIt()
    {
        var lines = Read(ComposePath);
        var api = Array.FindIndex(lines, l => l.TrimEnd() == "  api:");
        var next = Array.FindIndex(lines, api + 1, l => l.Length > 2 && l.StartsWith("  ", StringComparison.Ordinal)
                                                         && !l.StartsWith("   ", StringComparison.Ordinal)
                                                         && !l.TrimStart().StartsWith('#')
                                                         && l.TrimEnd().EndsWith(':'));
        api.ShouldBeGreaterThan(-1, "no api service header");
        next.ShouldBeGreaterThan(api, "no service header after api");

        // Across the whole file, the x-* anchors included: an anchor the worker merges would carry it there too.
        var carriers = Uncommented(lines)
            .Where(l => l.Line.Contains("ScalewayRead", StringComparison.Ordinal)
                        || l.Line.Contains("EMAIL_SCALEWAY_READ_", StringComparison.Ordinal))
            .ToList();

        carriers.Count.ShouldBe(1);
        carriers[0].Index.ShouldBeInRange(api + 1, next - 1);
        carriers[0].Line.Trim().ShouldBe($"{Pointer}: ${{{Variable}:-}}", "empty by default and never `:?`");
    }

    [Fact]
    public void TheApi_MountsTheDirectory_ReadOnly_AndNeverCreatesItThroughDocker()
    {
        var mounts = ComposeMounts.Of(Compose.ServiceBlock("api"))
            .Where(m => ComposeMounts.SourceOf(m) == Source)
            .ToList();

        mounts.Count.ShouldBe(1, $"api must mount {Source} exactly once");
        mounts[0].ShouldContain("type: bind");
        mounts[0].ShouldContain($"target: {Target}");
        mounts[0].ShouldContain("read_only: true", "the api reads the key and can never write to it");
        mounts[0].ShouldContain("create_host_path: false",
            "a secret directory Docker created would be 0755 rather than closed; the reconcile, tmpfiles and the injection create it");
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
        sources.ShouldContain("/run/jobbliggaren/redis/worker-persistent");
        sources.Count(path => path == Source).ShouldBe(1, "only api's own mount binds the directory");
        foreach (var path in sources.Where(path => path != Source))
        {
            Source.StartsWith(path + "/", StringComparison.Ordinal)
                .ShouldBeFalse($"{path} is a parent of {Source}, so whatever mounts it reaches the read key");
            path.StartsWith(Source + "/", StringComparison.Ordinal).ShouldBeFalse($"{path} lies inside {Source}");
        }

        Compose.ServiceBlock("api").Count(l => !l.TrimStart().StartsWith('#') && l.Contains(Source, StringComparison.Ordinal))
            .ShouldBe(1, "the one bind of the directory is api's");
        foreach (var service in ComposeMounts.ServiceNames(ComposePath).Where(name => name != "api"))
        {
            string.Join('\n', Compose.ServiceBlock(service).Where(l => !l.TrimStart().StartsWith('#')))
                .Contains("api-secrets", StringComparison.Ordinal)
                .ShouldBeFalse($"{service} must not mount or name the api-only directory");
        }
    }

    [Fact]
    public void TheEnvExample_OffersThePointer_IntoTheApiOnlyMountsTarget() =>
        Read("deploy/.env.example").Select(l => l.Trim())
            .ShouldContain($"#{Variable}={Target}/{KeyFile}");

    [Fact]
    public void TheDirectoryItsCreatorsAndTheInjection_AreOnePathAndOneFileName()
    {
        // The compose source, the two scripts that create it before an `up`, the boot-time line and the script's own
        // test name one directory and one file. A creator that drifted would leave the source absent, and with
        // create_host_path: false that is a refused api start.
        var script = string.Join('\n', Read("deploy/systemd/jobbliggaren-inject-secrets.sh"));
        var scriptTest = string.Join('\n', Read("deploy/systemd/jobbliggaren-inject-secrets.test.sh"));

        script.ShouldContain($"readonly API_SECRETS_DIR={Source}\n");
        script.ShouldContain($"readonly SCALEWAY_READ_SECRET_KEY=\"{KeyFile}\"\n");
        script.ShouldContain($"[[ -n \"$(env_value {Variable})\" ]]");
        Read("deploy/systemd/jobbliggaren-reconcile.sh").ShouldContain($"readonly API_SECRETS_DIR={Source}");

        Read("deploy/systemd/jobbliggaren-tmpfiles.conf")
            .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'))
            .Select(l => string.Join(' ', l.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .ShouldContain($"d {Source} :0700 :root :root -");

        scriptTest.ShouldContain($"readonly READ_KEY=\"{KeyFile}\"\n");
        scriptTest.ShouldContain($"readonly READ_POINTER=\"{Variable}={Target}/$READ_KEY\"\n");
        scriptTest.ShouldContain("inject_scaleway_read_key 4242 4343");
    }

    private static readonly Regex LongSource = new(@"^\s*source:\s*(?<path>/\S+)\s*$", RegexOptions.CultureInvariant);

    private static readonly Regex ShortSource = new(@"(?:^|\s)(?<path>/[^\s:]+):/", RegexOptions.CultureInvariant);
}
