using System.Runtime.CompilerServices;
using Jobbliggaren.Infrastructure.Admin.Accounts;
using Mono.Cecil;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// The IL type-reference sweep of every production assembly, shared by <see cref="AccountActivityReaderTests"/> and
/// <see cref="ReadTimeMatchingConsumerTests"/>.
/// </summary>
internal static class ProductionTypeSweep
{
    internal static readonly string[] AssemblyPaths =
    [
        typeof(Jobbliggaren.Domain.Matching.UserJobAdMatch).Assembly.Location,
        typeof(Jobbliggaren.Application.Matching.Jobs.BackgroundMatching.TopMatchMailDispatcher).Assembly.Location,
        typeof(SqlAccountDirectory).Assembly.Location,
        typeof(Jobbliggaren.Api.Endpoints.AuthProblem).Assembly.Location,
        typeof(Jobbliggaren.Worker.Hosting.BackgroundMatchingWorker).Assembly.Location,
        typeof(Jobbliggaren.Migrate.ConnectionStringFactory).Assembly.Location,
        typeof(Jobbliggaren.Migrate.Roles).Assembly.Location,
    ];

    internal static IEnumerable<TypeDefinition> Types()
    {
        foreach (var path in AssemblyPaths)
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            foreach (var type in assembly.Modules.SelectMany(module => module.GetTypes()))
                yield return type;
        }
    }

    /// <summary>The written types, other than <paramref name="target"/> itself, whose signatures or IL name it.</summary>
    internal static SortedSet<string> OwnersReferencing(string target)
    {
        var owners = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in Types())
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

        return owners;
    }

    /// <summary>The type a compiler-generated closure, state machine or display class was written in.</summary>
    internal static TypeDefinition OwnerOf(TypeDefinition type)
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
}
