using System.Net;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Infrastructure.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// ADR 0083 Amendment 2026-08-03 — the one combination that must not boot: public registration OPEN
/// without email confirmation, outside Development/Test. That is legacy instant-login (an account
/// bound to an address the registrant may not own) plus the acknowledged-deferred duplicate-
/// enumeration oracle, on a public IP. Prerequisites are owned by #734.
/// <para>
/// The predicate is unit-tested exhaustively here rather than through a failing host: the Production
/// smoke fixture exists to prove the host DOES boot, so a refusal case cannot live in it. What the
/// last test buys is the half a predicate test cannot: that the validator is actually WIRED, which is
/// where this class of guard usually dies.
/// </para>
/// </summary>
public class AuthOptionsValidatorTests
{
    private static AuthOptionsValidator ValidatorFor(string environmentName)
    {
        // Direct construction, not reflection: Infrastructure already carries an InternalsVisibleTo
        // for this assembly, and the sibling validator tests construct theirs the same way.
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName = environmentName;
        return new AuthOptionsValidator(env);
    }

    private static AuthOptions Options(bool open, bool confirm) =>
        new() { RegistrationsOpen = open, RequireEmailConfirmation = confirm };

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("SomethingNobodyNamedYet")]
    public void Open_without_email_confirmation_refuses_to_boot(string environmentName)
    {
        // Allowlist, not !IsProduction(): Staging and every unrecognised name must be covered, or the
        // guard exempts exactly the environments nobody thought about.
        var result = ValidatorFor(environmentName).Validate(null, Options(open: true, confirm: false));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("#734");
        result.FailureMessage.ShouldContain(environmentName);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Every_other_combination_boots_in_Production(bool open, bool confirm)
    {
        // Fires in ONE direction. The fail-safe default (both false, i.e. an absent Auth section) must
        // still boot clean — a guard that also broke the safe state would have replaced one outage
        // class with another.
        ValidatorFor("Production").Validate(null, Options(open, confirm)).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void The_dangerous_combination_is_exempt_in_Development_and_Test(string environmentName)
    {
        // Measured: the integration harness forces Development, so the instant-login bootstrap sites
        // are exempt by THIS clause and not by accident.
        ValidatorFor(environmentName).Validate(null, Options(open: true, confirm: false))
            .Succeeded.ShouldBeTrue();
    }

    [Collection("Api")]
    public class Wiring(ApiFactory factory)
    {
        [Fact]
        public void The_validator_is_registered_for_AuthOptions()
        {
            // A correct predicate that nothing resolves is a guard with no reader — the exact failure
            // mode this whole change exists to close, one level up.
            var validators = factory.Services.GetServices<IValidateOptions<AuthOptions>>();

            validators.ShouldContain(v => v is AuthOptionsValidator);
        }

        /// <summary>
        /// The boot announcement, pinned against BEHAVIOUR rather than configuration. This change
        /// argues that a posture observable only by attempting to register is a posture nobody
        /// checks — and that argument applies to the announcement itself, which otherwise ships as
        /// the one unguarded artefact in the diff (removing the call left every suite green).
        /// <para>
        /// Asserted on the closed-registration host, which already exists: the line it emits and the
        /// 503 it serves come from the same process, so they cannot diverge without one of the two
        /// assertions below failing.
        /// </para>
        /// </summary>
        [Fact]
        public async Task The_host_announces_the_gate_it_actually_enforces()
        {
            var client = factory.CreateRegistrationsClosedClient();

            var response = await client.PostAsJsonAsync(
                "/api/v1/auth/register",
                new
                {
                    email = $"announce-{Guid.NewGuid()}@example.com",
                    password = "T3stlosen123456",
                    displayName = "Test User",
                },
                TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(
                HttpStatusCode.ServiceUnavailable, "this host holds the gate closed");

            var announcement = factory.ClosedHostLogs.SingleOrDefault(l => l.EventId.Id == 4300);
            announcement.ShouldNotBeNull(
                "the gate must announce itself once per process (EventId 4300)");
            announcement.Message.ShouldContain("CLOSED");
            // Both flags, because an open gate WITHOUT email confirmation is the dangerous
            // combination — announcing only the gate would reproduce this defect class one flag over.
            announcement.Message.ShouldContain("email confirmation:");
        }
    }
}
