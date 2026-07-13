using Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Commands.EraseRecruiterAds;

/// <summary>
/// The mandatory dry run — the single control standing between an operator and irreversible,
/// corpus-wide destruction (#842).
/// </summary>
/// <remarks>
/// <b>This file is a regression guard against a real, shipped defect, and it is worth stating what
/// it was.</b> The rule was first written as one chain:
/// <code>
/// RuleFor(c => c.ConfirmedJobAdIds)
///     .NotNull().When(c => !c.DryRun)
///     .Must(...).When(c => !c.DryRun &amp;&amp; c.ConfirmedJobAdIds is not null);
/// </code>
/// FluentValidation's <c>.When()</c> defaults to <c>ApplyConditionTo.AllValidators</c> — it re-scopes
/// EVERY validator in the chain, not just the one it follows. So the second condition silently
/// applied to <c>NotNull()</c> as well, and <c>NotNull()</c> could therefore only run when the value
/// was <b>not null</b>. <b>It could never fire.</b> The mandatory dry run was a control that looked
/// like it worked and never ran once — which is precisely the defect class this entire issue exists
/// to close, reproduced inside its own fix. Each condition now gets its own <c>RuleFor</c>, which a
/// later chain link cannot re-scope.
/// </remarks>
public class EraseRecruiterAdsCommandValidatorTests
{
    private readonly EraseRecruiterAdsCommandValidator _validator = new();

    private static EraseRecruiterAdsCommand Command(
        string identifier = "anna.karlsson@acme.se",
        bool dryRun = true,
        IReadOnlyList<Guid>? confirmedIds = null) =>
        new(Guid.NewGuid(), identifier, dryRun, confirmedIds);

    /// <summary>
    /// <b>THE test.</b> If this ever passes-by-vacuity again, an operator can destroy the corpus
    /// without having looked at a single ad.
    /// </summary>
    [Fact]
    public void A_destructive_call_with_NO_confirmed_ids_is_INVALID()
    {
        var result = _validator.Validate(Command(dryRun: false, confirmedIds: null));

        result.IsValid.ShouldBeFalse(
            "you cannot erase without having reviewed. If this passes, the mandatory dry run does "
            + "not exist — which is exactly what shipped for one commit.");

        result.Errors.ShouldContain(e => e.PropertyName == nameof(EraseRecruiterAdsCommand.ConfirmedJobAdIds));
    }

    [Fact]
    public void A_dry_run_needs_no_confirmed_ids()
    {
        _validator.Validate(Command(dryRun: true, confirmedIds: null)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void A_destructive_call_WITH_confirmed_ids_is_valid()
    {
        _validator.Validate(Command(dryRun: false, confirmedIds: [Guid.NewGuid()]))
            .IsValid.ShouldBeTrue();
    }

    /// <summary>
    /// An empty list is a legitimate answer — "I reviewed the matches and none of them are hers."
    /// It must not be confused with "I never looked", which is what <c>null</c> means.
    /// </summary>
    [Fact]
    public void A_destructive_call_confirming_ZERO_ads_after_review_is_valid()
    {
        _validator.Validate(Command(dryRun: false, confirmedIds: [])).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Duplicate_confirmed_ids_are_INVALID()
    {
        var id = Guid.NewGuid();

        _validator.Validate(Command(dryRun: false, confirmedIds: [id, id]))
            .IsValid.ShouldBeFalse("a duplicate would inflate the count we report to a data subject.");
    }

    /// <summary>
    /// The substring channel matches any occurrence, so a one-character identifier is a corpus-wide
    /// destruction primitive. The dry run would reveal it — but a floor that makes the mistake
    /// unrepresentable beats a review step that merely makes it visible.
    /// </summary>
    [Theory]
    [InlineData("a")]
    [InlineData("an")]
    [InlineData("ann")]
    public void A_dangerously_short_identifier_is_INVALID(string identifier)
    {
        _validator.Validate(Command(identifier: identifier)).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void An_identifier_at_the_floor_is_valid()
    {
        EraseRecruiterAdsCommandValidator.MinIdentifierLength.ShouldBe(4);

        _validator.Validate(Command(identifier: "Anna")).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void An_empty_identifier_is_INVALID()
    {
        _validator.Validate(Command(identifier: "  ")).IsValid.ShouldBeFalse();
    }
}
