using Jobbliggaren.Application.Auth.Commands.VerifyEmail;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #714 CONFIRM step — pins the validator for the PUBLIC verify-email command (parity with
/// <c>ConfirmEmailChangeCommandValidatorTests</c>). Uid + Token are carried by the activation link; a
/// malformed link must be a clean 400 before UserManager runs. Uid.NotEmpty is additionally load-
/// bearing: <c>ExtractAggregateId</c> returns Uid, so an empty Uid would otherwise reach the
/// post-success <c>AuditLogEntry.Create</c> empty-aggregateId throw.
/// </summary>
public class VerifyEmailCommandValidatorTests
{
    private readonly VerifyEmailCommandValidator _validator = new();

    private const string ValidToken = "opaque-url-safe-token"; // gitleaks:allow

    [Fact]
    public void Validate_ValidLink_Passes()
        => _validator.Validate(new VerifyEmailCommand(Guid.NewGuid(), ValidToken))
            .IsValid.ShouldBeTrue();

    [Fact]
    public void Validate_EmptyUid_Fails()
    {
        var result = _validator.Validate(new VerifyEmailCommand(Guid.Empty, ValidToken));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(VerifyEmailCommand.Uid));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_MissingToken_Fails(string? token)
    {
        var result = _validator.Validate(new VerifyEmailCommand(Guid.NewGuid(), token));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(e => e.PropertyName == nameof(VerifyEmailCommand.Token));
    }
}
