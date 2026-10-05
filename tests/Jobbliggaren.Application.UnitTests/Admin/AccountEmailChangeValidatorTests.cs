using Jobbliggaren.Application.Admin.Accounts.Commands.CancelAccountEmailChange;
using Jobbliggaren.Application.Admin.Accounts.Commands.RequestAccountEmailChange;
using Jobbliggaren.Application.Admin.Accounts.Queries.GetPendingAccountEmailChange;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin;

/// <summary>
/// #1975 — the administrator's three address-change messages are refused here, before the grant is redeemed and
/// before the volatile store is asked: the <c>{id:guid}</c> route admits <see cref="Guid.Empty"/>, so these rules are
/// what turn it into a 400.
/// </summary>
public class AccountEmailChangeValidatorTests
{
    private const string Grant = "AAECAwQFBgcICQoLDA0ODw"; // gitleaks:allow
    private const string NewEmail = "ny.adress@example.se";
    private static readonly Guid AccountId = Guid.Parse("7b6f4c1e-3a52-4f0e-9d8b-2c1a5e7f9b30");

    private static readonly RequestAccountEmailChangeCommandValidator Request = new();

    [Fact]
    public void A_well_formed_request_passes() =>
        Request.Validate(new RequestAccountEmailChangeCommand(AccountId, NewEmail, Grant)).IsValid.ShouldBeTrue();

    [Fact]
    public void A_request_for_the_empty_id_is_refused()
    {
        var result = Request.Validate(new RequestAccountEmailChangeCommand(Guid.Empty, NewEmail, Grant));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RequestAccountEmailChangeCommand.UserId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("inte-en-adress")]
    [InlineData("@example.se")]
    public void A_request_for_a_missing_or_malformed_address_is_refused(string? newEmail)
    {
        var result = Request.Validate(new RequestAccountEmailChangeCommand(AccountId, newEmail, Grant));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RequestAccountEmailChangeCommand.NewEmail));
    }

    [Fact]
    public void A_request_for_an_address_over_the_bound_is_refused()
    {
        var result = Request.Validate(
            new RequestAccountEmailChangeCommand(AccountId, new string('a', 250) + "@example.se", Grant));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(RequestAccountEmailChangeCommand.NewEmail));
    }

    [Fact]
    public void A_cancel_for_the_empty_id_is_refused_and_a_real_one_passes()
    {
        var validator = new CancelAccountEmailChangeCommandValidator();

        validator.Validate(new CancelAccountEmailChangeCommand(Guid.Empty)).IsValid.ShouldBeFalse();
        validator.Validate(new CancelAccountEmailChangeCommand(AccountId)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void A_read_for_the_empty_id_is_refused_and_a_real_one_passes()
    {
        var validator = new GetPendingAccountEmailChangeQueryValidator();

        validator.Validate(new GetPendingAccountEmailChangeQuery(Guid.Empty)).IsValid.ShouldBeFalse();
        validator.Validate(new GetPendingAccountEmailChangeQuery(AccountId)).IsValid.ShouldBeTrue();
    }
}
