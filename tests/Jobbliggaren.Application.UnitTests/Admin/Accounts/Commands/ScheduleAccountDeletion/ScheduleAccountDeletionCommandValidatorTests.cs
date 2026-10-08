using Jobbliggaren.Application.Admin.Accounts.Commands.ScheduleAccountDeletion;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Accounts.Commands.ScheduleAccountDeletion;

public sealed class ScheduleAccountDeletionCommandValidatorTests
{
    private const string Grant = "an-opaque-reauth-grant"; // gitleaks:allow

    [Fact]
    public void Validate_ShouldAcceptTheTargetAndGrant_WhenBothArePresent() =>
        new ScheduleAccountDeletionCommandValidator().Validate(new ScheduleAccountDeletionCommand(Guid.NewGuid(), Grant))
            .IsValid.ShouldBeTrue();

    [Fact]
    public void Validate_ShouldRefuseTheTargetBeforeGrantRedemption_WhenTheTargetIdIsEmpty()
    {
        var result = new ScheduleAccountDeletionCommandValidator().Validate(new ScheduleAccountDeletionCommand(Guid.Empty, Grant));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.PropertyName == nameof(ScheduleAccountDeletionCommand.UserId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_ShouldRefuseTheGrant_WhenItIsMissing(string? grant)
    {
        var result = new ScheduleAccountDeletionCommandValidator().Validate(new ScheduleAccountDeletionCommand(Guid.NewGuid(), grant));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.PropertyName == nameof(ScheduleAccountDeletionCommand.ReauthGrant));
    }
}
