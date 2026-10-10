using FluentValidation;

namespace Jobbliggaren.Application.Admin.Feedback.Commands.SendFeedbackReply;

public sealed class SendFeedbackReplyCommandValidator : AbstractValidator<SendFeedbackReplyCommand>
{
    public SendFeedbackReplyCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.ReplyKey).NotEmpty();
    }
}
