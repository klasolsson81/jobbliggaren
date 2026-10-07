using Jobbliggaren.Application.Admin.Accounts.Commands.ReinstateAccount;
using Jobbliggaren.Application.Admin.Accounts.Commands.SuspendAccount;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Accounts.Access;

public sealed class AccountAccessCommandValidatorTests
{
    private const string Grant = "an-opaque-reauth-grant"; // gitleaks:allow

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_TargetAndGrant_AcceptsBothTransitions(bool suspend) =>
        Validate(suspend, Guid.NewGuid(), Grant).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validate_EmptyTarget_RefusesBeforeGrantRedemption(bool suspend)
    {
        var result = Validate(suspend, Guid.Empty, Grant);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.PropertyName == nameof(SuspendAccountCommand.UserId));
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(true, "")]
    [InlineData(true, "   ")]
    [InlineData(false, null)]
    [InlineData(false, "")]
    [InlineData(false, "   ")]
    public void Validate_MissingGrant_RefusesBothTransitions(bool suspend, string? grant)
    {
        var result = Validate(suspend, Guid.NewGuid(), grant);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.PropertyName == nameof(SuspendAccountCommand.ReauthGrant));
    }

    private static FluentValidation.Results.ValidationResult Validate(bool suspend, Guid userId, string? grant) =>
        suspend
            ? new SuspendAccountCommandValidator().Validate(new SuspendAccountCommand(userId, grant))
            : new ReinstateAccountCommandValidator().Validate(new ReinstateAccountCommand(userId, grant));
}
