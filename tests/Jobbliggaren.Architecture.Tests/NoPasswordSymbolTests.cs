using Jobbliggaren.Api.Configuration;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Worker.Auditing;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// ADR 0142 part 5a (#1743) — no password symbol is reachable from the auth surface: declared, carried in a
/// signature, referenced from another module, or held as a string constant, literal or attribute argument. One
/// instrument, Mono.Cecil (senior-cto-advisor F3, 2026-09-25).
/// <para>
/// <b>Scope, per assembly.</b> Domain, Application, Api and Worker. Infrastructure is outside it: its Identity
/// adapter removes a squatter's hash on first proof (<c>RemovePasswordAsync</c>) until 5b. Migrate is outside it:
/// its master password is infrastructure, not the auth surface.
/// </para>
/// <para>
/// <b>Token-level normalisation.</b> Every <c>passwordless</c> is removed before <c>password</c> is searched for,
/// so <c>CreatePasswordlessUserAsync</c> passes while a name carrying <c>password</c> beside it does not. There is
/// no exception list.
/// </para>
/// </summary>
public class NoPasswordSymbolTests
{
    private const string Passwordless = "passwordless";

    public static TheoryData<Type> AuthSurfaceAssemblies() =>
    [
        typeof(JobSeeker),
        typeof(AuthErrorCodes),
        typeof(HstsOptions),
        typeof(WorkerSystemUser),
    ];

    [Theory]
    [MemberData(nameof(AuthSurfaceAssemblies))]
    public void No_password_symbol_is_declared_referenced_or_held_as_a_constant(Type assemblyMarker)
    {
        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(assemblyMarker.Assembly.Location)!);
        using var assembly = AssemblyDefinition.ReadAssembly(
            assemblyMarker.Assembly.Location, new ReaderParameters { AssemblyResolver = resolver });
        var symbols = Symbols(assembly).ToList();

        symbols.ShouldNotBeEmpty($"{assembly.Name.Name}: nothing was scanned, so an empty offender list proves nothing");

        var offenders = symbols
            .Where(s => s.Arm is Arm.Constant or Arm.Literal ? ConstantNamesAPassword(s.Text) : NameNamesAPassword(s.Text))
            .Select(s => $"{s.Arm}: {s.Text}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            $"{assembly.Name.Name} still reaches a password symbol:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_normalisation_strips_the_passwordless_symbols_rather_than_skipping_them()
    {
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AuthErrorCodes).Assembly.Location);

        Symbols(assembly)
            .Where(s => s.Text.Contains(Passwordless, StringComparison.OrdinalIgnoreCase))
            .ShouldContain(s => s.Arm == Arm.Declaration && s.Text.EndsWith(".IPasswordlessAccountCreator", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("IPasswordlessAccountCreator", false)]
    [InlineData("CreatePasswordlessUserAsync", false)]
    [InlineData("CreatePasswordlessUserOrResetPasswordAsync", true)]
    [InlineData("ChangePasswordCommand", true)]
    [InlineData("PASSWORD_HASH", true)]
    public void A_name_is_judged_after_every_passwordless_is_removed(string name, bool namesAPassword) =>
        NameNamesAPassword(name).ShouldBe(namesAPassword);

    [Theory]
    [InlineData("E-post eller lösenord är felaktigt.", true)]
    [InlineData("Glomt losenord", true)]
    [InlineData("Logga in utan lösenord", true)]
    [InlineData("Det gick inte att bekräfta att det är du.", false)]
    public void A_constant_is_judged_on_both_languages(string constant, bool namesAPassword) =>
        ConstantNamesAPassword(constant).ShouldBe(namesAPassword);

    private enum Arm
    {
        Declaration,
        Signature,
        Reference,
        Constant,
        Literal,
    }

    private sealed record Symbol(Arm Arm, string Text);

    private static bool NameNamesAPassword(string text) =>
        text.Replace(Passwordless, string.Empty, StringComparison.OrdinalIgnoreCase)
            .Contains("password", StringComparison.OrdinalIgnoreCase);

    private static bool ConstantNamesAPassword(string text) =>
        NameNamesAPassword(text)
        || text.Contains("lösenord", StringComparison.OrdinalIgnoreCase)
        || text.Contains("losenord", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<Symbol> Symbols(AssemblyDefinition assembly)
    {
        foreach (var literal in AttributeStrings(assembly))
            yield return new Symbol(Arm.Literal, literal);

        foreach (var module in assembly.Modules)
        {
            foreach (var literal in AttributeStrings(module))
                yield return new Symbol(Arm.Literal, literal);

            // GetTypes() includes nested and compiler-generated types.
            foreach (var type in module.GetTypes())
            {
                yield return new Symbol(Arm.Declaration, type.FullName);
                foreach (var literal in AttributeStrings(type))
                    yield return new Symbol(Arm.Literal, literal);
                foreach (var generic in type.GenericParameters)
                    yield return new Symbol(Arm.Declaration, generic.Name);
                foreach (var signature in Unwrapped(type.BaseType).Concat(type.Interfaces.SelectMany(i => Unwrapped(i.InterfaceType))))
                    yield return new Symbol(Arm.Signature, signature);

                foreach (var method in type.Methods)
                {
                    yield return new Symbol(Arm.Declaration, method.Name);
                    foreach (var literal in AttributeStrings(method).Concat(AttributeStrings(method.MethodReturnType)))
                        yield return new Symbol(Arm.Literal, literal);
                    if (method.HasBody)
                    {
                        foreach (var instruction in method.Body.Instructions)
                        {
                            if (instruction.OpCode == OpCodes.Ldstr && instruction.Operand is string literal)
                                yield return new Symbol(Arm.Literal, literal);
                        }
                    }
                    foreach (var generic in method.GenericParameters)
                        yield return new Symbol(Arm.Declaration, generic.Name);
                    foreach (var signature in Unwrapped(method.ReturnType))
                        yield return new Symbol(Arm.Signature, signature);
                    foreach (var parameter in method.Parameters)
                    {
                        yield return new Symbol(Arm.Declaration, parameter.Name);
                        foreach (var literal in AttributeStrings(parameter))
                            yield return new Symbol(Arm.Literal, literal);
                        foreach (var signature in Unwrapped(parameter.ParameterType))
                            yield return new Symbol(Arm.Signature, signature);
                    }
                }

                foreach (var field in type.Fields)
                {
                    yield return new Symbol(Arm.Declaration, field.Name);
                    foreach (var literal in AttributeStrings(field))
                        yield return new Symbol(Arm.Literal, literal);
                    foreach (var signature in Unwrapped(field.FieldType))
                        yield return new Symbol(Arm.Signature, signature);
                    if (field.HasConstant && field.Constant is string constant)
                        yield return new Symbol(Arm.Constant, constant);
                }

                foreach (var property in type.Properties)
                {
                    yield return new Symbol(Arm.Declaration, property.Name);
                    foreach (var literal in AttributeStrings(property))
                        yield return new Symbol(Arm.Literal, literal);
                    foreach (var signature in Unwrapped(property.PropertyType))
                        yield return new Symbol(Arm.Signature, signature);
                }

                foreach (var @event in type.Events)
                {
                    yield return new Symbol(Arm.Declaration, @event.Name);
                    foreach (var literal in AttributeStrings(@event))
                        yield return new Symbol(Arm.Literal, literal);
                    foreach (var signature in Unwrapped(@event.EventType))
                        yield return new Symbol(Arm.Signature, signature);
                }
            }

            // The reference tables carry every operand a method body takes from another module, so no
            // instruction walk is needed.
            foreach (var reference in module.GetTypeReferences())
                yield return new Symbol(Arm.Reference, reference.FullName);
            foreach (var reference in module.GetMemberReferences())
                yield return new Symbol(Arm.Reference, reference.FullName);
        }
    }

    private static IEnumerable<string> AttributeStrings(ICustomAttributeProvider provider) =>
        provider.CustomAttributes
            .SelectMany(a => a.ConstructorArguments
                .Concat(a.Properties.Select(p => p.Argument))
                .Concat(a.Fields.Select(f => f.Argument)))
            .SelectMany(ArgumentStrings);

    private static IEnumerable<string> ArgumentStrings(CustomAttributeArgument argument) => argument.Value switch
    {
        string text => [text],
        CustomAttributeArgument[] items => items.SelectMany(ArgumentStrings),
        CustomAttributeArgument boxed => ArgumentStrings(boxed),
        _ => [],
    };

    private static IEnumerable<string> Unwrapped(TypeReference? type)
    {
        if (type is null)
            yield break;

        yield return type.FullName;

        switch (type)
        {
            case GenericInstanceType generic:
                foreach (var name in Unwrapped(generic.ElementType).Concat(generic.GenericArguments.SelectMany(Unwrapped)))
                    yield return name;
                break;
            case TypeSpecification specification:
                // Arrays, by-ref, pointers, pinned and modified types.
                foreach (var name in Unwrapped(specification.ElementType))
                    yield return name;
                break;
        }
    }
}
