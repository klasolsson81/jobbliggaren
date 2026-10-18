using Jobbliggaren.Application.Matching.Jobs.BackgroundMatching;
using Jobbliggaren.Application.Matching.Jobs.DigestDispatch;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Worker.Hosting;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// ADR 0161 D8 — <c>user_job_ad_matches</c> has no concurrency token, so a claim is an unconditional
/// update, and a row is mailed once only because exactly one method claims each grade's rows: the
/// Toppmatch dispatcher under <c>BackgroundMatchingWorker</c>'s lock, and the digest for Strong rows.
/// An IL sweep of every production assembly for calls to <see cref="UserJobAdMatch.MarkQueued"/>,
/// with async state machines and lambdas mapped back to the method that declared them.
/// </summary>
public sealed class UserJobAdMatchClaimerTests
{
    internal static readonly string[] ProductionAssemblyPaths =
    [
        typeof(UserJobAdMatch).Assembly.Location,
        typeof(TopMatchMailDispatcher).Assembly.Location,
        typeof(Jobbliggaren.Infrastructure.Persistence.AppDbContext).Assembly.Location,
        typeof(Jobbliggaren.Api.Endpoints.AuthProblem).Assembly.Location,
        typeof(BackgroundMatchingWorker).Assembly.Location,
        typeof(Jobbliggaren.Migrate.ConnectionStringFactory).Assembly.Location,
    ];

    [Fact]
    public void MarkQueued_IsCalledOnlyByTheToppmatchDispatcherAndTheStrongDigest()
    {
        var claimers = ReferencesTo(typeof(UserJobAdMatch), nameof(UserJobAdMatch.MarkQueued));

        claimers.ShouldContain($"{typeof(TopMatchMailDispatcher).FullName}.SendToRecipientAsync",
            "the sweep must see the claimer it guards");
        claimers.ShouldContain($"{typeof(DigestDispatchJob).FullName}.DispatchUserDigestAsync",
            "the sweep must see the claimer it guards");
        claimers.ShouldBe(
            [
                $"{typeof(DigestDispatchJob).FullName}.DispatchUserDigestAsync",
                $"{typeof(TopMatchMailDispatcher).FullName}.SendToRecipientAsync",
            ],
            ignoreOrder: true,
            "a second claimer of Top rows must share BackgroundMatchingWorker's lock or claim with a "
            + "conditional update (ADR 0161 D8). Claimers found: " + string.Join(", ", claimers));
    }

    [Fact]
    public void TheToppmatchDispatcher_IsReachedOnlyThroughTheLockedWorkerRun()
    {
        var dispatchCallers = ReferencesTo(typeof(TopMatchMailDispatcher), nameof(TopMatchMailDispatcher.DispatchAsync));
        var runCallers = ReferencesTo(typeof(BackgroundMatchingJob), nameof(BackgroundMatchingJob.RunAsync));

        dispatchCallers.ShouldBe([$"{typeof(BackgroundMatchingJob).FullName}.RunAsync"],
            "the dispatcher claims Top rows and runs only under BackgroundMatchingWorker's lock (ADR 0161 D8). "
            + "Callers found: " + string.Join(", ", dispatchCallers));
        runCallers.ShouldBe([$"{typeof(BackgroundMatchingWorker).FullName}.RunAsync"],
            "the job runs the dispatcher and runs only under BackgroundMatchingWorker's lock (ADR 0161 D8). "
            + "Callers found: " + string.Join(", ", runCallers));
    }

    internal static List<string> ReferencesTo(Type declaringType, string methodName)
    {
        var declaringTypeName = declaringType.FullName;
        var referrers = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in ProductionAssemblyPaths)
        {
            using var assembly = AssemblyDefinition.ReadAssembly(path);
            foreach (var method in assembly.Modules.SelectMany(module => module.GetTypes()).SelectMany(type => type.Methods))
            {
                if (method.HasBody && method.Body.Instructions.Any(instruction =>
                        (instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt
                         || instruction.OpCode == OpCodes.Ldftn || instruction.OpCode == OpCodes.Ldvirtftn
                         || instruction.OpCode == OpCodes.Ldtoken)
                        && instruction.Operand is MethodReference callee
                        && string.Equals(callee.Name, methodName, StringComparison.Ordinal)
                        && string.Equals(callee.DeclaringType.FullName, declaringTypeName, StringComparison.Ordinal)))
                    referrers.Add(JobAdLifecycleReadRegistryTests.LogicalOwnerKey(method));
            }
        }

        return [.. referrers];
    }
}
