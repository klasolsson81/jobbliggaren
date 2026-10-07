using Jobbliggaren.Application.Auth.Commands.CompleteAccountEmailChange;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #1975 — the owner's presentation on <c>/adressbyte</c> is refused here when malformed, so it never reaches the store
/// and spends none of the change's attempts.
/// </summary>
public class CompleteAccountEmailChangeCommandValidatorTests
{
    private const string Current = "gammal.adress@example.se";
    private const string NewEmail = "ny.adress@example.se";
    private const string Code = "042917";

    private static readonly CompleteAccountEmailChangeCommandValidator Validator = new();

    [Fact]
    public void A_well_formed_presentation_passes() =>
        Validator.Validate(new CompleteAccountEmailChangeCommand(Current, NewEmail, Code)).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("inte-en-adress")]
    public void A_missing_or_malformed_current_address_is_refused(string? current)
    {
        var result = Validator.Validate(new CompleteAccountEmailChangeCommand(current, NewEmail, Code));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(CompleteAccountEmailChangeCommand.CurrentEmail));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("inte-en-adress")]
    public void A_missing_or_malformed_new_address_is_refused(string? newEmail)
    {
        var result = Validator.Validate(new CompleteAccountEmailChangeCommand(Current, newEmail, Code));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(CompleteAccountEmailChangeCommand.NewEmail));
    }

    [Fact]
    public void An_address_over_the_bound_is_refused()
    {
        var tooLong = new string('a', 250) + "@example.se";

        Validator.Validate(new CompleteAccountEmailChangeCommand(tooLong, NewEmail, Code)).IsValid.ShouldBeFalse();
        Validator.Validate(new CompleteAccountEmailChangeCommand(Current, tooLong, Code)).IsValid.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("04291")]
    [InlineData("0429170")]
    [InlineData("04291a")]
    public void A_code_not_of_the_minted_shape_is_refused(string? code)
    {
        var result = Validator.Validate(new CompleteAccountEmailChangeCommand(Current, NewEmail, code));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(CompleteAccountEmailChangeCommand.Code));
    }
}
