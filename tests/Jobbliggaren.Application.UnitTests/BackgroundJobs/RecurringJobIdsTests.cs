using Jobbliggaren.Application.BackgroundJobs;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.BackgroundJobs;

/// <summary>
/// #204 / TD-83 PR2 — locks the closed trigger allowlist <see cref="RecurringJobIds.All"/>.
/// This set is the security boundary for the admin "trigger now" surface (fan-out/RCE
/// prevention, security-auditor T7): a change to its membership is a security-relevant
/// change and must be deliberate. The exact-15 count + distinctness + Ordinal comparer are
/// asserted here; the registrar-parity (registered ids == this set, no drift) is asserted in
/// <c>RecurringJobRegistrarParityTests</c> (Worker side).
/// </summary>
public class RecurringJobIdsTests
{
    // The 15 constants, listed independently of RecurringJobIds.All so the test is a real
    // second source of truth (a drift between the constants and All would surface here).
    private static readonly string[] ExpectedIds =
    [
        RecurringJobIds.SyncPlatsbankenStream,
        RecurringJobIds.SyncPlatsbankenSnapshot,
        RecurringJobIds.AuditLogRetention,
        RecurringJobIds.RetainPlatsbankenJobAds,
        RecurringJobIds.BackgroundMatching,
        RecurringJobIds.DetectGhosted,
        RecurringJobIds.ExpireJobAds,
        RecurringJobIds.HardDeleteAccounts,
        RecurringJobIds.PurgeStaleRawPayloads,
        RecurringJobIds.ReapStrandedMatches,
        RecurringJobIds.BackfillFieldEncryption,
        RecurringJobIds.ParsedResumeRetention,
        RecurringJobIds.DigestDispatchDaily,
        RecurringJobIds.DigestDispatchWeekly,
        RecurringJobIds.RefreshLandingStats,
    ];

    [Fact]
    public void All_HasExactlyFifteenMembers()
    {
        RecurringJobIds.All.Count.ShouldBe(15);
    }

    [Fact]
    public void All_MembersAreDistinct()
    {
        // FrozenSet dedups silently; assert against the raw constant list so a duplicated
        // const value (copy-paste slip) is caught rather than absorbed by the set.
        ExpectedIds.Length.ShouldBe(15);
        ExpectedIds.Distinct(StringComparer.Ordinal).Count().ShouldBe(15);
    }

    [Fact]
    public void All_ContainsEveryDeclaredConstant()
    {
        foreach (var id in ExpectedIds)
            RecurringJobIds.All.ShouldContain(id);
    }

    [Fact]
    public void All_UsesOrdinalComparer()
    {
        RecurringJobIds.All.Comparer.ShouldBe(StringComparer.Ordinal);
    }

    [Fact]
    public void All_DoesNotMatchCaseInsensitively_ProvesOrdinal()
    {
        // Ordinal (not OrdinalIgnoreCase): the upper-cased slug is NOT a member. Guards the
        // comparer choice behaviourally, not just by reference equality.
        RecurringJobIds.All.Contains(RecurringJobIds.SyncPlatsbankenStream.ToUpperInvariant())
            .ShouldBeFalse();
    }

    [Fact]
    public void All_RejectsUnknownId()
    {
        RecurringJobIds.All.Contains("definitely-not-a-registered-job").ShouldBeFalse();
    }
}
