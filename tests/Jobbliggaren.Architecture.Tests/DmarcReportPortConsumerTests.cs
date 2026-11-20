using System.Reflection;
using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Application.Admin.Email.Dmarc.Queries.GetDmarcReports;
using Jobbliggaren.Application.Admin.Email.Jobs.DmarcReportIngestion;
using Jobbliggaren.Application.Admin.Email.Jobs.DmarcReportRetention;
using Jobbliggaren.Infrastructure.Email.Dmarc;
using Jobbliggaren.Infrastructure.Mailbox.Dmarc;
using Jobbliggaren.Worker.Hosting;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1981 PR 4a (ADR 0185) — who may hold each DMARC port, and where each half is composed. The mailbox, the decoder and
/// the ingestion store reach the ingestion job alone, the retention reaches the retention job alone, and the read
/// reaches the reports query alone. The Worker composes its half through one registration, which only its Program
/// calls; the Api's half is composed by <c>AddInfrastructure</c>, which only the Api's Program calls, and no type written
/// in the Worker names the read. Call sites and references are the IL's own, mapped from compiler-generated code back to
/// the type or method written, over the production assemblies only: the Worker test fixture's call is not one.
/// </summary>
public class DmarcReportPortConsumerTests
{
    private const string Bound =
        "ADR 0185: a new holder of a DMARC port is a new reader or writer of report data, whose source addresses are "
        + "personal data; it needs its own review.";

    /// <summary>The tool name the Mediator source generator stamps on the types it writes, in every host that runs it.</summary>
    private const string MediatorGenerator = "Mediator.SourceGenerator";

    private static readonly string IngestionRegistration =
        $"Jobbliggaren.Worker: {typeof(DmarcReportIngestionRegistration).FullName}.{nameof(DmarcReportIngestionRegistration.AddDmarcReportIngestion)}";

    private static readonly string QueryNamespace = typeof(GetDmarcReportsQuery).Namespace!;

    public static TheoryData<string, string[]> PortConsumers() => new()
    {
        { typeof(IDmarcMailbox).FullName!, [typeof(DmarcReportIngestionJob).FullName!] },
        { typeof(IDmarcReportDecoder).FullName!, [typeof(DmarcReportIngestionJob).FullName!] },
        { typeof(IDmarcReportStore).FullName!, [typeof(DmarcReportIngestionJob).FullName!] },
        { typeof(IDmarcReportRetention).FullName!, [typeof(DmarcReportRetentionJob).FullName!] },
        { typeof(IDmarcReportsSource).FullName!, [typeof(GetDmarcReportsQueryHandler).FullName!] },
    };

    [Theory]
    [MemberData(nameof(PortConsumers))]
    public void EachDmarcPort_IsInjectedOnlyIntoItsNamedConsumer(string port, string[] consumers)
    {
        const BindingFlags declared =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var found = ProductionAssemblies.ReflectedTypes()
            .Where(type => type.GetConstructors(declared).Cast<MethodBase>().Concat(type.GetMethods(declared))
                .Any(member => member.GetParameters().Any(parameter => parameter.ParameterType.FullName == port)))
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal)
            .ToList();

        found.ShouldContain(consumers[0], $"the sweep must see the consumer it guards. Found: {string.Join(", ", found)}");
        found.ShouldBe([.. consumers.Order(StringComparer.Ordinal)], $"{Bound} Found for {port}: {string.Join(", ", found)}");
    }

    [Fact]
    public void AddDmarcReportIngestion_IsCalledOnce_FromTheWorkersProgram()
    {
        var sites = CallSitesOf(typeof(DmarcReportIngestionRegistration), nameof(DmarcReportIngestionRegistration.AddDmarcReportIngestion));

        sites.Select(site => site.InType).ShouldBe(["Jobbliggaren.Worker: Program"],
            "the Worker composes the mailbox, the decoder and the stores through this one registration, called once from "
            + "its Program: " + string.Join(", ", sites));
    }

    [Fact]
    public void TheWorkersStoresAndMailbox_AreRegisteredOnlyByTheIngestionRegistration()
    {
        var stores = CallSitesOf(
            typeof(DmarcReportPersistenceCollectionExtensions),
            nameof(DmarcReportPersistenceCollectionExtensions.AddDmarcReportIngestionStores));
        var mailbox = CallSitesOf(
            typeof(DmarcMailboxCollectionExtensions), nameof(DmarcMailboxCollectionExtensions.AddDmarcMailbox));

        stores.Select(site => site.InMethod).ShouldBe([IngestionRegistration],
            "AddInfrastructure, which the Api calls, must never compose the ingestion store: " + string.Join(", ", stores));
        mailbox.Select(site => site.InMethod).ShouldBe([IngestionRegistration],
            "only the Worker builds the mailbox: " + string.Join(", ", mailbox));
    }

    [Fact]
    public void TheRead_IsRegisteredOnlyByAddInfrastructure_WhichOnlyTheApiCalls()
    {
        var read = CallSitesOf(
            typeof(DmarcReportPersistenceCollectionExtensions),
            nameof(DmarcReportPersistenceCollectionExtensions.AddDmarcReportsSource));
        var infrastructure = CallSitesOf(
            typeof(Jobbliggaren.Infrastructure.DependencyInjection),
            nameof(Jobbliggaren.Infrastructure.DependencyInjection.AddInfrastructure));

        read.Select(site => site.InMethod).ShouldBe(
            [$"Jobbliggaren.Infrastructure: {typeof(Jobbliggaren.Infrastructure.DependencyInjection).FullName}.AddInfrastructure"],
            "the read is composed in one place: " + string.Join(", ", read));
        infrastructure.Select(site => site.InType).ShouldBe(["Jobbliggaren.Api: Program"],
            "the Worker never calls AddInfrastructure, so it never composes the read: " + string.Join(", ", infrastructure));
    }

    [Fact]
    public void NoTypeWrittenInTheWorker_NamesTheRead()
    {
        var written = ReadSideMentions(ProductionAssemblies.Worker)
            .Where(mention => !mention.ByMediatorGenerator)
            .ToList();

        written.ShouldBeEmpty("the DMARC read is the Api's, and the Worker names it: " + string.Join(", ", written));
    }

    /// <summary>
    /// The one exception to the rule above, measured rather than assumed: the Worker runs the Mediator source generator,
    /// whose dispatch table and registration name every query handler in Application, this one included. They name the
    /// query and its handler and never the read itself, which the Worker does not register, so the handler is never
    /// built there.
    /// </summary>
    [Fact]
    public void TheWorkersGeneratedMediator_NamesOnlyTheQueryAndItsHandler()
    {
        var generated = ReadSideMentions(ProductionAssemblies.Worker)
            .Where(mention => mention.ByMediatorGenerator)
            .Select(mention => mention.Named)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        generated.ShouldNotBeEmpty("the scan must read the Worker's generated code, or the exclusion above is not measured");
        generated.ShouldBeSubsetOf(
            [typeof(GetDmarcReportsQuery).FullName!, typeof(GetDmarcReportsQueryHandler).FullName!],
            "the generated dispatch names the read itself: " + string.Join(", ", generated));
    }

    /// <summary>The scan the Worker rules use finds each read-side target where it is held, through a lambda too.</summary>
    [Theory]
    [InlineData("Application", "Jobbliggaren.Application.Admin.Email.Dmarc.IDmarcReportsSource",
        "Jobbliggaren.Application.Admin.Email.Dmarc.Queries.GetDmarcReports.GetDmarcReportsQueryHandler")]
    [InlineData("Infrastructure", "Jobbliggaren.Infrastructure.Email.Dmarc.SqlDmarcReportsSource",
        "Jobbliggaren.Infrastructure.Email.Dmarc.DmarcReportPersistenceCollectionExtensions")]
    [InlineData("Api", "Jobbliggaren.Application.Admin.Email.Dmarc.Queries.GetDmarcReports.GetDmarcReportsQuery",
        "Jobbliggaren.Api.Endpoints.AdminEmailEndpoints")]
    public void TheReadSideScan_FindsEachTargetWhereItIsHeld(string assembly, string named, string owner)
    {
        var target = assembly switch
        {
            "Application" => ProductionAssemblies.Application,
            "Infrastructure" => ProductionAssemblies.Infrastructure,
            "Api" => ProductionAssemblies.Api,
            _ => throw new ArgumentOutOfRangeException(nameof(assembly), assembly, "Not a scanned assembly."),
        };

        ReadSideMentions(target).ShouldContain(new Mention(owner, named, ByMediatorGenerator: false),
            "the scan must see the read where it is held, or the Worker rule pins nothing");
    }

    private sealed record CallSite(string InType, string InMethod)
    {
        public override string ToString() => InMethod;
    }

    private sealed record Mention(string Owner, string Named, bool ByMediatorGenerator);

    /// <summary>
    /// One entry per instruction that calls, or takes the address of, <paramref name="method"/> declared on
    /// <paramref name="declaringType"/>: the assembly with the type written (a top-level Program's async state machine
    /// maps to <c>Program</c>), and with the method written. A second call from the same method is a second entry.
    /// </summary>
    private static List<CallSite> CallSitesOf(Type declaringType, string method)
    {
        var sites = new List<CallSite>();
        foreach (var production in ProductionAssemblies.All)
        {
            using var assembly = production.ReadDefinition();
            foreach (var caller in assembly.Modules.SelectMany(module => module.GetTypes()).SelectMany(type => type.Methods))
            {
                if (!caller.HasBody)
                    continue;
                foreach (var instruction in caller.Body.Instructions)
                {
                    if ((instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt
                            || instruction.OpCode == OpCodes.Ldftn || instruction.OpCode == OpCodes.Ldvirtftn
                            || instruction.OpCode == OpCodes.Ldtoken)
                        && instruction.Operand is MethodReference callee
                        && string.Equals(callee.Name, method, StringComparison.Ordinal)
                        && string.Equals(callee.DeclaringType.FullName, declaringType.FullName, StringComparison.Ordinal))
                    {
                        sites.Add(new CallSite(
                            $"{assembly.Name.Name}: {ProductionTypeSweep.OwnerOf(caller.DeclaringType).FullName}",
                            $"{assembly.Name.Name}: {JobAdLifecycleReadRegistryTests.LogicalOwnerKey(caller)}"));
                    }
                }
            }
        }

        return [.. sites.OrderBy(site => site.InMethod, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Every type written in <paramref name="assembly"/> (a compiler-generated closure or state machine counts as the type
    /// it was written in) with each read-side type its signatures or IL name: the read port, its adapter, or anything in
    /// the reports query's namespace.
    /// </summary>
    private static List<Mention> ReadSideMentions(ProductionAssembly assembly)
    {
        var mentions = new HashSet<Mention>();
        using var definition = assembly.ReadDefinition();
        foreach (var type in definition.Modules.SelectMany(module => module.GetTypes()))
        {
            var owner = ProductionTypeSweep.OwnerOf(type);
            var byMediator = WrittenByMediatorGenerator(owner);
            foreach (var named in NamedBy(type).SelectMany(Flatten).Select(reference => reference.FullName))
            {
                if (named == typeof(IDmarcReportsSource).FullName
                    || named == typeof(SqlDmarcReportsSource).FullName
                    || named.StartsWith(QueryNamespace + ".", StringComparison.Ordinal))
                    mentions.Add(new Mention(owner.FullName, named, byMediator));
            }
        }

        return [.. mentions.OrderBy(mention => mention.Owner, StringComparer.Ordinal).ThenBy(mention => mention.Named, StringComparer.Ordinal)];
    }

    /// <summary>The type, or one it is nested in, carries <c>[GeneratedCode("Mediator.SourceGenerator", ...)]</c>.</summary>
    private static bool WrittenByMediatorGenerator(TypeDefinition type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
        {
            if (current.CustomAttributes.Any(attribute =>
                    attribute.AttributeType.FullName == typeof(System.CodeDom.Compiler.GeneratedCodeAttribute).FullName
                    && attribute.ConstructorArguments is [{ Value: MediatorGenerator }, ..]))
                return true;
        }

        return false;
    }

    private static IEnumerable<TypeReference?> NamedBy(TypeDefinition type)
    {
        yield return type.BaseType;
        foreach (var implemented in type.Interfaces)
            yield return implemented.InterfaceType;
        foreach (var field in type.Fields)
            yield return field.FieldType;
        foreach (var method in type.Methods)
        {
            yield return method.ReturnType;
            foreach (var parameter in method.Parameters)
                yield return parameter.ParameterType;
            if (!method.HasBody)
                continue;
            foreach (var variable in method.Body.Variables)
                yield return variable.VariableType;
            foreach (var instruction in method.Body.Instructions)
            {
                switch (instruction.Operand)
                {
                    case TypeReference operand:
                        yield return operand;
                        break;
                    case MethodReference operand:
                        yield return operand.DeclaringType;
                        yield return operand.ReturnType;
                        foreach (var parameter in operand.Parameters)
                            yield return parameter.ParameterType;
                        if (operand is GenericInstanceMethod generic)
                        {
                            foreach (var argument in generic.GenericArguments)
                                yield return argument;
                        }

                        break;
                    case FieldReference operand:
                        yield return operand.DeclaringType;
                        yield return operand.FieldType;
                        break;
                }
            }
        }
    }

    /// <summary>A reference and every type inside it: generic arguments, and the element of an array, pointer or by-ref.</summary>
    private static IEnumerable<TypeReference> Flatten(TypeReference? reference)
    {
        switch (reference)
        {
            case null:
                yield break;
            case GenericInstanceType generic:
                foreach (var inner in Flatten(generic.ElementType).Concat(generic.GenericArguments.SelectMany(Flatten)))
                    yield return inner;
                break;
            case TypeSpecification specification:
                foreach (var inner in Flatten(specification.ElementType))
                    yield return inner;
                break;
            default:
                yield return reference;
                break;
        }
    }
}
