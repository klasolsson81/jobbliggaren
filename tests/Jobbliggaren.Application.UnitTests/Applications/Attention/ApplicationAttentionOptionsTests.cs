using System.ComponentModel.DataAnnotations;
using Jobbliggaren.Application.Applications.Attention;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Applications.Attention;

/// <summary>
/// Pins the <see cref="ApplicationAttentionOptions"/> cross-field invariant
/// (GhostSuggestDays ≥ NoResponseNudgeDays). The same <c>Validator</c> path runs at
/// boot via <c>ValidateDataAnnotations</c> + <c>ValidateOnStart</c>, so an inverted
/// operator config fails fast rather than silently making the nudge unreachable.
/// </summary>
public class ApplicationAttentionOptionsTests
{
    private static List<ValidationResult> Validate(ApplicationAttentionOptions options)
    {
        var results = new List<ValidationResult>();
        // validateAllProperties: true → runs the [Range] attributes AND IValidatableObject.
        Validator.TryValidateObject(
            options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void Validate_Defaults_PassValidation()
    {
        // Defaults: GhostSuggestDays 30 ≥ NoResponseNudgeDays 14.
        Validate(new ApplicationAttentionOptions()).ShouldBeEmpty();
    }

    [Fact]
    public void Validate_GhostSuggestEqualToNudge_PassValidation()
    {
        // Boundary: equal is allowed (ghost-suggest and nudge fire at the same threshold;
        // ghost-suggest, checked first, simply wins — the nudge is still reachable for
        // no other case, but the config is not self-contradictory).
        var options = new ApplicationAttentionOptions { NoResponseNudgeDays = 14, GhostSuggestDays = 14 };

        Validate(options).ShouldBeEmpty();
    }

    [Fact]
    public void Validate_GhostSuggestBelowNudge_FailsValidation()
    {
        // Inverted config makes the no-response nudge unreachable (ghost-suggest is
        // checked first) — must fail fast at boot, not silently misbehave.
        var options = new ApplicationAttentionOptions { NoResponseNudgeDays = 20, GhostSuggestDays = 10 };

        Validate(options).ShouldContain(r =>
            r.MemberNames.Contains(nameof(ApplicationAttentionOptions.GhostSuggestDays)));
    }
}
