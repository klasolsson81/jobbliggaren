using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Infrastructure.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Email;

/// <summary>
/// ADR 0087 D5 (#311 PR-4) — smoke coverage that the two non-transactional
/// <see cref="IEmailSender"/> impls implement the new
/// <see cref="IEmailSender.SendFollowedCompanyNotificationEmailAsync"/> without throwing:
/// <see cref="NullEmailSender"/> suppresses; <see cref="ConsoleEmailSender"/> builds the template
/// and logs it (dev/Test only).
/// </summary>
public class ConsoleAndNullEmailSenderFollowedCompanyTests
{
    private static FollowedCompanyNotificationEmail SampleContent() =>
        new(
            DigestCadence.Weekly,
            Items: [new FollowedCompanyAdItem("Backend-utvecklare", "Acme AB")],
            TotalCount: 1);

    private static FollowedCompanyNotificationIdempotencyKey SampleKey() =>
        FollowedCompanyNotificationIdempotencyKey.ForDigest(
            Guid.NewGuid(), DigestCadence.Weekly, [Guid.NewGuid()]);

    [Fact]
    public async Task NullEmailSender_ShouldSuppressFollowedCompanyNotification_WithoutThrowing()
    {
        var sut = new NullEmailSender(Substitute.For<ILogger<NullEmailSender>>());

        var act = async () => await sut.SendFollowedCompanyNotificationEmailAsync(
            "user@example.com", SampleContent(), SampleKey(), CancellationToken.None);

        await act.ShouldNotThrowAsync();
    }

    [Fact]
    public async Task ConsoleEmailSender_ShouldBuildAndLogFollowedCompanyNotification_WithoutThrowing()
    {
        var options = Options.Create(new EmailOptions { BaseUrl = "https://jobbliggaren.se" });
        var sut = new ConsoleEmailSender(Substitute.For<ILogger<ConsoleEmailSender>>(), options);

        var act = async () => await sut.SendFollowedCompanyNotificationEmailAsync(
            "user@example.com", SampleContent(), SampleKey(), CancellationToken.None);

        await act.ShouldNotThrowAsync();
    }
}
