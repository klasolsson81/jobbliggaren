using System.Runtime.CompilerServices;
using Jobbliggaren.Infrastructure.Admin.Accounts;
using Jobbliggaren.Infrastructure.Auth.Activity;
using Jobbliggaren.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #2161 part 2 (ADR 0172 D3) — where the activity record may go. The account directory reads it, the recorder writes
/// it, the configuration maps it, and nothing else in the production assemblies names the table or its type. A new
/// consumer, such as an inactivity mail or an inactivity deletion, is a new purpose (security-auditor's bound (b)) and
/// fails here until it is reviewed. The sites are the IL's own, mapped from compiler-generated code back to the method
/// written; EF migrations and model snapshots are admitted by kind, since every later migration carries the whole
/// model. The test cannot see SQL assembled without the table's name, a view or function a migration creates, or a
/// read made outside the code (CLAUDE.md §9.2 (b)).
/// </summary>
public class AccountActivityReaderTests
{
    private const string Table = "account_activity";

    private const string Bound =
        "ADR 0172 D3: only SqlAccountDirectory reads account_activity; any other consumer is a new purpose "
        + "(security-auditor's bound (b), #2161) and needs its own review.";

    [Fact]
    public void The_scan_reads_every_project_under_src()
    {
        var projects = Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetFileNameWithoutExtension(path));

        ProductionTypeSweep.AssemblyPaths.Select(path => Path.GetFileNameWithoutExtension(path)).ShouldBe(projects, ignoreOrder: true, Bound);
    }

    [Fact]
    public void Only_the_configuration_the_recorder_and_the_directory_name_the_table()
    {
        var sites = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in ProductionTypeSweep.Types().Where(type => !IsMigrationOrSnapshot(type)))
        {
            foreach (var method in type.Methods.Where(method => method.HasBody))
            {
                if (method.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldstr
                        && instruction.Operand is string literal
                        && literal.Contains(Table, StringComparison.OrdinalIgnoreCase)))
                    sites.Add(JobAdLifecycleReadRegistryTests.LogicalOwnerKey(method));
            }
        }

        sites.ShouldBe(
            [
                $"{typeof(SqlAccountDirectory).FullName}.Command",
                $"{typeof(SqlAccountActivityRecorder).FullName}.WriteAsync",
                $"{typeof(AccountActivityConfiguration).FullName}.{nameof(AccountActivityConfiguration.Configure)}",
            ],
            ignoreOrder: true,
            $"{Bound} Sites found: {string.Join(", ", sites)}");
    }

    [Fact]
    public void Only_the_configuration_references_the_activity_type()
    {
        var owners = ProductionTypeSweep.OwnersReferencing(typeof(AccountActivity).FullName!);

        owners.ShouldBe([typeof(AccountActivityConfiguration).FullName!], $"{Bound} Types found: {string.Join(", ", owners)}");
    }

    private static bool IsMigrationOrSnapshot(TypeDefinition type)
    {
        var owner = ProductionTypeSweep.OwnerOf(type);
        return owner.BaseType?.FullName is { } baseType
            && (baseType == typeof(Migration).FullName || baseType == typeof(ModelSnapshot).FullName);
    }

    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
