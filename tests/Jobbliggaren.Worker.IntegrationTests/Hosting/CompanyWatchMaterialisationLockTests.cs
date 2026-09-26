using System.Reflection;
using Hangfire;
using Hangfire.Common;
using Jobbliggaren.Infrastructure.CompanyRegister;
using Jobbliggaren.Worker.Hosting;
using Shouldly;

namespace Jobbliggaren.Worker.IntegrationTests.Hosting;

/// <summary>
/// #1681 clause (ii) — the pin on the ONE property that makes a second materialisation writer safe:
/// both Worker methods must take the SAME Hangfire distributed lock.
///
/// <para>
/// <b>This is a correctness pin, not a configuration pin.</b> Without a shared lock, two overlapping
/// resolutions of one criterion can leave the member table holding the UNION of two predicates'
/// company sets while the state row carries the newer fingerprint — at which point the read gate
/// passes and the surface renders an exact, confident, wrong number. The full derivation lives on
/// <see cref="CompanyWatchCriterionMaterialisationWorker"/>; what this file adds is that the property
/// is measured rather than asserted in prose.
/// </para>
///
/// <para>
/// <b>The failure it guards is silent by construction.</b> <c>DisableConcurrentExecution</c>'s
/// <c>(int)</c> constructor keys the lock per METHOD, so dropping the resource argument from either
/// attribute leaves both jobs running, both suites green, and the hazard simply back. Nothing else in
/// the build would notice.
/// </para>
///
/// <para>
/// Resource resolution goes through Hangfire's own <c>GetResource(Job)</c> rather than reading the
/// attribute's constructor argument, because the argument is not the whole answer: the per-method
/// default is computed inside that method, so only invoking it can tell a shared key from two
/// coincidentally-equal literals. It is non-public, hence reflection — and the reflection itself is
/// proven live by the negative control below, which would fail if the lookup silently returned null.
/// </para>
/// </summary>
public class CompanyWatchMaterialisationLockTests
{
    [Fact]
    public void BothMaterialisationJobs_ResolveToTheSameDistributedLockResource()
    {
        var nightly = ResourceFor(nameof(CompanyWatchCriterionMaterialisationWorker.RunAsync));
        var sweep = ResourceFor(nameof(CompanyWatchCriterionMaterialisationWorker.SweepAsync));

        nightly.ShouldNotBeNullOrWhiteSpace();
        sweep.ShouldBe(nightly);
    }

    /// <summary>
    /// The negative control, and it is what makes the assertion above non-vacuous. An attribute
    /// carrying only a timeout resolves to a per-method key — so if the reflection lookup were
    /// broken, or if <c>GetResource</c> ignored its argument, THIS test would fail and the shared-key
    /// test would be passing on two nulls (a "guard is green" that measures nothing, the vacuous
    /// class this repo names at #805-3 / #842).
    /// </summary>
    [Fact]
    public void AnAttributeWithoutAnExplicitResource_KeysTheLockPerMethod()
    {
        var perMethod = new DisableConcurrentExecutionAttribute(60);

        var nightly = InvokeGetResource(
            perMethod, nameof(CompanyWatchCriterionMaterialisationWorker.RunAsync));
        var sweep = InvokeGetResource(
            perMethod, nameof(CompanyWatchCriterionMaterialisationWorker.SweepAsync));

        nightly.ShouldNotBeNullOrWhiteSpace();
        sweep.ShouldNotBe(nightly);
    }

    /// <summary>
    /// The sweep suppresses Hangfire's retry and the nightly run keeps it — opposite postures with
    /// one shared reason (the next minutely tick IS the sweep's retry, while the nightly run's next
    /// attempt is up to 24 h away). Pinned because the asymmetry looks like an oversight to anyone
    /// tidying the two attributes toward each other.
    /// </summary>
    [Fact]
    public void OnlyTheSweep_SuppressesHangfireRetries()
    {
        MethodFor(nameof(CompanyWatchCriterionMaterialisationWorker.SweepAsync))
            .GetCustomAttribute<AutomaticRetryAttribute>()!.Attempts.ShouldBe(0);

        MethodFor(nameof(CompanyWatchCriterionMaterialisationWorker.RunAsync))
            .GetCustomAttribute<AutomaticRetryAttribute>().ShouldBeNull();
    }

    /// <summary>
    /// The two acquisition waits, and the relation the type system cannot carry: an attribute
    /// argument must be a compile-time constant while <c>SweepCron</c> is an <c>IOptions</c> value,
    /// so nothing but a test can make a mismatch fail.
    ///
    /// <para>
    /// <b>The sweep's wait must stay BELOW its own arrival interval.</b> Hangfire's filter acquires
    /// the lock on the worker thread, so a blocked tick occupies a worker for the whole wait while
    /// the scheduler keeps enqueueing one a minute; offered concurrency is wait ÷ interval. At the
    /// 15 minutes inherited from the nightly job that is 15 against <c>WorkerCount = 4</c>, so a few
    /// minutes of held lock exhausts the pool and the Worker stops running everything — account
    /// hard-delete and the retention jobs included. <c>SyncPlatsbankenStreamWorker</c> already
    /// carries this rule in the opposite direction (540 s under a 600 s cron).
    /// </para>
    /// </summary>
    [Fact]
    public void TheSweepWaitsLessThanOneTick_WhileTheNightlyRunWaitsFifteenMinutes()
    {
        var defaults = new CompanyWatchMaterialisationOptions();
        defaults.SweepCron.ShouldBe("* * * * *",
            "härledningen nedan förutsätter en minutlig kadens — ändras cron:en måste väntetiden "
            + "räknas om mot den nya perioden");
        const int tickSeconds = 60;

        // BÅDA gränserna, inte bara den övre (test-writer, omkontroll 2026-09-07). Docblocket
        // formulerar ett tvåsidigt villkor — väntan ska ligga ÖVER nattkörningens mätta hålltid och
        // UNDER tickintervallet — men `ShouldBeLessThan(60)` ensam är sann också för 1, och 1 s
        // betyder att en tick som möter nattkörningen MISSLYCKAS i stället för att vänta in den.
        // Det tillverkar precis det Failed-jobb som kastet nyss togs bort för att slippa.
        LockWaitSecondsFor(nameof(CompanyWatchCriterionMaterialisationWorker.SweepAsync))
            .ShouldBeInRange(10, tickSeconds - 1);

        LockWaitSecondsFor(nameof(CompanyWatchCriterionMaterialisationWorker.RunAsync))
            .ShouldBe(15 * 60,
                "nattjobbet ankommer en gång per dygn, så ingenting köar bakom dess väntan");
    }

    /// <summary>
    /// The timeout as WRITTEN, read off the attribute application rather than off a Hangfire
    /// property — the constructor argument is the thing under review, and reading it this way cannot
    /// drift with the library's own field naming.
    /// </summary>
    private static int LockWaitSecondsFor(string methodName)
    {
        var data = MethodFor(methodName)
            .GetCustomAttributesData()
            .Single(a => a.AttributeType == typeof(DisableConcurrentExecutionAttribute));

        data.ConstructorArguments.Count.ShouldBe(2,
            $"{methodName} måste använda (resurs, timeout)-konstruktorn — enargumentsformen nycklar "
            + "låset per METOD och delar det inte");

        return (int)data.ConstructorArguments[1].Value!;
    }

    private static string? ResourceFor(string methodName)
    {
        var attribute = MethodFor(methodName)
            .GetCustomAttribute<DisableConcurrentExecutionAttribute>();
        attribute.ShouldNotBeNull($"{methodName} carries no DisableConcurrentExecution attribute.");

        return InvokeGetResource(attribute, methodName);
    }

    private static string? InvokeGetResource(
        DisableConcurrentExecutionAttribute attribute, string methodName)
    {
        var job = Job.FromExpression<CompanyWatchCriterionMaterialisationWorker>(
            methodName == nameof(CompanyWatchCriterionMaterialisationWorker.RunAsync)
                ? w => w.RunAsync(CancellationToken.None)
                : w => w.SweepAsync(CancellationToken.None));

        var getResource = typeof(DisableConcurrentExecutionAttribute).GetMethod(
            "GetResource", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        getResource.ShouldNotBeNull(
            "Hangfire's DisableConcurrentExecutionAttribute.GetResource(Job) is gone — the lock's "
            + "keying has moved and this pin must be re-derived against the new API.");

        return (string?)getResource.Invoke(attribute, [job]);
    }

    private static MethodInfo MethodFor(string name) =>
        typeof(CompanyWatchCriterionMaterialisationWorker).GetMethod(name)!;
}
