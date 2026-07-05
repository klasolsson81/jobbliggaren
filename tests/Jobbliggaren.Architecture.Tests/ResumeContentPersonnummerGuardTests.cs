using System.Reflection;
using Jobbliggaren.Application.Resumes.Commands.CreateResume;
using Jobbliggaren.Application.Resumes.Commands.PromoteParsedResume;
using Jobbliggaren.Application.Resumes.Commands.RenameResume;
using Jobbliggaren.Application.Resumes.Commands.UpdateMasterContent;
using Jobbliggaren.Application.Resumes.Queries;
using Jobbliggaren.Domain.Resumes;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #499/#650 (ADR 0074 Invariant 1 — "no MVP exception path"; CTO Q3 = Approach H) — the
/// resume-content personnummer-guard call-boundary tripwire.
///
/// <para>
/// Invariant: EVERY command handler that is a resume-content WRITE SURFACE must call the shared
/// <c>ResumeContentPersonnummerGuard</c> on the composed content BEFORE it becomes canonical
/// <c>Resume</c> content — otherwise a personnummer typed into a resume-write payload reaches an
/// unflagged canonical Resume (render/PDF). A handler is a subject when EITHER probe hits
/// (#650 widened key, union):
/// (a) the reflection probe — its command carries a user-submitted <see cref="ResumeContentDto"/>
///     anywhere in the public property graph (the original #499 key), OR
/// (b) the Cecil sink probe — the handler (transitively, within the Application module) calls a
///     <c>Resume</c>/<c>ResumeVersion</c> member taking a Domain <c>ResumeContent</c> parameter.
/// Probe (b) closes the escape probe (a) had: a future TargetId-based apply command (ids + frame
/// inputs only, no <see cref="ResumeContentDto"/> on the command) that composes content
/// server-side and hands it to the aggregate would never carry the DTO, yet it IS a write
/// surface — the sink call is the invariant point, so the sink is the key. Both probes (and the
/// guard-call probe) run over a bounded transitive reachable set — seed methods plus every
/// call target DECLARED IN THE Application module, async state machines included — so a handler
/// that delegates the sink or the guard call to an Application helper is still classified
/// correctly. The two present subjects are <c>UpdateMasterContentCommandHandler</c> (#499) and
/// <c>PromoteParsedResumeCommandHandler</c> (DQ6), pinned by staleness anchors below.
/// </para>
///
/// <para>
/// The guard was CTO-bound to the Application boundary (not the aggregate) because it is a
/// cross-cutting input-sanitisation policy over the RAW free text, so this build-time tripwire is
/// the fail-closed backstop: a new resume-content-writing handler that skips the guard fails the
/// build. Backstopped by per-handler unit tests (the arch test proves the call exists; the unit
/// tests prove it blocks). Mono.Cecil IL-scan, mirroring <c>ConnectionStringLeakageTests</c>.
/// Known residuals (documented, not solved here):
/// (1) Non-Mediator write paths — code that reaches the
/// Resume sinks without implementing <c>ICommandHandler</c> in the Application assembly — stay
/// outside the subject set; CQRS discipline (§2.3) keeps every product write surface a command
/// handler.
/// (2) The transitive walk is bounded to the Application module — a sink call delegated to a
/// Domain collaborator (a Domain service that composes content and invokes the aggregate itself)
/// is outside the walk; a narrower cousin of the non-Mediator residual, tracked in issue #669.
/// (3) Virtual/interface dispatch is not devirtualized — the walk follows the callsite's declared
/// target, so a handler that mutates via an injected Application interface (the callsite names
/// the interface, the sink lives only in the implementation) would escape both probes. Contrived
/// today; revisit when the first id-based apply handler lands (epic #649 PR-7/#656).
/// When that first id-based apply handler exists, a positive walker test pinning a
/// helper-DELEGATED sink (handler → Application helper → Resume sink) should be added alongside
/// the staleness anchors below.
/// </para>
/// </summary>
public class ResumeContentPersonnummerGuardTests
{
    private const string GuardTypeName = "ResumeContentPersonnummerGuard";

    // Sink identity (#650): a call to a member DECLARED on Resume/ResumeVersion taking a Domain
    // ResumeContent parameter. Anchored on typeof(...).FullName so a rename of the aggregate or
    // the value object breaks THIS FILE's compilation (loud), never silently empties the probe.
    private static readonly string[] SinkDeclaringTypeNames =
    [
        typeof(Resume).FullName!,
        typeof(ResumeVersion).FullName!,
    ];

    private static readonly string SinkParameterTypeName = typeof(ResumeContent).FullName!;

    // Explicit, reason-carrying exemptions. Fail-safe default (Saltzer & Schroeder): a new
    // resume-content-writing handler lands in NEITHER this set nor the "calls the guard"
    // set and fails the build until a human classifies it. Empty today — every such handler
    // must guard.
    private static readonly HashSet<string> ExemptHandlers = new(StringComparer.Ordinal);

    [Fact]
    public void EveryResumeContentWritingCommandHandler_MustCallTheSharedGuard()
    {
        var appAssembly = typeof(ResumeContentDto).Assembly;
        using var module = ModuleDefinition.ReadModule(appAssembly.Location);

        var subjects = new List<string>();
        var offenders = new List<string>();

        foreach (var handler in FindCommandHandlerTypes(appAssembly))
        {
            var reachable = ReachableMethodsOf(module, handler);

            // Subject key = UNION of the two probes (#650): DTO-carrying command OR
            // (transitive) ResumeContent sink call.
            var carriesDto = HandlerCommandsCarryResumeContent(handler);
            var callsSink = AnyResumeContentSinkCall(reachable);
            if (!carriesDto && !callsSink)
                continue;

            subjects.Add(handler.Name);

            if (ExemptHandlers.Contains(handler.FullName!))
                continue;

            if (!AnyGuardCall(reachable))
                offenders.Add(handler.FullName!);
        }

        // Anchor: the two known subjects must be discovered, else the probes are stale.
        subjects.ShouldContain(nameof(UpdateMasterContentCommandHandler));
        subjects.ShouldContain(nameof(PromoteParsedResumeCommandHandler));

        offenders.ShouldBeEmpty(
            "Every command handler that is a resume-content write surface, keyed on EITHER " +
            "(a) its command carrying a user-submitted ResumeContentDto anywhere in the public " +
            "property graph, OR (b) the handler (transitively, within the Application module) " +
            $"calling a {nameof(Resume)}/{nameof(ResumeVersion)} member that takes a Domain " +
            $"{nameof(ResumeContent)}, MUST run {GuardTypeName} on the composed content before " +
            "the Resume sink call (ADR 0074 Invariant 1, #499/#650). A new such handler must " +
            "call the guard or be added to ExemptHandlers with a reason. Offenders: " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void SinkProbeAlone_DiscoversBothKnownResumeContentSinkCallingHandlers()
    {
        // Staleness anchor for probe (b) ALONE, independent of the DTO probe: if the sink probe
        // ever stops seeing the two known Resume sink calls (Resume.CreateFromParsed in promote,
        // resume.UpdateMasterContent in the master edit), the widened key has silently rotted
        // and a future TargetId-based apply handler would escape discovery.
        using var module = ModuleDefinition.ReadModule(typeof(ResumeContentDto).Assembly.Location);

        AnyResumeContentSinkCall(ReachableMethodsOf(module, typeof(PromoteParsedResumeCommandHandler)))
            .ShouldBeTrue(
                "PromoteParsedResumeCommandHandler calls Resume.CreateFromParsed(..., ResumeContent, ...); " +
                "the sink probe must discover it");
        AnyResumeContentSinkCall(ReachableMethodsOf(module, typeof(UpdateMasterContentCommandHandler)))
            .ShouldBeTrue(
                "UpdateMasterContentCommandHandler calls resume.UpdateMasterContent(ResumeContent, ...); " +
                "the sink probe must discover it");
    }

    [Fact]
    public void DtoProbeAlone_DiscoversBothKnownResumeContentDtoCarryingHandlers()
    {
        // Staleness anchor for probe (a) ALONE, symmetric to the sink-probe anchor above: both
        // known subjects' commands carry a user-submitted ResumeContentDto, so the reflection
        // property-graph probe must discover each of them without any help from the sink probe.
        // If it ever stops, the original #499 key has silently rotted and the union would be
        // carried by probe (b) alone.
        HandlerCommandsCarryResumeContent(typeof(PromoteParsedResumeCommandHandler))
            .ShouldBeTrue(
                "PromoteParsedResumeCommand carries a ResumeContentDto; " +
                "the DTO probe must discover it");
        HandlerCommandsCarryResumeContent(typeof(UpdateMasterContentCommandHandler))
            .ShouldBeTrue(
                "UpdateMasterContentCommand carries a ResumeContentDto; " +
                "the DTO probe must discover it");
    }

    [Fact]
    public void GuardProbe_ReturnsTrue_ForAHandlerKnownToCallTheGuard()
    {
        // Positive anchor for the guard-call probe (the counterpart of the false-negative
        // control below): UpdateMasterContentCommandHandler calls the shared guard, so the
        // probe must find the call in its reachable set. If a probe regression made AnyGuardCall
        // return false unconditionally, the main tripwire would flag every subject as an
        // offender — loud — but THIS anchor names the probe itself as the broken part.
        using var module = ModuleDefinition.ReadModule(typeof(ResumeContentDto).Assembly.Location);

        AnyGuardCall(ReachableMethodsOf(module, typeof(UpdateMasterContentCommandHandler)))
            .ShouldBeTrue(
                $"UpdateMasterContentCommandHandler calls {GuardTypeName}.Check(...); " +
                "the guard-call probe must discover it");
    }

    [Fact]
    public void SinkProbe_ReturnsFalse_ForResumeHandlersThatNeverPassResumeContentToTheAggregate()
    {
        // Negative control (the probe must be proven selective, not just to hit): both handlers
        // touch the Resume aggregate — CreateResumeCommandHandler calls Resume.Create(name,
        // fullName, ...) and RenameResumeCommandHandler calls resume.Rename(name, ...) — but
        // neither passes a Domain ResumeContent, so the sink probe must NOT classify them.
        using var module = ModuleDefinition.ReadModule(typeof(ResumeContentDto).Assembly.Location);

        AnyResumeContentSinkCall(ReachableMethodsOf(module, typeof(CreateResumeCommandHandler)))
            .ShouldBeFalse("Resume.Create takes no ResumeContent, so it is not a sink");
        AnyResumeContentSinkCall(ReachableMethodsOf(module, typeof(RenameResumeCommandHandler)))
            .ShouldBeFalse("Resume.Rename takes no ResumeContent, so it is not a sink");
    }

    [Fact]
    public void GuardProbe_ReturnsFalse_ForAHandlerThatDoesNotCallTheGuard()
    {
        // Negative control (the tripwire must be proven to TRIP, not just to pass): if a future
        // bug made the guard probe return true unconditionally, the fail-closed backstop would
        // silently fail-open — a green build while a handler drops the guard.
        // CreateResumeCommandHandler creates a Resume from a name, never calls the guard, so the
        // reachable-set IL-scan (own methods + async state machine + followed Application-module
        // calls) must return false for it.
        using var module = ModuleDefinition.ReadModule(typeof(ResumeContentDto).Assembly.Location);

        AnyGuardCall(ReachableMethodsOf(module, typeof(CreateResumeCommandHandler))).ShouldBeFalse();
    }

    // ===============================================================
    // Subject discovery — probe (a): reflection over the command's property graph
    // ===============================================================

    private static IEnumerable<Type> FindCommandHandlerTypes(Assembly asm)
    {
        foreach (var type in SafeGetTypes(asm))
        {
            if (type.IsAbstract || type.IsInterface)
                continue;

            if (type.GetInterfaces().Any(IsCommandHandlerInterface))
                yield return type;
        }
    }

    // Match Mediator's ICommandHandler<...> by name (any arity) so we do not depend on the
    // exact generic-arity type existing in this Mediator version. The command is always the
    // first generic argument.
    private static bool IsCommandHandlerInterface(Type iface) =>
        iface.IsGenericType
        && iface.GetGenericTypeDefinition().Name.StartsWith("ICommandHandler", StringComparison.Ordinal);

    private static bool HandlerCommandsCarryResumeContent(Type handlerType) =>
        handlerType.GetInterfaces()
            .Where(IsCommandHandlerInterface)
            .Select(iface => iface.GetGenericArguments()[0])
            .Any(CommandCarriesResumeContent);

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

    // ===============================================================
    // Bounded transitive reachable-set walk (#650) — shared by probe (b) and the guard probe
    // ===============================================================

    private static HashSet<MethodDefinition> ReachableMethodsOf(
        ModuleDefinition module, Type handlerType)
    {
        // Reflection nests with '+', Cecil with '/' — normalize so a nested handler still resolves.
        var typeDef = module.GetType(handlerType.FullName!.Replace('+', '/'));
        return typeDef is null ? [] : ComputeReachableMethods(typeDef, module);
    }

    // Seed = the root type's own methods plus every nested type's methods (an async handler
    // compiles its body into a nested state machine <Handle>d__N.MoveNext; lambdas live in
    // nested display classes), then transitively FOLLOW every method-reference operand
    // (call/callvirt/newobj/ldftn/...) into methods DECLARED IN THE SAME Application module,
    // expanding each followed method with its own compiler-generated async/iterator state
    // machine. The visited set terminates the walk (cycle-safe) and the module boundary bounds
    // it — Domain/BCL/EF targets are never entered. This closes the escape the per-type scan
    // had: a handler that delegates the sink call or the guard call to an Application helper
    // is still classified correctly.
    private static HashSet<MethodDefinition> ComputeReachableMethods(
        TypeDefinition root, ModuleDefinition module)
    {
        var visited = new HashSet<MethodDefinition>();
        var queue = new Queue<MethodDefinition>();

        void Enqueue(MethodDefinition method)
        {
            if (visited.Add(method))
                queue.Enqueue(method);
        }

        foreach (var method in MethodsIncludingNestedTypes(root))
            Enqueue(method);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!current.HasBody)
                continue;

            foreach (var instruction in current.Body.Instructions)
            {
                if (instruction.Operand is not MethodReference mref)
                    continue;

                var target = ResolveIfDeclaredInModule(mref, module);
                if (target is null)
                    continue;

                Enqueue(target);

                // A followed async/iterator method's real body lives in a compiler-generated
                // nested state machine type — include it, or a delegated async helper's
                // sink/guard calls would be invisible to both probes.
                if (StateMachineTypeOf(target, module) is { } stateMachine)
                {
                    foreach (var method in MethodsIncludingNestedTypes(stateMachine))
                        Enqueue(method);
                }
            }
        }

        return visited;
    }

    private static IEnumerable<MethodDefinition> MethodsIncludingNestedTypes(TypeDefinition type)
    {
        foreach (var method in type.Methods)
            yield return method;

        foreach (var nested in type.NestedTypes)
        {
            foreach (var method in MethodsIncludingNestedTypes(nested))
                yield return method;
        }
    }

    // Resolves a method reference ONLY when its declaring type is defined in the given module
    // (the walk's boundary). External references (Domain, EF, BCL, Mediator) return null without
    // any resolution attempt, so the walk never depends on an assembly resolver for them.
    private static MethodDefinition? ResolveIfDeclaredInModule(
        MethodReference mref, ModuleDefinition module)
    {
        if (mref is MethodDefinition definition)
            return definition.Module == module ? definition : null;

        // GetElementType() unwraps generic instances/arrays to the raw declaring type, whose
        // Scope IS the module itself for same-module types.
        if (mref.DeclaringType?.GetElementType().Scope != module)
            return null;

        try
        {
            var resolved = mref.Resolve();
            return resolved?.Module == module ? resolved : null;
        }
        catch (AssemblyResolutionException)
        {
            return null;
        }
    }

    // The compiler stamps an async/iterator method with [AsyncStateMachine(typeof(<M>d__N))] /
    // [IteratorStateMachine(...)] naming its nested state machine type — the precise link from
    // a followed method to where its real body lives.
    private static TypeDefinition? StateMachineTypeOf(MethodDefinition method, ModuleDefinition module)
    {
        foreach (var attribute in method.CustomAttributes)
        {
            if (attribute.AttributeType.Name
                is not ("AsyncStateMachineAttribute" or "IteratorStateMachineAttribute"))
            {
                continue;
            }

            if (attribute.ConstructorArguments is [{ Value: TypeReference stateMachineType }]
                && stateMachineType.GetElementType().Scope == module)
            {
                return stateMachineType.Resolve();
            }
        }

        return null;
    }

    // ===============================================================
    // Probes over the reachable set
    // ===============================================================

    // Probe (b) (#650): a Call/Callvirt whose target is DECLARED on Resume/ResumeVersion and
    // takes a Domain ResumeContent parameter — the canonical-content sink the guard must precede.
    private static bool AnyResumeContentSinkCall(IReadOnlyCollection<MethodDefinition> methods)
    {
        foreach (var method in methods)
        {
            if (!method.HasBody)
                continue;

            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)
                    continue;

                if (instruction.Operand is MethodReference mref
                    && mref.DeclaringType?.FullName is { } declaringType
                    && SinkDeclaringTypeNames.Contains(declaringType, StringComparer.Ordinal)
                    && mref.Parameters.Any(p =>
                        string.Equals(p.ParameterType.FullName, SinkParameterTypeName, StringComparison.Ordinal)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool AnyGuardCall(IReadOnlyCollection<MethodDefinition> methods)
    {
        foreach (var method in methods)
        {
            if (!method.HasBody)
                continue;

            foreach (var instruction in method.Body.Instructions)
            {
                if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
                    && instruction.Operand is MethodReference mref
                    && string.Equals(mref.DeclaringType?.Name, GuardTypeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
