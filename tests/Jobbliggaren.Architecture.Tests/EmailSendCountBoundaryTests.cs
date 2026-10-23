using System.Reflection;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Email;
using Jobbliggaren.Infrastructure.Email.Counting;
using Jobbliggaren.Infrastructure.Persistence.Configurations;
using Jobbliggaren.Worker.Auditing;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// #1981 PR 3 (ADR 0180) — where our own send counts may go. The tally writes the day's count, the configurations map
/// the two tables, and nothing else in the production assemblies names them; the recording start has no writer outside
/// the migration that created it (security-auditor E). Only the Scaleway funnel counts. The sites are the IL's own,
/// mapped from compiler-generated code back to the method written; EF migrations and model snapshots are admitted by
/// kind. The test cannot see SQL assembled without a table's name, or a read made outside the code (CLAUDE.md §9.2 (b)).
/// </summary>
public class EmailSendCountBoundaryTests
{
    private const string Bound =
        "ADR 0180: our own send counts have one writer, the tally, and a new reader or writer is a new use of data "
        + "security-auditor ruled linkable (purpose 3); it needs its own review.";

    [Fact]
    public void Only_the_configuration_and_the_tally_name_the_daily_counts()
        => SitesNaming("email_send_daily_counts").ShouldBe(
            [
                $"{typeof(EmailSendDailyCountConfiguration).FullName}.{nameof(EmailSendDailyCountConfiguration.Configure)}",
                $"{typeof(SqlEmailSendTally).FullName}.WriteAsync",
            ],
            ignoreOrder: true,
            Bound);

    [Fact]
    public void Only_the_configuration_names_the_recording_start_so_no_code_writes_it()
        => SitesNaming("email_send_counting").ShouldBe(
            [$"{typeof(EmailSendCountingConfiguration).FullName}.{nameof(EmailSendCountingConfiguration.Configure)}"],
            ignoreOrder: true,
            Bound);

    [Theory]
    [InlineData(typeof(EmailSendDailyCount), typeof(EmailSendDailyCountConfiguration))]
    [InlineData(typeof(EmailSendCounting), typeof(EmailSendCountingConfiguration))]
    public void Only_the_configuration_references_the_entity_type(Type entity, Type configuration)
        => ProductionTypeSweep.OwnersReferencing(entity.FullName!)
            .ShouldBe([configuration.FullName!], Bound);

    [Fact]
    public void The_tally_is_injected_into_the_Scaleway_sender_alone()
    {
        const BindingFlags declared =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        Assembly[] owned =
        [
            typeof(Jobbliggaren.Application.AssemblyMarker).Assembly,
            typeof(Jobbliggaren.Infrastructure.AssemblyMarker).Assembly,
            typeof(Jobbliggaren.Api.Endpoints.AdminEmailEndpoints).Assembly,
            typeof(WorkerSystemUser).Assembly,
            typeof(Jobbliggaren.Migrate.ConnectionStringFactory).Assembly,
        ];

        var consumers = owned
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.GetConstructors(declared).Cast<MethodBase>().Concat(type.GetMethods(declared))
                .Any(member => member.GetParameters()
                    .Any(parameter => typeof(EmailSendTally).IsAssignableFrom(parameter.ParameterType))))
            .Select(type => type.FullName!)
            .ToList();

        consumers.ShouldBe(
            [typeof(ScalewayEmailSender).FullName!],
            "only the arm that hands a mail to the provider counts: " + string.Join(", ", consumers));
    }

    /// <summary>
    /// The count carries the kind and the outcome and nothing else: no recipient (5e), no time — the day is the server's
    /// own (5b) — and no caller token, so neither a caller's cancellation nor a caller's value can reach the write.
    /// </summary>
    [Fact]
    public void RecordAsync_takes_the_kind_and_the_outcome_and_nothing_else()
    {
        var record = typeof(EmailSendTally).GetMethod(nameof(EmailSendTally.RecordAsync)).ShouldNotBeNull();

        record.GetParameters().Select(parameter => parameter.ParameterType)
            .ShouldBe([typeof(EmailKind), typeof(EmailSendOutcome)]);
        record.ReturnType.ShouldBe(typeof(ValueTask));
        record.IsVirtual.ShouldBeFalse("the containment is the base's own and no subclass may replace it");
        typeof(EmailSendTally).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .ShouldBe([nameof(EmailSendTally.RecordAsync)], "RecordAsync is the tally's only public entry");
    }

    private static SortedSet<string> SitesNaming(string table)
    {
        var sites = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in ProductionTypeSweep.Types().Where(type => !IsMigrationOrSnapshot(type)))
        {
            foreach (var method in type.Methods.Where(method => method.HasBody))
            {
                if (method.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Ldstr
                        && instruction.Operand is string literal
                        && literal.Contains(table, StringComparison.OrdinalIgnoreCase)))
                    sites.Add(JobAdLifecycleReadRegistryTests.LogicalOwnerKey(method));
            }
        }

        return sites;
    }

    private static bool IsMigrationOrSnapshot(TypeDefinition type)
    {
        var owner = ProductionTypeSweep.OwnerOf(type);
        return owner.BaseType?.FullName is { } baseType
            && (baseType == typeof(Migration).FullName || baseType == typeof(ModelSnapshot).FullName);
    }
}
