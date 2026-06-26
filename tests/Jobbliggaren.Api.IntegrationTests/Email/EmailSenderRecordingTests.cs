using System.Net;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Common.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Email;

/// <summary>
/// #241 — locks the integration host onto the recording email fake instead of any real provider,
/// and proves the fake records an email side-effect so tests can positively assert it.
/// <para>
/// Regression guard: before #241, a gitignored <c>appsettings.Local.json</c> with
/// <c>Email:Provider=Resend</c> + a live key made the host resolve <c>ResendEmailSender</c>, so four
/// email-success tests 500'd locally (403 test-mode) while passing in CI (no Local.json → Console).
/// The first test fails loudly if anyone drops the <see cref="ApiFactory"/> override and lets a real
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

    [Fact]
    public async Task Waitlist_signup_records_a_confirmation_email_without_the_network()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();
        var email = $"record-{Guid.NewGuid()}@example.com";

        var response = await client.PostAsJsonAsync(
            "/api/v1/waitlist/",
            new
            {
                email,
                name = "Recording Fake Testperson",
                motivation = "Integration-test som verifierar att bekräftelsemejlet köas via faket.",
                marketingEmailAccepted = false,
            },
            ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Assert by the unique recipient — the fake's collection is shared across the [Collection("Api")]
        // lifetime, so a count assertion would race other tests; a unique-email match is isolation-safe.
        _factory.Emails.Sent.ShouldContain(e =>
            e.Kind == RecordedEmailKind.WaitlistConfirmation &&
            string.Equals(e.ToEmail, email, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Waitlist_idempotent_resignup_records_only_one_confirmation_email()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = _factory.CreateClient();
        var email = $"record-dup-{Guid.NewGuid()}@example.com";
        var payload = new
        {
            email,
            name = "Recording Fake Testperson",
            motivation = "Integration-test som verifierar att re-signup inte köar ett andra bekräftelsemejl.",
            marketingEmailAccepted = false,
        };

        var first = await client.PostAsJsonAsync("/api/v1/waitlist/", payload, ct);
        var second = await client.PostAsJsonAsync("/api/v1/waitlist/", payload, ct);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);

        // GDPR-relevant invariant: an idempotent re-signup must NOT queue a second confirmation — the
        // handler returns before SendWaitlistConfirmationAsync on the existing-Pending path. Count by the
        // unique recipient so the shared [Collection("Api")] queue stays isolation-safe.
        _factory.Emails.Sent
            .Count(e => e.Kind == RecordedEmailKind.WaitlistConfirmation &&
                        string.Equals(e.ToEmail, email, StringComparison.OrdinalIgnoreCase))
            .ShouldBe(1);
    }
}
