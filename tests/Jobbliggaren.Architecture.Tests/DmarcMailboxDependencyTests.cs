using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Jobbliggaren.Infrastructure.Mailbox.Dmarc;
using Mono.Cecil;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1981 PR 4a (ADR 0184; senior-cto-advisor Beslut D2, r2 §3; dotnet-architect re-check Rekommendation 1) — MailKit,
/// MimeKit and BouncyCastle reach the Worker and nothing else. The mail packages are declared in one project, that
/// project references Application alone, only the Worker references it, and only its assembly references the mail
/// assemblies. Each fact is read where it is decided: the build files as text, the compiled assemblies as IL.
/// </summary>
public class DmarcMailboxDependencyTests
{
    private const string MailboxProject = "src/Jobbliggaren.Infrastructure.Mailbox/Jobbliggaren.Infrastructure.Mailbox.csproj";

    private static readonly string[] MailPackageIds = ["MailKit", "MimeKit", "MailKitLite", "MimeKitLite"];

    private static readonly string[] MailAssemblyNames = ["MailKit", "MimeKit", "BouncyCastle.Cryptography"];

    private static readonly string[] ScannedRoots = ["src", "tests", "perf"];

    private static readonly string[] BuildFilePatterns = ["*.csproj", "*.props", "*.targets"];

    /// <summary><c>GlobalPackageReference</c> too: in <c>Directory.Packages.props</c> it hands a package to every project.</summary>
    private static readonly Regex PackageElement = new(
        @"<(?<element>GlobalPackageReference|PackageReference|PackageVersion)\b[^>]*?Include\s*=\s*""(?<id>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex ProjectReferenceElement = new(
        @"<ProjectReference\b[^>]*?Include\s*=\s*""(?<path>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly string MailboxAssemblyPath = typeof(DmarcMailboxCollectionExtensions).Assembly.Location;

    [Fact]
    public void Only_the_mailbox_project_and_the_central_version_file_declare_a_mail_package()
    {
        var declarations = PackageDeclarations()
            .Where(declaration => MailPackageIds.Contains(declaration.Id, StringComparer.OrdinalIgnoreCase))
            .Select(declaration => declaration.ToString());

        declarations.ShouldBe(
            [
                "Directory.Packages.props PackageVersion MailKit",
                $"{MailboxProject} PackageReference MailKit",
            ],
            ignoreOrder: true,
            "MailKit is declared once for its version and once where it is used (ADR 0184, Klas GO K2). MimeKit and "
            + "the obsolete Lite variants reach the mailbox project through MailKit and are never declared.");
    }

    [Fact]
    public void No_build_file_declares_bouncy_castle()
    {
        PackageDeclarations()
            .Where(declaration => declaration.Id.Contains("BouncyCastle", StringComparison.OrdinalIgnoreCase))
            .Select(declaration => declaration.ToString())
            .ShouldBeEmpty(
                "BouncyCastle is MimeKit's own dependency for S/MIME and PGP, which nothing here calls. Declaring it "
                + "would pin it for every project under transitive pinning.");
    }

    /// <summary>
    /// The scans above pass over nothing if the enumeration or the regex stops working, so both are crossed here
    /// against files that certainly exist (the <see cref="NoAmazonReferenceTests"/> form).
    /// </summary>
    [Fact]
    public void The_package_scan_reads_the_files_a_declaration_can_live_in()
    {
        var root = RepoRoot();
        var files = BuildFiles(root).Select(file => Relative(root, file)).ToList();

        files.ShouldContain("Directory.Packages.props");
        files.ShouldContain("Directory.Build.props");
        files.ShouldContain("tests/Directory.Build.props");
        files.ShouldContain(MailboxProject);
        PackageDeclarations().ShouldContain(declaration => declaration.Id == "QuestPDF");
    }

    [Fact]
    public void The_mailbox_project_references_application_and_three_packages_and_nothing_else()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), MailboxProject));

        ProjectReferenceElement.Matches(text).Select(ProjectName).ShouldBe(
            ["Jobbliggaren.Application"],
            "the port it implements lives in Application; Infrastructure would hand it EF Core and Npgsql "
            + "(dotnet-architect re-check, Rekommendation 1)");
        PackageElement.Matches(text).Select(match => match.Groups["id"].Value).ShouldBe(
            ["MailKit", "Microsoft.Extensions.Configuration.Abstractions", "Microsoft.Extensions.DependencyInjection.Abstractions"],
            ignoreOrder: true);
    }

    /// <summary>
    /// Infrastructure and Application are named on their own because either one referencing the mailbox would hand
    /// MailKit to the Api transitively, while a check of the Api's project file alone would still pass.
    /// </summary>
    [Fact]
    public void No_src_project_but_the_worker_references_the_mailbox_project()
    {
        var root = RepoRoot();
        var referencing = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(project => ProjectReferenceElement.Matches(File.ReadAllText(project))
                .Select(ProjectName)
                .Contains("Jobbliggaren.Infrastructure.Mailbox", StringComparer.OrdinalIgnoreCase))
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();

        referencing.ShouldBe(["Jobbliggaren.Worker"], "only the Worker reads the DMARC mailbox (ADR 0184)");
        referencing.ShouldNotContain("Jobbliggaren.Infrastructure");
        referencing.ShouldNotContain("Jobbliggaren.Application");
        referencing.ShouldNotContain("Jobbliggaren.Api");
    }

    [Fact]
    public void Only_the_mailbox_assembly_references_a_mail_assembly()
    {
        var offenders = ProductionTypeSweep.AssemblyPaths
            .Where(path => !string.Equals(path, MailboxAssemblyPath, StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => AssemblyReferenceNames(path)
                .Where(MailAssemblyNames.Contains)
                .Select(reference => $"{Path.GetFileNameWithoutExtension(path)} -> {reference}"))
            .ToList();

        offenders.ShouldBeEmpty("MailKit and MimeKit belong to Jobbliggaren.Infrastructure.Mailbox alone (Beslut D2)");
        ProductionTypeSweep.AssemblyPaths.ShouldContain(MailboxAssemblyPath);
    }

    /// <summary>The positive half: the scan above is asked about the right assembly, and it is the one holding MailKit.</summary>
    [Fact]
    public void The_mailbox_assembly_references_mailkit_and_mimekit_and_never_bouncy_castle()
    {
        var references = AssemblyReferenceNames(MailboxAssemblyPath);

        references.ShouldContain("MailKit");
        references.ShouldContain("MimeKit");
        references.ShouldNotContain("BouncyCastle.Cryptography", "no S/MIME or PGP call is ever made from the adapter");
    }

    /// <summary>
    /// CTO r2 §3 (finding 11): the Worker-only rules elsewhere in this project scan the Worker assembly by name and
    /// never this one. They hold for it by construction as long as everything it reaches in our code is the DMARC
    /// port and its own types, since the Worker reaches all of that too.
    /// </summary>
    [Fact]
    public void Every_reference_from_the_mailbox_into_our_code_stays_in_the_dmarc_port_namespace()
    {
        using var assembly = AssemblyDefinition.ReadAssembly(MailboxAssemblyPath);
        var module = assembly.MainModule;

        module.AssemblyReferences.Select(reference => reference.Name)
            .Where(name => name.StartsWith("Jobbliggaren.", StringComparison.Ordinal))
            .ShouldBe(["Jobbliggaren.Application"]);

        var reached = module.GetTypeReferences()
            .Where(type => type.Scope is AssemblyNameReference { Name: var name } && name.StartsWith("Jobbliggaren.", StringComparison.Ordinal))
            .Select(type => (Type: type.FullName, Namespace: OutermostNamespace(type)))
            .ToList();

        reached.Where(reference => reference.Namespace != "Jobbliggaren.Application.Admin.Email.Dmarc")
            .Select(reference => reference.Type)
            .ShouldBeEmpty("the mailbox adapter may reach the DMARC port and nothing else of ours");
        reached.Select(reference => reference.Type)
            .ShouldContain("Jobbliggaren.Application.Admin.Email.Dmarc.IDmarcMailbox", "the scan read the adapter's references at all");
    }

    [Fact]
    public void Only_the_worker_image_copies_the_mailbox_project_and_before_its_restore()
    {
        var root = RepoRoot();
        var copying = Directory.EnumerateFiles(Path.Combine(root, "src"), "Dockerfile", SearchOption.AllDirectories)
            .Where(file => InstructionLines(file).Any(line => line.Contains("Jobbliggaren.Infrastructure.Mailbox", StringComparison.Ordinal)))
            .Select(file => Relative(root, file))
            .ToList();

        copying.ShouldBe(["src/Jobbliggaren.Worker/Dockerfile"], "the Api image never holds MailKit (Art. 25(2), Beslut D2)");

        var worker = InstructionLines(Path.Combine(root, "src", "Jobbliggaren.Worker", "Dockerfile"));
        var copyProject = worker.FindIndex(line => line.StartsWith(
            "COPY src/Jobbliggaren.Infrastructure.Mailbox/Jobbliggaren.Infrastructure.Mailbox.csproj ", StringComparison.Ordinal));
        var restore = worker.FindIndex(line => line.StartsWith("RUN dotnet restore", StringComparison.Ordinal));
        copyProject.ShouldBeGreaterThanOrEqualTo(0, "the restore layer needs the mailbox project file");
        copyProject.ShouldBeLessThan(restore);
        worker.ShouldContain("COPY src/Jobbliggaren.Infrastructure.Mailbox/ src/Jobbliggaren.Infrastructure.Mailbox/");
    }

    private static IEnumerable<PackageDeclaration> PackageDeclarations()
    {
        var root = RepoRoot();
        foreach (var file in BuildFiles(root))
        {
            foreach (Match match in PackageElement.Matches(File.ReadAllText(file)))
                yield return new PackageDeclaration(Relative(root, file), match.Groups["element"].Value, match.Groups["id"].Value);
        }
    }

    private static IEnumerable<string> BuildFiles(string root) =>
        ScannedRoots
            .Select(directory => Path.Combine(root, directory))
            .Where(Directory.Exists)
            .SelectMany(directory => BuildFilePatterns.SelectMany(pattern => Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)))
            .Concat(BuildFilePatterns.SelectMany(pattern => Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly)))
            .Where(path => !IsBuildOutput(path));

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string ProjectName(Match match) =>
        Path.GetFileNameWithoutExtension(match.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar));

    private static List<string> AssemblyReferenceNames(string path)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        return [.. assembly.MainModule.AssemblyReferences.Select(reference => reference.Name)];
    }

    /// <summary>Cecil gives a nested type reference no namespace of its own; it lives in its outermost type's.</summary>
    private static string OutermostNamespace(TypeReference type)
    {
        while (type.DeclaringType is not null)
            type = type.DeclaringType;
        return type.Namespace;
    }

    private static List<string> InstructionLines(string dockerfile) =>
        [.. File.ReadLines(dockerfile).Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#'))];

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private sealed record PackageDeclaration(string File, string Element, string Id)
    {
        public override string ToString() => $"{File} {Element} {Id}";
    }
}
