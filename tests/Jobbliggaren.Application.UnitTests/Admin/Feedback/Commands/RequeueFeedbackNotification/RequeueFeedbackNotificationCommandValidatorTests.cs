using Jobbliggaren.Application.Admin.Feedback.Commands.RequeueFeedbackNotification;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Feedback.Commands.RequeueFeedbackNotification;

public sealed class RequeueFeedbackNotificationCommandValidatorTests
{
    private readonly RequeueFeedbackNotificationCommandValidator _validator = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validate_AnId_PassesWhateverTheAcknowledgement(bool acknowledged) =>
        _validator.Validate(new RequeueFeedbackNotificationCommand(Guid.NewGuid(), acknowledged)).IsValid.ShouldBeTrue();

    [Fact]
    public void Validate_AnEmptyId_Fails() =>
        _validator.Validate(new RequeueFeedbackNotificationCommand(Guid.Empty, true)).Errors
            .ShouldContain(e => e.PropertyName == nameof(RequeueFeedbackNotificationCommand.Id));
}
