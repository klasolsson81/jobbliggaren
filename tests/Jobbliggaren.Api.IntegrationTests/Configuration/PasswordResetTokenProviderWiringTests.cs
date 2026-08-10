using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// #1171 (dotnet-architect 2026-08-10) — the password-reset token provider's WIRING, read off the real
/// DI graph. Two lines in <c>AddIdentityAndSessions</c> have to agree: the assignment
/// <c>opts.Tokens.PasswordResetTokenProvider = "PasswordReset"</c> and the matching
/// <c>.AddTokenProvider&lt;…&gt;("PasswordReset")</c>.
/// <para>
/// <b>Why this exists: both broken states are SILENT, and one of them is silent by the design of the
/// very handler that depends on it.</b>
/// </para>
/// <list type="bullet">
/// <item>Lose the ASSIGNMENT and Identity falls back to <c>Default</c> — a working 24-hour token, while
/// <c>EmailTemplates.PasswordReset</c> keeps promising <c>LifespanMinutes</c>. The published promise and
/// the enforcement diverge with nothing red.</item>
/// <item>Lose the REGISTRATION and minting throws <c>NotSupportedException</c> — which lands inside
/// <c>RequestPasswordResetCommandHandler</c>'s uniform-202 catch, by construction. The user is told to
/// check an inbox that will never receive anything, and the only trace is one Warning line. The catch
/// that protects against enumeration converts the fail-loud half into a silent one too.</item>
/// </list>
/// <para>
/// The existing template test pins the mail body's PROMISE against the constant; nothing pinned the
/// ENFORCEMENT. Deliberately in the shared <c>Api</c> collection — no fourth
/// <c>WebApplicationFactory</c> (#1190's ceiling).
/// </para>
/// </summary>
[Collection("Api")]
public class PasswordResetTokenProviderWiringTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public void PasswordReset_tokens_resolve_to_the_dedicated_provider_and_not_the_shared_default()
    {
        var identity = _factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;

        identity.Tokens.PasswordResetTokenProvider.ShouldNotBe(
            TokenOptions.DefaultProvider,
            "falling back to the shared Default provider is the silent 24h failure this test exists for");
        identity.Tokens.ProviderMap.ShouldContainKey(
            identity.Tokens.PasswordResetTokenProvider,
            "an assignment without a matching AddTokenProvider throws NotSupportedException at mint time, "
            + "which the handler's uniform-202 catch swallows into a 'check your inbox' that never arrives");
    }

    [Fact]
    public void The_dedicated_provider_enforces_the_lifespan_the_email_body_promises()
    {
        var options = _factory.Services
            .GetRequiredService<IOptions<PasswordResetTokenProviderOptions>>().Value;

        // Read the constant, never the literal 60: EmailTemplates.PasswordReset interpolates the SAME
        // constant into the mail, so this asserts that promise and enforcement cannot drift — including
        // against a future Configure<PasswordResetTokenProviderOptions> that overwrites the ctor default.
        options.TokenLifespan.ShouldBe(
            TimeSpan.FromMinutes(PasswordResetTokenProviderOptions.LifespanMinutes));
    }

    [Fact]
    public void The_two_long_lived_token_kinds_keep_the_shared_24_hour_default()
    {
        // The counterfactual, and the reason the dedicated provider exists at all. Without it, a future
        // "simplification" back to Configure<DataProtectionTokenProviderOptions> would shorten these two
        // as well — and their mail bodies promise 24 hours in published copy.
        var identity = _factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;
        var shared = _factory.Services
            .GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value;

        identity.Tokens.EmailConfirmationTokenProvider.ShouldBe(TokenOptions.DefaultProvider);
        identity.Tokens.ChangeEmailTokenProvider.ShouldBe(TokenOptions.DefaultProvider);
        shared.TokenLifespan.ShouldBe(TimeSpan.FromDays(1));
    }
}
