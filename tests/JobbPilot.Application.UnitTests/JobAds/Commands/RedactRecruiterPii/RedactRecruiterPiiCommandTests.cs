using JobbPilot.Application.Common.Abstractions;
using JobbPilot.Application.Common.Auditing;
using JobbPilot.Application.JobAds.Abstractions;
using JobbPilot.Application.JobAds.Commands.RedactRecruiterPii;
using JobbPilot.Domain.Common;
using NSubstitute;
using Shouldly;

namespace JobbPilot.Application.UnitTests.JobAds.Commands.RedactRecruiterPii;

/// <summary>
/// TD-73 prod-gating — RedactRecruiterPiiCommand + Handler.
/// Per ADR 0032 §8 amendment 2026-05-13 + ADR 0035 + CTO 2026-05-13.
///
/// Tester verifierar:
/// <list type="bullet">
/// <item>Email-typ delegerar till IRecruiterPiiPurger och returnerar rowsAffected</item>
/// <item>Name-typ defereras till TD-75 (NameNotSupportedYet)</item>
/// <item>IAuditableCommand + IAdminRequest-disciplin</item>
/// <item>AggregateId stabil under command-lifetime</item>
/// </list>
/// </summary>
public class RedactRecruiterPiiCommandTests
{
    [Fact]
    public async Task Handle_WithEmailType_DelegatesToPurgerAndReturnsRowsAffected()
    {
        var purger = Substitute.For<IRecruiterPiiPurger>();
        purger.RedactByEmailAsync("alice@example.com", Arg.Any<CancellationToken>())
            .Returns(7);
        var handler = new RedactRecruiterPiiCommandHandler(purger);
        var command = new RedactRecruiterPiiCommand("alice@example.com", RecruiterIdentifierType.Email);

        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(7);
        await purger.Received(1).RedactByEmailAsync("alice@example.com", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithNameType_ReturnsNameNotSupportedYetFailure()
    {
        var purger = Substitute.For<IRecruiterPiiPurger>();
        var handler = new RedactRecruiterPiiCommandHandler(purger);
        var command = new RedactRecruiterPiiCommand("Alice Anka", RecruiterIdentifierType.Name);

        var result = await handler.Handle(command, TestContext.Current.CancellationToken);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("RedactRecruiterPii.NameNotSupportedYet");
        await purger.DidNotReceive().RedactByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Command_IsAuditable_WithCorrectEventTypeAndAggregateType()
    {
        var command = new RedactRecruiterPiiCommand("alice@example.com", RecruiterIdentifierType.Email);

        ((IAuditableCommand)command).EventType.ShouldBe("Admin.RecruiterPiiRedacted");
        ((IAuditableCommand)command).AggregateType.ShouldBe("System.RecruiterPiiRedaction");
    }

    [Fact]
    public void Command_IsAdminRequest()
    {
        var command = new RedactRecruiterPiiCommand("alice@example.com", RecruiterIdentifierType.Email);
        command.ShouldBeAssignableTo<IAdminRequest>();
    }

    [Fact]
    public void ExtractAggregateId_ReturnsCommandRequestId_Stable()
    {
        var command = new RedactRecruiterPiiCommand("alice@example.com", RecruiterIdentifierType.Email);
        var result = Result.Success(3);

        var first = ((IAuditableCommand<Result<int>>)command).ExtractAggregateId(result);
        var second = ((IAuditableCommand<Result<int>>)command).ExtractAggregateId(result);

        first.ShouldNotBe(Guid.Empty);
        first.ShouldBe(second);  // RequestId är stabil under command-lifetime
        first.ShouldBe(command.RequestId);
    }

    [Fact]
    public void RequestId_DistinctPerCommandInstance()
    {
        var a = new RedactRecruiterPiiCommand("alice@example.com", RecruiterIdentifierType.Email);
        var b = new RedactRecruiterPiiCommand("alice@example.com", RecruiterIdentifierType.Email);

        a.RequestId.ShouldNotBe(b.RequestId);
    }
}
