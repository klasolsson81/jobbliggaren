using FluentValidation;
using FluentValidation.Results;
using JobbPilot.Application.Common.Behaviors;
using Mediator;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Common.Behaviors;

public class ValidationBehaviorTests
{
    private static MessageHandlerDelegate<TestCommand, string> NextReturning(string value) =>
        (_, _) => ValueTask.FromResult(value);

    [Fact]
    public async Task Handle_WithNoValidators_CallsNextAndReturnsResult()
    {
        var behavior = new ValidationBehavior<TestCommand, string>([]);
        var result = await behavior.Handle(new TestCommand("x"), NextReturning("ok"), CancellationToken.None);
        result.ShouldBe("ok");
    }

    [Fact]
    public async Task Handle_WithPassingValidator_CallsNext()
    {
        var validator = Substitute.For<IValidator<TestCommand>>();
        validator.ValidateAsync(Arg.Any<ValidationContext<TestCommand>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());

        var behavior = new ValidationBehavior<TestCommand, string>([validator]);
        var result = await behavior.Handle(new TestCommand("x"), NextReturning("ok"), CancellationToken.None);

        result.ShouldBe("ok");
    }

    [Fact]
    public async Task Handle_WithFailingValidator_ThrowsValidationException()
    {
        var validator = Substitute.For<IValidator<TestCommand>>();
        validator.ValidateAsync(Arg.Any<ValidationContext<TestCommand>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult([new ValidationFailure("Payload", "Krävs.")]));

        var behavior = new ValidationBehavior<TestCommand, string>([validator]);

        await Should.ThrowAsync<Application.Common.Exceptions.ValidationException>(
            () => behavior.Handle(new TestCommand(""), NextReturning("ok"), CancellationToken.None).AsTask());
    }
}
