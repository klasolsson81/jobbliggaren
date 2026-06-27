using Jobbliggaren.Application.Admin.BackgroundJobs.Commands.TriggerRecurringJob;
using Jobbliggaren.Application.BackgroundJobs;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.BackgroundJobs.Commands.TriggerRecurringJob;

/// <summary>
/// #204 / TD-83 PR2 — the trigger allowlist gate (security-auditor T7, fan-out/RCE prevention).
/// The validator passes ONLY a member of <see cref="RecurringJobIds.All"/>; every non-member
/// (and empty) is a 400 shape error before the handler ever touches the port.
/// </summary>
public class TriggerRecurringJobCommandValidatorTests
{
    private readonly TriggerRecurringJobCommandValidator _validator = new();

    [Fact]
    public void Validate_AllowlistMember_Passes()
    {
        var result = _validator.Validate(new TriggerRecurringJobCommand(RecurringJobIds.HardDeleteAccounts));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_EveryAllowlistMember_Passes()
    {
        // The validator must accept the full closed set — a registered id rejected here would be
        // permanently untriggerable. Guards against a Must() predicate drift away from RecurringJobIds.All.
        foreach (var id in RecurringJobIds.All)
            _validator.Validate(new TriggerRecurringJobCommand(id)).IsValid
                .ShouldBeTrue($"'{id}' is an allowlist member and must validate");
    }

    [Fact]
    public void Validate_NonAllowlistedId_Fails()
    {
        var result = _validator.Validate(new TriggerRecurringJobCommand("arbitrary-job; drop-table"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(TriggerRecurringJobCommand.RecurringJobId));
    }

    [Fact]
    public void Validate_CaseMismatchedId_Fails()
    {
        // Ordinal allowlist — an upper-cased known slug is NOT a member.
        var result = _validator.Validate(
            new TriggerRecurringJobCommand(RecurringJobIds.BackgroundMatching.ToUpperInvariant()));

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_EmptyId_Fails()
    {
        var result = _validator.Validate(new TriggerRecurringJobCommand(string.Empty));

        result.IsValid.ShouldBeFalse();
    }
}
