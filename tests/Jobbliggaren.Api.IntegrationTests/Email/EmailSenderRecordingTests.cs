using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Email;

/// <summary>
/// #241 — locks the integration host onto the recording email fake instead of any real provider.
/// <para>
/// Regression guard: before #241, a gitignored <c>appsettings.Local.json</c> with
/// <c>Email:Provider=Resend</c> + a live key made the host resolve <c>ResendEmailSender</c>, so
/// email-success tests 500'd locally (403 test-mode) while passing in CI (no Local.json → Console).
/// This test fails loudly if anyone drops the <see cref="ApiFactory"/> override and lets a real
/// provider back into the integration host.
/// </para>
/// </summary>
[Collection("Api")]
public class EmailSenderRecordingTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public void Host_resolves_the_recording_email_fake_never_a_real_provider()
    {
        using var scope = _factory.Services.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        sender.ShouldBeOfType<RecordingEmailSender>();
    }
}
