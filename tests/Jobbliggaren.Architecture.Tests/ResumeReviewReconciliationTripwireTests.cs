using System.Reflection;
using Jobbliggaren.Application.Resumes.Commands.ApplyCvImprovements;
using Jobbliggaren.Application.Resumes.Commands.CreateResume;
using Jobbliggaren.Application.Resumes.Commands.PromoteParsedResume;
using Jobbliggaren.Application.Resumes.Commands.RenameResume;
using Jobbliggaren.Application.Resumes.Commands.SetFindingStatus;
using Jobbliggaren.Application.Resumes.Commands.SetResumeLanguage;
using Jobbliggaren.Application.Resumes.Commands.UpdateMasterContent;
using Jobbliggaren.Application.Resumes.Queries;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Mono.Cecil;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Fas 4b PR-8 (#657, ADR 0093 §D5(b); CTO-bind PR-8 Q1 gate flag).
///
/// <para>
/// Invariant: the DEK-free finding-status ledger is the hub badge's ONLY data source
/// (no engine on the list path, ADR 0045), so EVERY command handler that mutates a
/// review input — master content (<c>UpdateMasterContent</c>/<c>CreateFromParsed</c>/
/// <c>Create</c>) or the review language (<c>SetLanguage</c>) — must reconcile the
/// ledger via <c>IResumeReviewReconciler</c> in the same transaction. A write path that
/// skips the reconciler silently desyncs every badge derived from that CV.
/// </para>
///
/// <para>
/// Form (#650 precedent, mirroring <c>ResumeContentPersonnummerGuardTests</c>): a
/// bounded transitive IL walk (Mono.Cecil) over each command handler's reachable
/// Application-module methods. Subject key = a call to one of the four review-input
/// members DECLARED on <c>Resume</c>; requirement = a reachable call to
/// <c>IResumeReviewReconciler.ReconcileAsync</c>. Fail-safe default: a NEW review-input
/// write handler fails this test until it reconciles or a human exempts it here with a
/// reason.
/// </para>
/// </summary>
public class ResumeReviewReconciliationTripwireTests
{
    private static readonly string ResumeTypeName = typeof(Resume).FullName!;
    private static readonly string ReconcilerTypeName = typeof(IResumeReviewReconciler).FullName!;

    // Anchored via nameof so a rename of any sink member breaks THIS FILE's compilation
    // (loud), never silently empties the probe.
    private static readonly HashSet<string> ReviewInputSinkNames = new(StringComparer.Ordinal)
    {
        nameof(Resume.UpdateMasterContent),
        nameof(Resume.CreateFromParsed),
        nameof(Resume.Create),
        nameof(Resume.SetLanguage),
    };

    // Explicit, reason-carrying exemptions (Saltzer & Schroeder fail-safe default).
    // Empty today — every review-input write handler must reconcile.
    private static readonly HashSet<string> ExemptHandlers = new(StringComparer.Ordinal);

    [Fact]
    public void EveryReviewInputWritingCommandHandler_MustCallTheReconciler()
    {
        var appAssembly = typeof(ResumeContentDto).Assembly;
        using var module = ModuleDefinition.ReadModule(appAssembly.Location);

        var subjects = new List<string>();
        var offenders = new List<string>();

        foreach (var handler in FindCommandHandlerTypes(appAssembly))
        {
            var reachable = ReachableMethodsOf(module, handler);
            if (!AnyReviewInputSinkCall(reachable))
                continue;

            subjects.Add(handler.Name);

            if (ExemptHandlers.Contains(handler.FullName!))
                continue;

            if (!AnyReconcilerCall(reachable))
                offenders.Add(handler.FullName!);
        }

        // Staleness anchors: the five known review-input writers must be discovered,
        // else the sink probe has rotted (loud, not silent).
        subjects.ShouldContain(nameof(UpdateMasterContentCommandHandler));
        subjects.ShouldContain(nameof(PromoteParsedResumeCommandHandler));
        subjects.ShouldContain(nameof(CreateResumeCommandHandler));
        subjects.ShouldContain(nameof(SetResumeLanguageCommandHandler));
        subjects.ShouldContain(nameof(ApplyCvImprovementsCommandHandler));

        offenders.ShouldBeEmpty(
            "Every command handler that (transitively, within the Application module) " +
            $"calls a review-input member declared on {nameof(Resume)} " +
            "(UpdateMasterContent/CreateFromParsed/Create/SetLanguage) MUST call " +
            $"{nameof(IResumeReviewReconciler)}.{nameof(IResumeReviewReconciler.ReconcileAsync)} " +
            "in the same handler flow (ADR 0093 §D5(b): the ledger is the badge's only " +
            "source — an unreconciled write silently desyncs the hub). A new such " +
            "handler must reconcile or be added to ExemptHandlers with a reason. " +
            "Offenders: " + string.Join(", ", offenders));
    }

    [Fact]
    public void SinkProbe_IsSelective_NonReviewInputHandlersAreNotSubjects()
    {
        // Negative controls (the probe must be proven selective, not just to hit):
        // Rename mutates only the CV label; SetFindingStatus records a user decision
        // (SetFindingStatus is deliberately NOT a review-input sink — the CTO-bound
        // call-site set is exactly the five content/language writers).
        using var module = ModuleDefinition.ReadModule(typeof(ResumeContentDto).Assembly.Location);

        AnyReviewInputSinkCall(ReachableMethodsOf(module, typeof(RenameResumeCommandHandler)))
            .ShouldBeFalse("Resume.Rename is not a review-input sink");
        AnyReviewInputSinkCall(ReachableMethodsOf(module, typeof(SetFindingStatusCommandHandler)))
            .ShouldBeFalse(
                "Resume.SetFindingStatus is the user-decision path, not a review-input " +
                "write — reconciling there would be a design change, not a drive-by");
    }

    [Fact]
    public void ReconcilerProbe_ReturnsTrue_ForAHandlerKnownToReconcile()
    {
        // Positive anchor for the reconciler-call probe: if a probe regression made
        // AnyReconcilerCall return false unconditionally, the main tripwire would flag
        // every subject — loud — but THIS anchor names the probe itself as broken.
        using var module = ModuleDefinition.ReadModule(typeof(ResumeContentDto).Assembly.Location);

        AnyReconcilerCall(ReachableMethodsOf(module, typeof(UpdateMasterContentCommandHandler)))
            .ShouldBeTrue(
                "UpdateMasterContentCommandHandler calls IResumeReviewReconciler.ReconcileAsync; " +
                "the reconciler-call probe must discover it");
    }

    [Fact]
    public void ReconcilerProbe_ReturnsFalse_ForAHandlerThatDoesNotReconcile()
    {
        // Negative control (the tripwire must be proven to TRIP): Rename never touches
        // review inputs and never reconciles — a probe that returned true here would
        // have silently failed open.
        using var module = ModuleDefinition.ReadModule(typeof(ResumeContentDto).Assembly.Location);

        AnyReconcilerCall(ReachableMethodsOf(module, typeof(RenameResumeCommandHandler)))
            .ShouldBeFalse("RenameResumeCommandHandler must not need the reconciler");
    }

    // ===============================================================
    // Probes
    // ===============================================================

    private static bool AnyReviewInputSinkCall(HashSet<MethodDefinition> reachable) =>
        reachable.Any(method => method.HasBody && method.Body.Instructions.Any(instruction =>
            instruction.Operand is MethodReference mref
            && mref.DeclaringType?.GetElementType().FullName == ResumeTypeName
            && ReviewInputSinkNames.Contains(mref.Name)));

    private static bool AnyReconcilerCall(HashSet<MethodDefinition> reachable) =>
        reachable.Any(method => method.HasBody && method.Body.Instructions.Any(instruction =>
            instruction.Operand is MethodReference mref
            && mref.DeclaringType?.GetElementType().FullName == ReconcilerTypeName
            && mref.Name == nameof(IResumeReviewReconciler.ReconcileAsync)));

    // ===============================================================
    // Handler discovery + bounded transitive walk — #650 machinery
    // (mirrors ResumeContentPersonnummerGuardTests; kept local so each tripwire's
    // walk semantics are self-contained and independently pinned by its anchors)
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

    private static bool IsCommandHandlerInterface(Type iface) =>
        iface.IsGenericType
        && iface.GetGenericTypeDefinition().Name.StartsWith("ICommandHandler", StringComparison.Ordinal);

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

    private static HashSet<MethodDefinition> ReachableMethodsOf(
        ModuleDefinition module, Type handlerType)
    {
        var typeDef = module.GetType(handlerType.FullName!.Replace('+', '/'));
        return typeDef is null ? [] : ComputeReachableMethods(typeDef, module);
    }

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

    private static MethodDefinition? ResolveIfDeclaredInModule(
        MethodReference mref, ModuleDefinition module)
    {
        if (mref is MethodDefinition definition)
            return definition.Module == module ? definition : null;

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

    // An async/iterator method's real body lives in a compiler-generated nested state
    // machine type referenced by its [AsyncStateMachine]/[IteratorStateMachine]
    // attribute — resolve it so a delegated async helper's calls stay visible.
    private static TypeDefinition? StateMachineTypeOf(
        MethodDefinition method, ModuleDefinition module)
    {
        foreach (var attribute in method.CustomAttributes)
        {
            if (attribute.AttributeType.Name is not
                ("AsyncStateMachineAttribute" or "IteratorStateMachineAttribute"))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Count == 1
                && attribute.ConstructorArguments[0].Value is TypeReference stateMachineRef
                && stateMachineRef.GetElementType().Scope == module)
            {
                try
                {
                    return stateMachineRef.Resolve();
                }
                catch (AssemblyResolutionException)
                {
                    return null;
                }
            }
        }

        return null;
    }
}
