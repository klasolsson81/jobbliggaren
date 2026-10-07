using System.Reflection;
using Jobbliggaren.Application.Auth.Access;
using Jobbliggaren.Application.Auth.Commands.DeleteAccount;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.CompanyWatches.Jobs.CompanyWatchScan;
using Jobbliggaren.Application.Dev.Configuration;
using Jobbliggaren.Application.JobSeekers.Commands.UpdateNotificationConsent;
using Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobSeekers;
using Mediator;
using Microsoft.Extensions.Options;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// ADR 0146 — the job_seekers row carries an xmin token, so every write of it can lose a race. A
/// command that writes it carries <see cref="IReplayOnConcurrencyConflict"/>, or its conflict refuses
/// the write (a consent withdrawal among them) instead of re-running it on a fresh read; and a marked
/// handler takes no port that acts before the commit, since a conflicting attempt may leave nothing
/// behind. An IL sweep, because the
/// writes are calls to the aggregate's own methods, wherever a handler or its state machine makes them.
/// </summary>
public class JobSeekerWriterReplayGuardTests
{
    private const string JobSeekerTypeName = "Jobbliggaren.Domain.JobSeekers.JobSeeker";

    // The nightly scans re-run a user's scan in a fresh scope themselves (ADR 0146 D5).
    private static readonly Type[] ScansWithTheirOwnRetry =
    [
        typeof(BackgroundMatchingJob),
        typeof(CompanyWatchScanJob),
    ];

    // Every constructor port a marked handler takes today. None of them acts before the commit; a new
    // one belongs here only once that is true of it.
    private static readonly Type[] PortsWithNoEffectBeforeCommit =
    [
        typeof(IAppDbContext),
        typeof(ICurrentUser),
        typeof(IDateTimeProvider),
        typeof(IFailedAccessLogger),
        typeof(IOptions<DevToolsOptions>),
    ];

    private static readonly Assembly[] ScannedAssemblies =
    [
        typeof(IAppDbContext).Assembly,
        typeof(Jobbliggaren.Infrastructure.Persistence.AppDbContext).Assembly,
        typeof(Jobbliggaren.Api.Endpoints.AuthProblem).Assembly,
        typeof(Jobbliggaren.Worker.Hosting.BackgroundMatchingWorker).Assembly,
    ];

    [Fact]
    public void Every_writer_of_a_job_seeker_retries_or_is_the_one_protected_deletion_handler_that_refuses_conflicts()
    {
        var writers = TypesCallingAJobSeekerMutator();
        writers.ShouldContain(typeof(UpdateNotificationConsentCommandHandler), "the sweep must see the writers it guards");
        writers.ShouldContain(typeof(BackgroundMatchingJob), "the sweep must see the writers it guards");
        writers.ShouldContain(typeof(DeleteAccountCommandHandler), "the sweep must see the explicit non-replay exception");

        var unmarked = writers
            .Where(type => !ScansWithTheirOwnRetry.Contains(type) && !HandlesAMarkedCommand(type)
                && type != typeof(DeleteAccountCommandHandler))
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToList();

        unmarked.ShouldBeEmpty(
            "These write a job_seekers row without a reviewed replay or protected non-replay protocol: "
            + string.Join(", ", unmarked));
    }

    [Fact]
    public void The_one_non_replay_deletion_exception_requires_the_protected_reauthenticated_audited_scope()
    {
        var command = typeof(DeleteAccountCommand);
        command.IsAssignableTo(typeof(IAccountAccessMutation)).ShouldBeTrue();
        command.IsAssignableTo(typeof(IReauthenticatingRequest)).ShouldBeTrue();
        command.IsAssignableTo(typeof(IAuditableCommand)).ShouldBeTrue();
        command.IsAssignableTo(typeof(IAuthenticatedRequest)).ShouldBeTrue();
        command.IsAssignableTo(typeof(IReplayOnConcurrencyConflict)).ShouldBeFalse();
        HandlesAMarkedCommand(typeof(DeleteAccountCommandHandler)).ShouldBeFalse();
    }

    [Fact]
    public void A_marked_command_handler_takes_no_port_that_acts_before_the_commit()
    {
        var markedHandlers = MarkedHandlers();

        var offending = markedHandlers
            .SelectMany(handler => handler.GetConstructors()
                .SelectMany(ctor => ctor.GetParameters())
                .Where(parameter => !PortsWithNoEffectBeforeCommit.Contains(parameter.ParameterType))
                .Select(parameter => $"{handler.Name}({parameter.ParameterType.Name})"))
            .Order(StringComparer.Ordinal)
            .ToList();

        offending.ShouldBeEmpty(
            "A replayed handler runs again after a conflict, so a port that acts before the commit (a "
            + "mail, a job, a Redis write) would act once per attempt: " + string.Join(", ", offending));
    }

    // The port the handler keeps is the unit of work's own, and it can still commit on its own: a save,
    // or ExecuteUpdate/ExecuteDelete, which run at once. Either leaves a write behind a conflicting
    // attempt, so a 409 would no longer mean nothing was written.
    [Fact]
    public void A_marked_command_handler_never_commits_ahead_of_the_unit_of_work()
    {
        using var module = ModuleDefinition.ReadModule(typeof(IAppDbContext).Assembly.Location);

        var committing = MarkedHandlers()
            .Where(handler => MethodsIncludingNestedTypes(module.GetType(handler.FullName!)).Any(CommitsOnItsOwn))
            .Select(handler => handler.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        committing.ShouldBeEmpty(
            "These marked handlers commit before the unit of work does: " + string.Join(", ", committing));
    }

    private static List<Type> MarkedHandlers()
    {
        var markedHandlers = typeof(IAppDbContext).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && HandlesAMarkedCommand(type))
            .ToList();
        markedHandlers.ShouldContain(typeof(UpdateNotificationConsentCommandHandler));
        return markedHandlers;
    }

    private static bool CommitsOnItsOwn(MethodDefinition method) =>
        method.HasBody
        && method.Body.Instructions.Any(instruction =>
            (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
            && instruction.Operand is MethodReference target
            && (target.Name is "SaveChanges" or "SaveChangesAsync"
                || target.Name.StartsWith("ExecuteUpdate", StringComparison.Ordinal)
                || target.Name.StartsWith("ExecuteDelete", StringComparison.Ordinal)));

    private static bool HandlesAMarkedCommand(Type type) =>
        type.GetInterfaces().Any(contract =>
            contract.IsGenericType
            && contract.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
            && typeof(IReplayOnConcurrencyConflict).IsAssignableFrom(contract.GetGenericArguments()[0]));

    // The outermost type of every method, state machines and lambdas included, whose IL calls one of
    // the aggregate's public instance methods.
    private static HashSet<Type> TypesCallingAJobSeekerMutator()
    {
        var mutators = typeof(JobSeeker)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .ToHashSet(StringComparer.Ordinal);
        mutators.ShouldContain(nameof(JobSeeker.UpdateNotificationConsent));

        var writers = new HashSet<Type>();
        foreach (var assembly in ScannedAssemblies)
        {
            using var module = ModuleDefinition.ReadModule(assembly.Location);
            foreach (var type in module.Types)
            {
                if (MethodsIncludingNestedTypes(type).Any(method => CallsAMutator(method, mutators)))
                    writers.Add(assembly.GetType(type.FullName, throwOnError: true)!);
            }
        }

        return writers;
    }

    private static bool CallsAMutator(MethodDefinition method, HashSet<string> mutators) =>
        method.HasBody
        && method.Body.Instructions.Any(instruction =>
            (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)
            && instruction.Operand is MethodReference target
            && string.Equals(target.DeclaringType?.FullName, JobSeekerTypeName, StringComparison.Ordinal)
            && mutators.Contains(target.Name));

    private static IEnumerable<MethodDefinition> MethodsIncludingNestedTypes(TypeDefinition type) =>
        type.Methods.Concat(type.NestedTypes.SelectMany(MethodsIncludingNestedTypes));
}
