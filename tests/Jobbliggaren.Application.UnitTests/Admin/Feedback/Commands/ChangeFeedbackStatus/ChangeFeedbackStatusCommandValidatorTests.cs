using Jobbliggaren.Application.Admin.Feedback.Commands.ChangeFeedbackStatus;
using Jobbliggaren.Domain.Feedback;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Feedback.Commands.ChangeFeedbackStatus;

public sealed class ChangeFeedbackStatusCommandValidatorTests
{
    private readonly ChangeFeedbackStatusCommandValidator _validator = new();

    [Fact]
    public void Validate_AnIdAndADefinedStatus_Passes() =>
        _validator.Validate(new ChangeFeedbackStatusCommand(Guid.NewGuid(), FeedbackStatus.Resolved)).IsValid.ShouldBeTrue();

    [Fact]
    public void Validate_AnEmptyId_Fails() =>
        _validator.Validate(new ChangeFeedbackStatusCommand(Guid.Empty, FeedbackStatus.Resolved)).Errors
            .ShouldContain(e => e.PropertyName == nameof(ChangeFeedbackStatusCommand.Id));

    [Fact]
    public void Validate_AnUndefinedStatus_Fails() =>
        _validator.Validate(new ChangeFeedbackStatusCommand(Guid.NewGuid(), (FeedbackStatus)42)).Errors
            .ShouldContain(e => e.PropertyName == nameof(ChangeFeedbackStatusCommand.Status));
}
