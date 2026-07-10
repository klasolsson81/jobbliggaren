using System.Reflection;
using Jobbliggaren.Application.Common.Security;
using Jobbliggaren.Domain.Resumes.Files;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Fas 4b PR-9a (ADR 0093 §D5 / ADR 0100, CTO Q2 = explicit seal) — the three structural
/// guardrails that keep the Form C binary store honest. Unlike Form A/B (whose interceptor
/// pipeline is engaged automatically via <c>EncryptedFieldRegistry</c>), Form C seals
/// EXPLICITLY in the write-path — so the compiler cannot enforce the DEK-warm contract or
/// the no-plaintext model. These pins do:
/// <list type="number">
/// <item><b>Marker-pin:</b> every handler that injects <see cref="IBinaryFieldSealer"/> (write
/// path) or <see cref="IBinaryFieldOpener"/> (PR-9b read path) must handle a message carrying
/// <see cref="IRequiresFieldEncryptionKey"/> — otherwise the prefetch behavior never warms the
/// owner DEK and the port's fail-closed throw fires at runtime instead of the DEK being warm by
/// construction. One parametrised body, one <see cref="System.Reflection"/> sweep per port, each
/// with its own vacuity guard.</item>
/// <item><b>Aggregate-honesty pin:</b> <see cref="ResumeFile"/> never grows a plaintext-bytes
/// surface — the ONLY byte-carrying member is the opaque <c>SealedContent</c> (+ the factory's
/// <c>sealedContent</c> parameter). Multi-MB CV plaintext must never be change-tracked or
/// exposed on the model (§5 minimisation; the streaming read path is PR-9b's, outside the
/// aggregate).</item>
/// <item>(The third guardrail — Domain never references the Form C ports — extends the
/// existing layer-pin in <see cref="FieldEncryptionKeyStoreLayerTests"/>.)</item>
/// </list>
/// </summary>
public class FormCBinaryStoreGuardrailTests
{
    [Fact]
    public void HandlersInjectingBinaryFieldSealer_MustHandleMessagesCarryingTheDekMarker()
        => AssertHandlersInjectingBinaryPort_HandleTheDekMarker(typeof(IBinaryFieldSealer));

    [Fact]
    public void HandlersInjectingBinaryFieldOpener_MustHandleMessagesCarryingTheDekMarker()
        => AssertHandlersInjectingBinaryPort_HandleTheDekMarker(typeof(IBinaryFieldOpener));

    // Fas 4b PR-9b (security-auditor Major 2): the read-path opener needs the SAME structural
    // guarantee as the write-path sealer — a handler injecting either Form C port whose message
    // lacks IRequiresFieldEncryptionKey would fail closed at runtime because the prefetch behavior
    // never warms the owner DEK. Parametrised over the port so both pins share one body; each [Fact]
    // runs the sweep independently, so each carries its OWN vacuity guard (sealer: the import
    // handler; opener: the download handler).
    private static void AssertHandlersInjectingBinaryPort_HandleTheDekMarker(Type binaryPortType)
    {
        var applicationAssembly = typeof(Jobbliggaren.Application.AssemblyMarker).Assembly;

        var offenders = new List<string>();
        var pinnedHandlers = 0;

        foreach (var type in applicationAssembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface)
                continue;

            var injectsPort = type.GetConstructors()
                .Any(c => c.GetParameters()
                    .Any(p => p.ParameterType == binaryPortType));

            if (!injectsPort)
                continue;

            // Every Mediator handler interface on the type: the MESSAGE (first generic
            // argument) must opt in to the DEK prefetch via IRequiresFieldEncryptionKey.
            var handlerInterfaces = type.GetInterfaces()
                .Where(i => i.IsGenericType
                    && i.FullName is not null
                    && (i.GetGenericTypeDefinition().FullName!.StartsWith(
                            "Mediator.ICommandHandler`", StringComparison.Ordinal)
                        || i.GetGenericTypeDefinition().FullName!.StartsWith(
                            "Mediator.IQueryHandler`", StringComparison.Ordinal)
                        || i.GetGenericTypeDefinition().FullName!.StartsWith(
                            "Mediator.IRequestHandler`", StringComparison.Ordinal)))
                .ToList();

            foreach (var handlerInterface in handlerInterfaces)
            {
                pinnedHandlers++;
                var message = handlerInterface.GetGenericArguments()[0];
                if (!typeof(IRequiresFieldEncryptionKey).IsAssignableFrom(message))
                    offenders.Add($"{type.Name} → {message.Name}");
            }

            // A port-injecting type that is not a Mediator handler at all (e.g. a job) has no
            // prefetch pipeline — flag it too: the port would ALWAYS fail closed.
            if (handlerInterfaces.Count == 0)
                offenders.Add($"{type.Name} (injects {binaryPortType.Name} outside the Mediator pipeline)");
        }

        offenders.ShouldBeEmpty(
            $"{binaryPortType.Name} peeks the DEK FieldEncryptionKeyPrefetchBehavior warmed — a "
            + "handler whose message lacks IRequiresFieldEncryptionKey fails closed at "
            + "runtime. Offenders: " + string.Join(", ", offenders));

        // Vacuity guard: at least one handler is pinned today (sealer → import; opener → download);
        // if this ever reads 0 the pin stopped seeing the handlers (reflection drift), which must
        // fail loud, not pass.
        pinnedHandlers.ShouldBeGreaterThan(0,
            $"the marker-pin found no {binaryPortType.Name}-injecting handlers — reflection drift?");
    }

    [Fact]
    public void ResumeFile_NeverExposesAPlaintextBytesMember()
    {
        // Aggregate honesty (CTO Q2): the model holds ONLY the opaque Form C envelope.
        // ANY additional byte-carrying surface (a plaintext property, a decrypt helper, a
        // stream accessor) is a §5-minimisation violation and breaks the sealed-at-
        // construction design — the read path is PR-9b's streaming endpoint, never the model.
        var aggregate = typeof(ResumeFile);

        var byteCarriers = new List<string>();
        // NonPublic included: a future PRIVATE byte[] plaintext field would still be EF-
        // mappable/change-trackable — the honesty pin must see it (dotnet-architect NTH,
        // PR-9a gate). Compiler-generated backing fields for allowed properties are excused.
        foreach (var member in aggregate.GetMembers(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            switch (member)
            {
                case PropertyInfo property when IsByteCarrier(property.PropertyType):
                    if (property.Name != nameof(ResumeFile.SealedContent))
                        byteCarriers.Add($"property {property.Name}");
                    break;

                case FieldInfo field when IsByteCarrier(field.FieldType):
                    if (field.Name != $"<{nameof(ResumeFile.SealedContent)}>k__BackingField")
                        byteCarriers.Add($"field {field.Name}");
                    break;

                case MethodInfo method when !method.IsSpecialName && !method.IsPrivate:
                    // Private methods are excluded from the parameter sweep (compiler-
                    // generated plumbing), but private RETURN carriers still surface via
                    // the field/property arms above — the persistable surface is what the
                    // pin guards.
                    if (IsByteCarrier(method.ReturnType))
                        byteCarriers.Add($"method {method.Name} (return)");
                    foreach (var parameter in method.GetParameters())
                    {
                        // The factory legitimately RECEIVES the already-sealed bytes.
                        if (IsByteCarrier(parameter.ParameterType)
                            && parameter.Name != "sealedContent")
                        {
                            byteCarriers.Add($"method {method.Name} (parameter {parameter.Name})");
                        }
                    }

                    break;
            }
        }

        byteCarriers.ShouldBeEmpty(
            "ResumeFile får aldrig växa en plaintext-bytes-yta — enda tillåtna byte-medlem är "
            + "SealedContent (+ factory-parametern sealedContent). Fynd: "
            + string.Join(", ", byteCarriers));
    }

    private static bool IsByteCarrier(Type type)
    {
        if (type == typeof(byte[])
            || type == typeof(Memory<byte>)
            || type == typeof(ReadOnlyMemory<byte>)
            || type == typeof(IEnumerable<byte>)
            || type == typeof(IReadOnlyList<byte>)
            || type == typeof(IReadOnlyCollection<byte>)
            || typeof(System.IO.Stream).IsAssignableFrom(type))
        {
            return true;
        }

        // Span/ReadOnlySpan<byte> (byref-like) and Result<T>-wrapped carriers.
        if (type.IsGenericType)
        {
            var args = type.GetGenericArguments();
            if (args.Length == 1 && args[0] == typeof(byte))
                return true;
            return args.Any(IsByteCarrier);
        }

        return false;
    }
}
