using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Auth;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Email;

/// <summary>
/// #241 — locks the integration host onto the recording email fake instead of any real provider.
/// <para>
/// Regression guard: before #241, a gitignored <c>appsettings.Local.json</c> with
/// <c>Email:Provider=Scaleway</c> + live API keys would make the host resolve
/// <c>ScalewayEmailSender</c>, so email-success tests 500'd locally while passing in CI
/// (no Local.json → Console).
/// This test fails loudly if anyone drops the <see cref="ApiFactory"/> override and lets a real
/// provider back into the integration host.
/// </para>
/// <para>
/// Since #1735 the host wraps the fake in the Development-only login-code capture, as the Development
/// composition wraps its sender, so the resolved type is the wrapper. What the guard needs is where a send
/// ends up, and that is asserted: a send through the resolved sender lands in the fake.
/// </para>
/// </summary>
[Collection("Api")]
public class EmailSenderRecordingTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task Host_resolves_the_recording_email_fake_never_a_real_provider()
    {
        using var scope = _factory.Services.CreateScope();
        var recipient = $"recording-{Guid.NewGuid():N}@example.com";

        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        await sender.SendEmailChangedNotificationAsync(recipient, TestContext.Current.CancellationToken);

        sender.ShouldBeOfType<DevLoginCodeCapturingEmailSender>();
        _factory.Emails.Sent.ShouldContain(
            new RecordedEmail(RecordedEmailKind.EmailChangedNotification, recipient));
    }
}
