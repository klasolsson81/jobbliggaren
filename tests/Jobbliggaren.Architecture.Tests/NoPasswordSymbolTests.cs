using Jobbliggaren.Api.Configuration;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Identity;
using Jobbliggaren.Worker.Auditing;
using Microsoft.AspNetCore.Identity;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// ADR 0142 parts 5a (#1743) and 5b (#1857) — no password symbol is reachable from the auth surface: declared,
/// carried in a signature, referenced from another module, named by a resolved Identity parameter, or held as a
/// string constant, literal or attribute argument. One instrument, Mono.Cecil (senior-cto-advisor F3, 2026-09-25;
/// scope revised in 5b's form round).
/// <para>
/// <b>Scope, per assembly.</b> Domain, Application, Api, Worker and Infrastructure. Migrate is outside it: its
/// master password is infrastructure, not the auth surface.
/// </para>
/// <para>
/// <b>Role rule, in every assembly.</b> A type is judged by its outermost declaring type, and a compiler-generated
/// top-level type by the types whose bodies reach it; one no body reaches stays in scope. A migration, a model
/// snapshot and a design-time context factory are outside the scope: they run only under Migrate or
/// <c>dotnet ef</c>, and they name <c>password_hash</c> because the column exists. A reference to a member of a type
/// this module defines is judged as that type is.
/// </para>
/// <para>
/// <b>Target rule, in Infrastructure only.</b> Every member reference counts except one whose declaring type is
/// <c>StackExchange.Redis.ConfigurationOptions</c>: a backing service's credential is not a user's. The rule fails
/// closed, so a third-party member that names a password is red until it is decided here.
/// </para>
/// <para>
/// <b>Token-level normalisation.</b> Every <c>passwordless</c> is removed before <c>password</c> is searched for,
/// so <c>CreatePasswordlessUserAsync</c> passes while a name carrying <c>password</c> beside it does not.
/// </para>
/// </summary>
public class NoPasswordSymbolTests
{
    private const string Passwordless = "passwordless";

    private const string BackingServiceCredentialType = "StackExchange.Redis.ConfigurationOptions";
    private const string BackingServiceCredentialScope = "StackExchange.Redis";

    public static TheoryData<Type, bool> AuthSurfaceAssemblies() => new()
    {
        { typeof(JobSeeker), false },
        { typeof(AuthErrorCodes), false },
        { typeof(HstsOptions), false },
        { typeof(WorkerSystemUser), false },
        { typeof(ApplicationUser), true },
    };

    [Theory]
    [MemberData(nameof(AuthSurfaceAssemblies))]
    public void No_password_symbol_is_declared_referenced_or_held_as_a_constant(
        Type assemblyMarker, bool backingServiceCredentialsOutOfScope)
    {
        var scan = ScanAssembly(assemblyMarker, backingServiceCredentialsOutOfScope);
        var name = scan.AssemblyName;

        scan.Symbols.ShouldNotBeEmpty($"{name}: nothing was scanned, so an empty offender list proves nothing");
        scan.Symbols.ShouldContain(s => s.Arm == Arm.Literal, $"{name}: no ldstr literal was read");
        scan.Symbols.ShouldContain(s => s.Arm == Arm.Attribute, $"{name}: no attribute argument was read");

        var offenders = scan.Symbols
            .Where(s => s.Arm is Arm.Constant or Arm.Literal or Arm.Attribute
                ? ConstantNamesAPassword(s.Text)
                : NameNamesAPassword(s.Text))
            .Select(s => $"{s.Arm}: {s.Text}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty($"{name} still reaches a password symbol:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Infrastructure_is_scanned_where_the_identity_adapter_lives_and_its_target_rule_removes_one_credential()
    {
        var scan = ScanAssembly(typeof(ApplicationUser), backingServiceCredentialsOutOfScope: true);

        scan.Symbols.ShouldContain(
            s => s.Arm == Arm.Declaration
                 && s.Text.EndsWith(".Auth.LoginChallenges.IdentityInboxProofRecorder", StringComparison.Ordinal),
            "the role rule must not scope the adapter the scan exists for out of it");
        scan.Symbols.ShouldContain(
            s => s.Arm == Arm.Reference && s.Text == "Microsoft.AspNetCore.Identity.UserManager`1",
            "the Identity type reference must be counted");
        scan.Symbols.ShouldContain(
            s => s.Arm == Arm.Reference && s.Text.Contains("Microsoft.AspNetCore.Identity.UserManager`1<", StringComparison.Ordinal)
                 && s.Text.Contains("::", StringComparison.Ordinal),
            "the target rule must let the Identity members through");

        scan.TargetRuleRemoved.ShouldAllBe(r => r.Contains(" StackExchange.Redis.ConfigurationOptions::", StringComparison.Ordinal));
        scan.TargetRuleRemoved
            .Where(NameNamesAPassword)
            .Distinct(StringComparer.Ordinal)
            .ShouldBe(["System.String StackExchange.Redis.ConfigurationOptions::get_Password()"]);

        scan.IdentityMembersResolved.ShouldBeGreaterThan(0, "no Identity member was resolved, so no parameter name was judged");
        scan.Symbols.ShouldContain(
            s => s.Arm == Arm.ResolvedParameter && s.Text == "user",
            "a resolved Identity member must yield its parameter names (UpdateSecurityStampAsync(TUser user) is called)");
        scan.OwnInScopeMembersCounted.ShouldBeGreaterThan(
            0, "no reference to a member of an in-scope type of this module was counted, so the rule for them is unmeasured");
    }

    [Theory]
    [InlineData("Jobbliggaren.Infrastructure.Identity.Migrations.AppIdentityDbContextModelSnapshot", false)]
    [InlineData("Jobbliggaren.Infrastructure.Identity.Migrations.AppIdentityDbContextModelSnapshot/<>c", false)]
    [InlineData("Jobbliggaren.Infrastructure.Identity.Migrations.DropAuthProviderColumns", false)]
    [InlineData("Jobbliggaren.Infrastructure.Identity.DesignTimeIdentityDbContextFactory", false)]
    [InlineData("Jobbliggaren.Infrastructure.Identity.ApplicationUser", true)]
    [InlineData("Jobbliggaren.Infrastructure.Auth.LoginChallenges.IdentityInboxProofRecorder/<RecordAsync>d__", true)]
    public void The_role_rule_scopes_a_type_by_its_outermost_declaring_type(string typeName, bool inScope)
    {
        using var loaded = ReadAssembly(typeof(ApplicationUser));
        var module = loaded.Definition.MainModule;
        var reachers = Reachers(module);

        // A state machine's name ends in a compiler-chosen number, so a name ending in "__" is a prefix.
        var type = module.GetTypes().Single(t => typeName.EndsWith("__", StringComparison.Ordinal)
            ? t.FullName.StartsWith(typeName, StringComparison.Ordinal)
            : t.FullName == typeName);

        InScope(type, reachers).ShouldBe(inScope);
    }

    [Fact]
    public void A_compiler_generated_top_level_type_is_scoped_by_the_bodies_that_reach_it()
    {
        using var loaded = ReadAssembly(typeof(ApplicationUser));
        var module = loaded.Definition.MainModule;
        var reachers = Reachers(module);
        var anonymous = module.GetTypes()
            .Where(t => t.DeclaringType is null && IsCompilerGenerated(t) && t.Name.Contains("AnonymousType", StringComparison.Ordinal))
            .ToList();

        var reachedOnlyFromSchema = anonymous.Where(t =>
            reachers.TryGetValue(t, out var owners) && owners.All(IsSchemaOrDesignTime)).ToList();
        var reachedFromScope = anonymous.Where(t =>
            reachers.TryGetValue(t, out var owners) && owners.Any(o => !IsSchemaOrDesignTime(o))).ToList();

        reachedOnlyFromSchema.ShouldNotBeEmpty("InitialIdentity's column builder is an anonymous type only a migration reaches");
        reachedOnlyFromSchema.ShouldAllBe(t => !InScope(t, reachers));
        reachedFromScope.ShouldNotBeEmpty("an anonymous type an in-scope body builds must exist to prove this half");
        reachedFromScope.ShouldAllBe(t => InScope(t, reachers));
    }

    [Fact]
    public void The_normalisation_strips_the_passwordless_symbols_rather_than_skipping_them()
    {
        var scan = ScanAssembly(typeof(AuthErrorCodes), backingServiceCredentialsOutOfScope: false);

        scan.Symbols
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
        ResolvedParameter,
        Constant,
        Literal,
        Attribute,
    }

    private sealed record Symbol(Arm Arm, string Text);

    private sealed record Scan(
        string AssemblyName,
        IReadOnlyList<Symbol> Symbols,
        IReadOnlyList<string> TargetRuleRemoved,
        int IdentityMembersResolved,
        int OwnInScopeMembersCounted);

    private static bool NameNamesAPassword(string text) =>
        text.Replace(Passwordless, string.Empty, StringComparison.OrdinalIgnoreCase)
            .Contains("password", StringComparison.OrdinalIgnoreCase);

    private static bool ConstantNamesAPassword(string text) =>
        NameNamesAPassword(text)
        || text.Contains("lösenord", StringComparison.OrdinalIgnoreCase)
        || text.Contains("losenord", StringComparison.OrdinalIgnoreCase);

    // Cecil does not dispose a resolver passed in through ReaderParameters, and the resolve arm opens the
    // Identity assemblies through it, so the pair is disposed together.
    private sealed record LoadedAssembly(AssemblyDefinition Definition, DefaultAssemblyResolver Resolver) : IDisposable
    {
        public void Dispose()
        {
            Definition.Dispose();
            Resolver.Dispose();
        }
    }

    private static LoadedAssembly ReadAssembly(Type marker)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(AppContext.BaseDirectory);
        // The shared framework is not copied to the test's bin, and Identity lives there.
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(UserManager<>).Assembly.Location)!);
        return new LoadedAssembly(
            AssemblyDefinition.ReadAssembly(marker.Assembly.Location, new ReaderParameters { AssemblyResolver = resolver }),
            resolver);
    }

    private static Scan ScanAssembly(Type marker, bool backingServiceCredentialsOutOfScope)
    {
        using var loaded = ReadAssembly(marker);
        var assembly = loaded.Definition;
        var symbols = new List<Symbol>();
        var removed = new List<string>();
        var resolved = 0;
        var ownInScope = 0;

        symbols.AddRange(AttributeStrings(assembly).Select(literal => new Symbol(Arm.Attribute, literal)));

        foreach (var module in assembly.Modules)
        {
            symbols.AddRange(AttributeStrings(module).Select(literal => new Symbol(Arm.Attribute, literal)));

            var reachers = Reachers(module);

            // GetTypes() includes nested and compiler-generated types.
            foreach (var type in module.GetTypes().Where(t => InScope(t, reachers)))
                symbols.AddRange(TypeSymbols(type));

            // The reference tables carry every operand a method body takes from another module, so no
            // instruction walk is needed for them.
            symbols.AddRange(module.GetTypeReferences().Select(reference => new Symbol(Arm.Reference, reference.FullName)));
            foreach (var reference in module.GetMemberReferences())
            {
                // A generic instantiation of a type this module defines is a TypeSpec, so a member on it is a
                // MemberRef even though the declaration arm already judged the member at its definition.
                var element = reference.DeclaringType?.GetElementType();
                if (element?.Scope == module)
                {
                    var definition = element.Resolve()
                        ?? throw new InvalidOperationException($"could not resolve this module's own type {element.FullName}");
                    if (!InScope(definition, reachers))
                        continue;
                    ownInScope++;
                }

                if (backingServiceCredentialsOutOfScope && IsBackingServiceCredential(reference))
                {
                    removed.Add(reference.FullName);
                    continue;
                }

                symbols.Add(new Symbol(Arm.Reference, reference.FullName));

                // A reference's full name carries parameter types, never parameter names, so the retired
                // UserManager.CreateAsync(TUser, string) would pass unread. Identity members are resolved and
                // their parameter names judged.
                if (IsIdentityMember(reference) && reference is MethodReference method)
                {
                    var definition = method.Resolve()
                        ?? throw new InvalidOperationException($"could not resolve the Identity member {method.FullName}");
                    resolved++;
                    symbols.AddRange(definition.Parameters.Select(p => new Symbol(Arm.ResolvedParameter, p.Name)));
                }
            }
        }

        return new Scan(assembly.Name.Name, symbols, removed, resolved, ownInScope);
    }

    private static IEnumerable<Symbol> TypeSymbols(TypeDefinition type)
    {
        yield return new Symbol(Arm.Declaration, type.FullName);
        foreach (var literal in AttributeStrings(type))
            yield return new Symbol(Arm.Attribute, literal);
        foreach (var generic in type.GenericParameters)
            yield return new Symbol(Arm.Declaration, generic.Name);
        foreach (var signature in Unwrapped(type.BaseType).Concat(type.Interfaces.SelectMany(i => Unwrapped(i.InterfaceType))))
            yield return new Symbol(Arm.Signature, signature);

        foreach (var method in type.Methods)
        {
            yield return new Symbol(Arm.Declaration, method.Name);
            foreach (var literal in AttributeStrings(method).Concat(AttributeStrings(method.MethodReturnType)))
                yield return new Symbol(Arm.Attribute, literal);
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
                    yield return new Symbol(Arm.Attribute, literal);
                foreach (var signature in Unwrapped(parameter.ParameterType))
                    yield return new Symbol(Arm.Signature, signature);
            }
        }

        foreach (var field in type.Fields)
        {
            yield return new Symbol(Arm.Declaration, field.Name);
            foreach (var literal in AttributeStrings(field))
                yield return new Symbol(Arm.Attribute, literal);
            foreach (var signature in Unwrapped(field.FieldType))
                yield return new Symbol(Arm.Signature, signature);
            if (field.HasConstant && field.Constant is string constant)
                yield return new Symbol(Arm.Constant, constant);
        }

        foreach (var property in type.Properties)
        {
            yield return new Symbol(Arm.Declaration, property.Name);
            foreach (var literal in AttributeStrings(property))
                yield return new Symbol(Arm.Attribute, literal);
            foreach (var signature in Unwrapped(property.PropertyType))
                yield return new Symbol(Arm.Signature, signature);
        }

        foreach (var @event in type.Events)
        {
            yield return new Symbol(Arm.Declaration, @event.Name);
            foreach (var literal in AttributeStrings(@event))
                yield return new Symbol(Arm.Attribute, literal);
            foreach (var signature in Unwrapped(@event.EventType))
                yield return new Symbol(Arm.Signature, signature);
        }
    }

    private static TypeDefinition Owner(TypeDefinition type)
    {
        while (type.DeclaringType is { } outer)
            type = outer;
        return type;
    }

    private static bool IsSchemaOrDesignTime(TypeDefinition owner) =>
        owner.BaseType?.FullName is "Microsoft.EntityFrameworkCore.Migrations.Migration"
                                 or "Microsoft.EntityFrameworkCore.Infrastructure.ModelSnapshot"
        || owner.Interfaces.Any(i => i.InterfaceType.GetElementType().FullName
                                     == "Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory`1");

    private static bool IsCompilerGenerated(TypeDefinition type) =>
        type.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");

    private static bool InScope(TypeDefinition type, Dictionary<TypeDefinition, HashSet<TypeDefinition>> reachers) =>
        type.DeclaringType is null && IsCompilerGenerated(type) && reachers.TryGetValue(type, out var owners)
            ? owners.Any(owner => !IsSchemaOrDesignTime(owner))
            : !IsSchemaOrDesignTime(Owner(type));

    // For every compiler-generated top-level type of this module, the owners of the bodies that reach it.
    private static Dictionary<TypeDefinition, HashSet<TypeDefinition>> Reachers(ModuleDefinition module)
    {
        var reachers = new Dictionary<TypeDefinition, HashSet<TypeDefinition>>();
        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods.Where(m => m.HasBody))
            {
                foreach (var instruction in method.Body.Instructions)
                {
                    var target = instruction.Operand switch
                    {
                        TypeReference typeReference => typeReference,
                        MemberReference { DeclaringType: { } declaring } => declaring,
                        _ => null,
                    };
                    // A generic instance's element type is a reference even when this module defines it.
                    var element = target?.GetElementType();
                    var reached = element as TypeDefinition ?? (element?.Scope == module ? element.Resolve() : null);
                    var owner = Owner(type);
                    // A type's own methods reaching it say nothing about who uses it.
                    if (reached is { DeclaringType: null } && IsCompilerGenerated(reached) && owner != reached)
                    {
                        if (!reachers.TryGetValue(reached, out var owners))
                            reachers[reached] = owners = [];
                        owners.Add(owner);
                    }
                }
            }
        }
        return reachers;
    }

    private static bool IsBackingServiceCredential(MemberReference reference) =>
        reference.DeclaringType?.GetElementType() is { FullName: BackingServiceCredentialType } declaring
        && declaring.Scope?.Name == BackingServiceCredentialScope;

    private static bool IsIdentityMember(MemberReference reference) =>
        reference.DeclaringType?.GetElementType().Scope?.Name is { } scope
        && (scope.StartsWith("Microsoft.AspNetCore.Identity", StringComparison.Ordinal)
            || scope.StartsWith("Microsoft.Extensions.Identity.", StringComparison.Ordinal));

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
