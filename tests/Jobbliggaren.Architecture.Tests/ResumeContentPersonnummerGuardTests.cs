using System.Reflection;
using Jobbliggaren.Application.Resumes.Commands.CreateResume;
using Jobbliggaren.Application.Resumes.Queries;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #499 (ADR 0074 Invariant 1 — "no MVP exception path"; CTO Q3 = Approach H) — the
/// resume-content personnummer-guard call-boundary tripwire.
///
/// <para>
/// Invariant: EVERY command handler whose command carries a user-submitted
/// <see cref="ResumeContentDto"/> must call the shared
/// <c>ResumeContentPersonnummerGuard</c> on that content BEFORE it becomes canonical
/// <c>Resume</c> content — otherwise a personnummer typed into a resume-write payload
/// reaches an unflagged canonical Resume (render/PDF). The two present subjects are
/// <c>UpdateMasterContentCommandHandler</c> (#499, the fixed gap) and
/// <c>PromoteParsedResumeCommandHandler</c> (DQ6). The guard was CTO-bound to the
/// Application boundary (not the aggregate) because it is a cross-cutting input-sanitisation
/// policy over the RAW DTO free text, so this build-time tripwire is the fail-closed backstop
/// for a FUTURE write surface (e.g. a <c>CreateTailored</c> handler, latent today): a new
/// <see cref="ResumeContentDto"/>-carrying handler that skips the guard fails the build.
/// Backstopped by per-handler unit tests (the arch test proves the call exists; the unit tests
/// prove it blocks). Mono.Cecil IL-scan, mirroring <c>ConnectionStringLeakageTests</c>.
/// </para>
/// </summary>
public class ResumeContentPersonnummerGuardTests
{
    private const string GuardTypeName = "ResumeContentPersonnummerGuard";

    // Explicit, reason-carrying exemptions. Fail-safe default (Saltzer & Schroeder): a new
    // ResumeContentDto-carrying handler lands in NEITHER this set nor the "calls the guard"
    // set and fails the build until a human classifies it. Empty today — every such handler
    // must guard.
    private static readonly HashSet<string> ExemptHandlers = new(StringComparer.Ordinal);

    [Fact]
    public void EveryCommandHandlerCarryingResumeContentDto_MustCallTheSharedGuard()
    {
        var appAssembly = typeof(ResumeContentDto).Assembly;
        var subjects = FindResumeContentCommandHandlers(appAssembly).ToList();

        // Anchor: the two known subjects must be discovered, else the reflection probe is stale.
        var names = subjects.Select(t => t.Name).ToList();
        names.ShouldContain("UpdateMasterContentCommandHandler");
        names.ShouldContain("PromoteParsedResumeCommandHandler");

        using var module = ModuleDefinition.ReadModule(appAssembly.Location);

        var offenders = new List<string>();
        foreach (var handler in subjects)
        {
            if (ExemptHandlers.Contains(handler.FullName!))
                continue;

            var typeDef = module.GetType(handler.FullName);
            if (typeDef is null || !TypeCallsGuard(typeDef))
                offenders.Add(handler.FullName!);
        }

        offenders.ShouldBeEmpty(
            "Every command handler whose command carries a user-submitted ResumeContentDto MUST " +
            $"call {GuardTypeName}.Check on that content before it becomes canonical Resume content " +
            "(ADR 0074 Invariant 1, #499). A new such handler must call the guard or be added to " +
            "ExemptHandlers with a reason. Offenders: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TypeCallsGuard_ReturnsFalse_ForAHandlerThatDoesNotCallTheGuard()
    {
        // Negative control (the tripwire must be proven to TRIP, not just to pass): if a future
        // bug made TypeCallsGuard return true unconditionally, the fail-closed backstop would
        // silently fail-open — a green build while a handler drops the guard. CreateResumeCommandHandler
        // creates a Resume from a name, carries NO ResumeContentDto and never calls the guard, so
        // the IL-scan must return false for it (including its own async state machine).
        using var module = ModuleDefinition.ReadModule(typeof(ResumeContentDto).Assembly.Location);
        var nonCaller = module.GetType(typeof(CreateResumeCommandHandler).FullName);

        nonCaller.ShouldNotBeNull();
        TypeCallsGuard(nonCaller).ShouldBeFalse();
    }

    // A command "carries" resume content if any public instance property is a ResumeContentDto.
    private static IEnumerable<Type> FindResumeContentCommandHandlers(Assembly asm)
    {
        foreach (var type in SafeGetTypes(asm))
        {
            if (type.IsAbstract || type.IsInterface)
                continue;

            foreach (var iface in type.GetInterfaces())
            {
                if (!iface.IsGenericType)
                    continue;

                // Match Mediator's ICommandHandler<...> by name (any arity) so we do not depend
                // on the exact generic-arity type existing in this Mediator version. The command
                // is always the first generic argument.
                if (!iface.GetGenericTypeDefinition().Name.StartsWith("ICommandHandler", StringComparison.Ordinal))
                    continue;

                var commandType = iface.GetGenericArguments()[0];
                if (CommandCarriesResumeContent(commandType))
                {
                    yield return type;
                    break;
                }
            }
        }
    }

    // A command "carries" resume content if a ResumeContentDto appears ANYWHERE in its public
    // property graph — directly, nested inside another DTO, or as a collection/array element —
    // so a future nested- or collection-carried write surface cannot evade the tripwire
    // (fail-closed; the direct-property-only check would silently miss it).
    private static bool CommandCarriesResumeContent(Type commandType) =>
        CarriesResumeContent(commandType, new HashSet<Type>());

    private static bool CarriesResumeContent(Type type, HashSet<Type> visited)
    {
        if (type == typeof(ResumeContentDto))
            return true;

        // Bound the walk: never revisit a type (cycle guard) and only descend into our OWN
        // Application DTO types — a BCL type (string, Guid, DateOnly, primitives) can never
        // transitively carry a DTO, so there is nothing to inspect there.
        if (!visited.Add(type) || type.Assembly != typeof(ResumeContentDto).Assembly)
            return false;

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var candidate in UnwrapTypes(prop.PropertyType))
            {
                if (CarriesResumeContent(candidate, visited))
                    return true;
            }
        }

        return false;
    }

    // The property type itself, plus (for arrays / IEnumerable&lt;T&gt;) its element/argument
    // types — so a List&lt;ResumeContentDto&gt; or a SomeDto[] is unwrapped and inspected.
    private static IEnumerable<Type> UnwrapTypes(Type type)
    {
        yield return type;

        if (type.IsArray && type.GetElementType() is { } elem)
            yield return elem;

        if (type.IsGenericType)
        {
            foreach (var arg in type.GetGenericArguments())
                yield return arg;
        }
    }

    // Scans the type's own methods AND its nested types recursively. An async handler compiles
    // its body into a nested compiler-generated state machine (<Handle>d__N.MoveNext), so the
    // call to the guard lives in a NESTED type, not the handler's own Handle method — missing
    // the nested types would false-flag every async handler that DOES call the guard.
    private static bool TypeCallsGuard(TypeDefinition typeDef)
    {
        foreach (var method in typeDef.Methods)
        {
            if (!method.HasBody)
                continue;

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                    continue;

                if (instruction.Operand is MethodReference mref
                    && string.Equals(mref.DeclaringType?.Name, GuardTypeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        foreach (var nested in typeDef.NestedTypes)
        {
            if (TypeCallsGuard(nested))
                return true;
        }

        return false;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
