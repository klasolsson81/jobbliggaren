using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// ADR 0080 Vag 4 PR-4a — smoke coverage that the two non-transactional
/// <see cref="IEmailSender"/> impls implement the new
/// <see cref="IEmailSender.SendMatchNotificationEmailAsync"/> method without throwing:
/// <see cref="NullEmailSender"/> suppresses (drops the mail, no recipient/body logged);
/// <see cref="ConsoleEmailSender"/> builds the template and logs it (dev/Test only).
/// </summary>
public class ConsoleAndNullEmailSenderMatchNotificationTests
{
    private static MatchNotificationEmail SampleContent() =>
        new(
            MatchNotificationKind.Direct,
            Cadence: null,
            Items: [new MatchNotificationItem("Backend-utvecklare", "Acme AB", "Toppmatch")],
            TotalCount: 1);

    [Fact]
    public async Task NullEmailSender_ShouldSuppressMatchNotification_WithoutThrowing()
    {
        var sut = new NullEmailSender(Substitute.For<ILogger<NullEmailSender>>());

        var act = async () => await sut.SendMatchNotificationEmailAsync(
            "user@example.com", SampleContent(), CancellationToken.None);

        await act.ShouldNotThrowAsync();
    }

    [Fact]
    public async Task ConsoleEmailSender_ShouldBuildAndLogMatchNotification_WithoutThrowing()
    {
        var options = Options.Create(new EmailOptions { BaseUrl = "https://jobbliggaren.se" });
        var sut = new ConsoleEmailSender(Substitute.For<ILogger<ConsoleEmailSender>>(), options);

        var act = async () => await sut.SendMatchNotificationEmailAsync(
            "user@example.com", SampleContent(), CancellationToken.None);

        await act.ShouldNotThrowAsync();
    }
}
