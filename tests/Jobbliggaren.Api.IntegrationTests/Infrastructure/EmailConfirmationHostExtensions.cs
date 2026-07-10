using Jobbliggaren.Application.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Jobbliggaren.Api.IntegrationTests.Infrastructure;

/// <summary>
/// #714 — builds an <see cref="HttpClient"/> against a host with email-confirmation-first registration
/// forced ON (<c>Auth:RequireEmailConfirmation</c>), reusing the <see cref="ApiFactory"/>'s
/// Testcontainers Postgres/Redis + the shared <c>RecordingEmailSender</c> (so
/// <c>factory.Emails.Sent</c> still captures sends). The base ApiFactory pins the flag OFF (protecting
/// the 142 instant-login bootstrap sites); this <c>PostConfigure(true)</c> is registered AFTER it and
/// therefore wins for the flag-ON test classes (CTO-bind Risk 3).
/// </summary>
internal static class EmailConfirmationHostExtensions
{
    public static HttpClient CreateEmailConfirmationClient(this ApiFactory factory) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<AuthOptions>(o => o.RequireEmailConfirmation = true))).CreateClient();
}
