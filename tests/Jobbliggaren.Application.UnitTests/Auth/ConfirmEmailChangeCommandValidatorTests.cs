using Jobbliggaren.Application.Auth.Commands.ConfirmEmailChange;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #679 CONFIRM step — pins the validator for the PUBLIC confirm command. UserId/NewEmail/Token are
/// all carried by the confirmation link; a malformed link must be a clean 400 before UserManager runs
/// (and, for UserId, before the post-success <c>AuditLogEntry.Create</c> empty-aggregateId throw —
/// <c>ExtractAggregateId</c> returns UserId).
/// </summary>
public class ConfirmEmailChangeCommandValidatorTests
{
    private readonly ConfirmEmailChangeCommandValidator _validator = new();

    private const string ValidEmail = "ny.adress@example.se";
    private const string ValidToken = "opaque-url-safe-token"; // gitleaks:allow

    [Fact]
    public void Validate_ValidLink_Passes()
        => _validator.Validate(new ConfirmEmailChangeCommand(Guid.NewGuid(), ValidEmail, ValidToken))
            .IsValid.ShouldBeTrue();

    [Fact]
    public void Validate_EmptyUserId_Fails()
    {
        var result = _validator.Validate(new ConfirmEmailChangeCommand(Guid.Empty, ValidEmail, ValidToken));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ConfirmEmailChangeCommand.UserId));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_MissingNewEmail_Fails(string? email)
    {
        var result = _validator.Validate(new ConfirmEmailChangeCommand(Guid.NewGuid(), email, ValidToken));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ConfirmEmailChangeCommand.NewEmail));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@no-local.se")]
    [InlineData("no-domain@")]
    public void Validate_MalformedNewEmail_Fails(string email)
    {
        var result = _validator.Validate(new ConfirmEmailChangeCommand(Guid.NewGuid(), email, ValidToken));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ConfirmEmailChangeCommand.NewEmail));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_MissingToken_Fails(string? token)
    {
        var result = _validator.Validate(new ConfirmEmailChangeCommand(Guid.NewGuid(), ValidEmail, token));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(ConfirmEmailChangeCommand.Token));
    }
}
