using Mono.Cecil;
using Mono.Cecil.Cil;
using Npgsql;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1633 — Npgsql's <c>Include Error Detail</c> must stay off. With it off (the driver default),
/// <c>PostgresException.Detail</c> renders as a fixed redaction placeholder instead of the
/// offending key's VALUES, so a duplicate-key exception carries the constraint name and nothing
/// else.
///
/// <para>
/// <b>Why this is load-bearing rather than housekeeping.</b> The unique index over
/// <c>AspNetUsers.normalized_user_name</c> is keyed on the user's normalised email. A duplicate
/// registration racing past Identity's own pre-check raises 23505 there, and with the flag ON the
/// email address would be written into whatever log receives the exception — the container log
/// and Seq alike. #1633's acceptance criterion 4 was CLOSED BY MEASUREMENT on 2026-09-04 (the
/// redaction placeholder read out of the live stack, and no occurrence of the flag anywhere in
/// <c>src/</c>), and this test is what turns that dated reading into a standing guarantee.
/// </para>
///
/// <para>
/// Two arms, because a connection string reaches Npgsql two ways: a committed configuration file
/// and code that builds one. The file arm matches raw text; the IL arm matches BOTH a string
/// literal and the typed <c>NpgsqlConnectionStringBuilder.IncludeErrorDetail</c> setter, which
/// compiles to no literal at all — <c>Jobbliggaren.Migrate</c> builds its connection string with
/// exactly that object-initialiser shape, so the second form is reachable, not hypothetical.
/// </para>
///
/// <para>
/// <b>What neither arm sees, enumerated rather than gestured at:</b> a gitignored
/// <c>appsettings.Local.json</c>, the box's <c>.env</c>, and
/// <c>docs/runbooks/local-dev-setup.md</c>'s <c>export ConnectionStrings__Postgres=…</c>, which a
/// developer pastes into a shell no test can read. Those three residuals are real and are not
/// closed here.
/// </para>
///
/// <para>
/// Matching is normalised (case-folded, spaces and underscores removed) because Npgsql accepts
/// the keyword in several spellings. Over JSON/YAML/env config that normalisation is safe; over
/// IL it is applied to string literals only, so a comment naming the concept — as
/// <c>RecentJobSearchCaptureBehavior</c> does — cannot trip it.
/// </para>
/// </summary>
public class IncludeErrorDetailGuardTests
{
    /// <summary>The normalised form every accepted spelling collapses to.</summary>
    private const string ForbiddenNormalised = "includeerrordetail";

    /// <summary>The property setter the typed builder compiles to — no string literal involved.</summary>
    private const string IncludeErrorDetailSetter = "set_IncludeErrorDetail";

    /// <summary>
    /// Committed files that can carry a connection string. <c>infra/terraform/</c> is deliberately
    /// excluded: ADR 0066 preserved it as a record of what ran on the destroyed AWS stack, not as
    /// live configuration (CLAUDE.md §11), so a hit there would gate on history.
    /// </summary>
    public static TheoryData<string> ConfigFiles =>
    [
        ".env.example",
        "deploy/.env.example",
        "deploy/detection/detection.env.example",
        "deploy/docker-compose.yml",
        "docker-compose.yml",
        // The app under test in CI, and its logs are public.
        ".github/workflows/e2e.yml",
        "src/Jobbliggaren.Api/appsettings.json",
        "src/Jobbliggaren.Api/appsettings.Development.json",
        "src/Jobbliggaren.Api/appsettings.Production.json",
        "src/Jobbliggaren.Api/appsettings.Local.json.example",
        "src/Jobbliggaren.Worker/appsettings.json",
        "src/Jobbliggaren.Worker/appsettings.Development.json",
        "src/Jobbliggaren.Worker/appsettings.Production.json",
    ];

    [Theory]
    [MemberData(nameof(ConfigFiles))]
    public void ConfigFile_should_not_enable_Include_Error_Detail(string relativePath)
    {
        var absolutePath = Path.Combine(
            FindRepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

        File.Exists(absolutePath).ShouldBeTrue(
            $"{relativePath} is missing — the guard's file list has drifted from the repo, and a " +
            "list that names a file nobody ships measures nothing. Fix the list, do not delete " +
            "the case.");

        Normalise(File.ReadAllText(absolutePath)).ShouldNotContain(
            ForbiddenNormalised,
            customMessage:
            $"{relativePath} names Npgsql's Include Error Detail. Enabled, it puts the offending " +
            "key's VALUES into PostgresException.Detail — for UserNameIndex, the user's email " +
            "address — and from there into the container log and Seq (#1633).");
    }

    [Theory]
    [InlineData(typeof(Jobbliggaren.Api.Configuration.HstsOptions))]
    [InlineData(typeof(Jobbliggaren.Worker.Auditing.WorkerSystemUser))]
    [InlineData(typeof(Jobbliggaren.Infrastructure.Persistence.AppDbContext))]
    [InlineData(typeof(Jobbliggaren.Migrate.ConnectionStringFactory))]
    public void Assembly_should_not_build_a_connection_string_enabling_Include_Error_Detail(
        Type assemblyMarker)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(assemblyMarker.Assembly.Location);

        var offenders = new List<string>();
        var instructionsScanned = 0;

        foreach (var type in assembly.Modules.SelectMany(m => m.GetTypes()))
        {
            foreach (var method in type.Methods.Where(m => m.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    instructionsScanned++;

                    if (instruction.OpCode == OpCodes.Ldstr
                        && instruction.Operand is string literal
                        && Normalise(literal).Contains(ForbiddenNormalised, StringComparison.Ordinal))
                    {
                        offenders.Add($"{type.FullName}::{method.Name} (literal)");
                    }

                    // The typed setter compiles to NO string literal, so the literal check above
                    // cannot see it. Same shape as PooledConnectionResetIlTests (#1232).
                    if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                        && instruction.Operand is MethodReference called
                        && called.Name == IncludeErrorDetailSetter
                        && called.DeclaringType.Name == nameof(NpgsqlConnectionStringBuilder))
                    {
                        offenders.Add($"{type.FullName}::{method.Name} ({IncludeErrorDetailSetter})");
                    }
                }
            }
        }

        // The probe's vacuity guard, and it is not decoration: an assembly Cecil reads with zero
        // method bodies produces an empty offender list and a green case after scanning nothing.
        // That is the #843 test-fiction class, and only one of these four assemblies was ever
        // mutation-proven.
        instructionsScanned.ShouldBeGreaterThan(
            0,
            $"{assembly.Name.Name}: zero IL instructions were examined, so this case passed " +
            "without measuring anything. Fix the marker type or the assembly resolution.");

        offenders.ShouldBeEmpty(
            $"{assembly.Name.Name} builds a connection string that turns on Include Error Detail. " +
            "See the file-arm message above for what that exposes (#1633).");
    }

    private static string Normalise(string value) =>
        value.Replace(" ", string.Empty, StringComparison.Ordinal)
             .Replace("_", string.Empty, StringComparison.Ordinal)
             .ToLowerInvariant();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;

        return dir.ShouldNotBeNull(
            "could not find the repo root (CLAUDE.md) walking up from the test bin — this guard " +
            "reads the source tree, not the build output").FullName;
    }
}
