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

    private static readonly string[] ProductionAssemblyPaths =
    [
        typeof(Jobbliggaren.Domain.Matching.UserJobAdMatch).Assembly.Location,
        typeof(Jobbliggaren.Application.Matching.Jobs.BackgroundMatching.TopMatchMailDispatcher).Assembly.Location,
        typeof(SqlAccountDirectory).Assembly.Location,
        typeof(Jobbliggaren.Api.Endpoints.AuthProblem).Assembly.Location,
        typeof(Jobbliggaren.Worker.Hosting.BackgroundMatchingWorker).Assembly.Location,
        typeof(Jobbliggaren.Migrate.ConnectionStringFactory).Assembly.Location,
        typeof(Jobbliggaren.Migrate.Roles).Assembly.Location,
    ];

    [Fact]
    public void The_scan_reads_every_project_under_src()
    {
        var projects = Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(path => Path.GetFileNameWithoutExtension(path));

        ProductionAssemblyPaths.Select(path => Path.GetFileNameWithoutExtension(path)).ShouldBe(
            projects, ignoreOrder: true, $"{Bound} A project under src/ that the scan skips can name the table unseen.");
    }

    [Fact]
    public void Only_the_configuration_the_recorder_and_the_directory_name_the_table()
    {
        var sites = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in ProductionTypes().Where(type => !IsMigrationOrSnapshot(type)))
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
        var target = typeof(AccountActivity).FullName!;
        var owners = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in ProductionTypes())
        {
            var owner = OwnerOf(type).FullName;
            if (owner == target)
                continue;
            if (MentionsType(type.BaseType, target)
                || type.Interfaces.Any(implemented => MentionsType(implemented.InterfaceType, target))
                || type.Fields.Any(field => MentionsType(field.FieldType, target))
                || type.Methods.Any(method => MethodMentions(method, target)))
                owners.Add(owner);
        }

        owners.ShouldBe([typeof(AccountActivityConfiguration).FullName!], $"{Bound} Types found: {string.Join(", ", owners)}");
    }

    private static IEnumerable<TypeDefinition> ProductionTypes()
    {
        foreach (var path in ProductionAssemblyPaths)
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            foreach (var type in assembly.Modules.SelectMany(module => module.GetTypes()))
                yield return type;
        }
    }

    private static bool IsMigrationOrSnapshot(TypeDefinition type)
    {
        var owner = OwnerOf(type);
        return owner.BaseType?.FullName is { } baseType
            && (baseType == typeof(Migration).FullName || baseType == typeof(ModelSnapshot).FullName);
    }

    /// <summary>The type a compiler-generated closure, state machine or display class was written in.</summary>
    private static TypeDefinition OwnerOf(TypeDefinition type)
    {
        while (type.DeclaringType is not null && IsCompilerGenerated(type))
            type = type.DeclaringType;
        return type;
    }

    private static bool IsCompilerGenerated(TypeDefinition type) =>
        type.Name.StartsWith('<')
        || type.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == typeof(CompilerGeneratedAttribute).FullName);

    private static bool MethodMentions(MethodDefinition method, string target) =>
        MentionsType(method.ReturnType, target)
        || method.Parameters.Any(parameter => MentionsType(parameter.ParameterType, target))
        || (method.HasBody
            && (method.Body.Variables.Any(variable => MentionsType(variable.VariableType, target))
                || method.Body.Instructions.Any(instruction => MentionsOperand(instruction.Operand, target))));

    private static bool MentionsOperand(object? operand, string target) => operand switch
    {
        TypeReference type => MentionsType(type, target),
        GenericInstanceMethod method => MentionsOperand(method.ElementMethod, target)
            || method.GenericArguments.Any(argument => MentionsType(argument, target)),
        MethodReference method => MentionsType(method.DeclaringType, target)
            || MentionsType(method.ReturnType, target)
            || method.Parameters.Any(parameter => MentionsType(parameter.ParameterType, target)),
        FieldReference field => MentionsType(field.DeclaringType, target) || MentionsType(field.FieldType, target),
        _ => false,
    };

    private static bool MentionsType(TypeReference? type, string target) => type switch
    {
        null => false,
        GenericInstanceType generic => MentionsType(generic.ElementType, target)
            || generic.GenericArguments.Any(argument => MentionsType(argument, target)),
        TypeSpecification specification => MentionsType(specification.ElementType, target),
        _ => type.FullName == target,
    };

    // thisFile = <repo>/tests/Jobbliggaren.Architecture.Tests/AccountActivityReaderTests.cs → up two = repo root.
    private static string RepoRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
