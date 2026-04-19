using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Behaviors;
using Mediator;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.Common.Behaviors;

public class UnitOfWorkBehaviorTests
{
    private readonly IAppDbContext _dbContext = Substitute.For<IAppDbContext>();

    [Fact]
    public async Task Handle_ForCommand_CallsSaveChangesAfterNext()
    {
        var behavior = new UnitOfWorkBehavior<TestCommand, string>(_dbContext);
        MessageHandlerDelegate<TestCommand, string> next =
            (_, _) => ValueTask.FromResult("ok");

        await behavior.Handle(new TestCommand("x"), next, CancellationToken.None);

        await _dbContext.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ForCommand_ReturnsSameResponseAsNext()
    {
        var behavior = new UnitOfWorkBehavior<TestCommand, string>(_dbContext);
        MessageHandlerDelegate<TestCommand, string> next =
            (_, _) => ValueTask.FromResult("expected");

        var result = await behavior.Handle(new TestCommand("x"), next, CancellationToken.None);

        result.ShouldBe("expected");
    }
}
