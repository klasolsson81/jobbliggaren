using Jobbliggaren.Application.Admin.Feedback.Commands.SendFeedbackReply;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Admin.Feedback.Commands.SendFeedbackReply;

public sealed class SendFeedbackReplyCommandValidatorTests
{
    private readonly SendFeedbackReplyCommandValidator _validator = new();

    [Fact]
    public void Validate_AnIdAndAKey_Passes() =>
        _validator.Validate(new SendFeedbackReplyCommand(Guid.NewGuid(), Guid.NewGuid(), "Tack.")).IsValid.ShouldBeTrue();

    [Fact]
    public void Validate_AnEmptyId_Fails() =>
        _validator.Validate(new SendFeedbackReplyCommand(Guid.Empty, Guid.NewGuid(), "Tack.")).Errors
            .ShouldContain(e => e.PropertyName == nameof(SendFeedbackReplyCommand.Id));

    [Fact]
    public void Validate_AnEmptyKey_Fails() =>
        _validator.Validate(new SendFeedbackReplyCommand(Guid.NewGuid(), Guid.Empty, "Tack.")).Errors
            .ShouldContain(e => e.PropertyName == nameof(SendFeedbackReplyCommand.ReplyKey));
}
