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
/// <item><b>Marker-pin:</b> every handler that injects <see cref="IBinaryFieldSealer"/> must
/// handle a message carrying <see cref="IRequiresFieldEncryptionKey"/> — otherwise the
/// prefetch behavior never warms the owner DEK and the sealer's fail-closed throw fires at
/// runtime instead of the DEK being warm by construction.</item>
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
    {
        var applicationAssembly = typeof(Jobbliggaren.Application.AssemblyMarker).Assembly;

        var offenders = new List<string>();
        var pinnedHandlers = 0;

        foreach (var type in applicationAssembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface)
                continue;

            var injectsSealer = type.GetConstructors()
                .Any(c => c.GetParameters()
                    .Any(p => p.ParameterType == typeof(IBinaryFieldSealer)));

            if (!injectsSealer)
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

            // A sealer-injecting type that is not a Mediator handler at all (e.g. a job)
            // has no prefetch pipeline — flag it too: the sealer would ALWAYS fail closed.
            if (handlerInterfaces.Count == 0)
                offenders.Add($"{type.Name} (injects IBinaryFieldSealer outside the Mediator pipeline)");
        }

        offenders.ShouldBeEmpty(
            "IBinaryFieldSealer peeks the DEK FieldEncryptionKeyPrefetchBehavior warmed — a "
            + "handler whose message lacks IRequiresFieldEncryptionKey seals fail-closed at "
            + "runtime. Offenders: " + string.Join(", ", offenders));

        // Vacuity guard: the import handler is pinned today; if this ever reads 0 the pin
        // stopped seeing the handlers (reflection drift), which must fail loud, not pass.
        pinnedHandlers.ShouldBeGreaterThan(0,
            "the marker-pin found no sealer-injecting handlers — reflection drift?");
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
        foreach (var member in aggregate.GetMembers(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly))
        {
            switch (member)
            {
                case PropertyInfo property when IsByteCarrier(property.PropertyType):
                    if (property.Name != nameof(ResumeFile.SealedContent))
                        byteCarriers.Add($"property {property.Name}");
                    break;

                case FieldInfo field when IsByteCarrier(field.FieldType):
                    byteCarriers.Add($"field {field.Name}");
                    break;

                case MethodInfo method when !method.IsSpecialName:
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
