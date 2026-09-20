using System.Net;
using System.Net.Http.Json;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Application.Auth;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure;
using Jobbliggaren.Infrastructure.Auth;
using Jobbliggaren.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Configuration;

/// <summary>
/// ADR 0083 Amendment 2026-08-03 + senior-cto-advisor D1 (2026-08-09) + security-auditor Major 12
/// (#1735) — the two conditions that must not boot outside Development/Test:
/// <list type="number">
/// <item>open WITHOUT email confirmation — legacy instant-login (an account bound to an address the
/// registrant may not own) plus the acknowledged-deferred duplicate-enumeration oracle, on a public
/// IP;</item>
/// <item>a sender that cannot deliver, whatever the flags say.</item>
/// </list>
/// Prerequisites are owned by <c>docs/runbooks/registration-gate.md</c>.
/// <para>
/// The predicate is unit-tested exhaustively here rather than through a failing host: the Production
/// smoke fixture exists to prove the host DOES boot, so a refusal case cannot live in it. What the
/// wiring tests buy is the half a predicate test cannot — that the validator is actually reachable
/// where it must be and absent where it must not be, which is where this class of guard usually dies.
/// </para>
/// </summary>
public class AuthOptionsValidatorTests
{
    /// <summary>
    /// A sender that delivers. Rule 2 keys on <see cref="IEmailSender.CanDeliver"/>, so every case
    /// that is NOT about delivery has to hold it fixed at the value the real delivering adapters
    /// emit; otherwise a rule-1 assertion could pass for rule 2's reason. That the Dev/Test default
    /// answers <see langword="true"/> is pinned elsewhere and not restated here —
    /// <c>AddEmailSenderGateTests.AddEmailSender_InDevelopmentOrTest_CanDeliver</c> owns that one
    /// clause; the Scaleway arm and the throwing arms have their own pins in the same file and in
    /// <c>ScalewayEmailProviderGateTests</c>.
    /// </summary>
    private static IEmailSender DeliveringSender()
    {
        var sender = Substitute.For<IEmailSender>();
        sender.CanDeliver.Returns(true);
        return sender;
    }

    /// <summary>
    /// The REAL <see cref="NullEmailSender"/>, not a substitute answering false. It is what
    /// <c>AddEmailSender</c> registers outside Development/Test with <c>Email:Provider</c> unset, so the
    /// refusal cases rest on the exact object that composition produces.
    /// </summary>
    private static NullEmailSender NonDeliveringSender() =>
        new(NullLogger<NullEmailSender>.Instance);

    private static AuthOptionsValidator ValidatorFor(
        string environmentName, IEmailSender? emailSender = null)
    {
        // Direct construction, not reflection: Infrastructure already carries an InternalsVisibleTo
        // for this assembly, and the sibling validator tests construct theirs the same way.
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName = environmentName;
        return new AuthOptionsValidator(env, emailSender ?? DeliveringSender());
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
        result.FailureMessage.ShouldContain("registration-gate.md");
        result.FailureMessage.ShouldContain(environmentName);
        // Rule 1's remedy key, in ENV-VAR form with the double underscore: it appears in rule 1's message
        // and nowhere else.
        result.FailureMessage.ShouldContain("Auth__RequireEmailConfirmation=true");
    }

    public static TheoryData<string, bool, bool> EnvironmentsAndTheFlagPairsRuleOneAdmits()
    {
        var rows = new TheoryData<string, bool, bool>();
        foreach (var environmentName in new[] { "Production", "Staging", "SomethingNobodyNamedYet" })
        {
            rows.Add(environmentName, false, false);
            rows.Add(environmentName, false, true);
            rows.Add(environmentName, true, true);
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(EnvironmentsAndTheFlagPairsRuleOneAdmits))]
    public void A_sender_that_cannot_deliver_refuses_to_boot_whatever_the_flags_say(
        string environmentName, bool open, bool confirm)
    {
        // (false, false) is the committed default composition: Email:Provider unset, registration closed.
        var result = ValidatorFor(environmentName, NonDeliveringSender())
            .Validate(null, Options(open, confirm));

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(environmentName);
        result.FailureMessage.ShouldContain("deploy/.env.example");
        // The remedy an operator can act on, and the sender that was actually registered.
        result.FailureMessage.ShouldContain("Email__Provider=Scaleway");
        result.FailureMessage.ShouldContain(nameof(NullEmailSender));
    }

    [Fact]
    public void A_closed_host_refuses_when_the_sender_cannot_deliver_and_boots_when_it_can()
    {
        // The crossing counterfactual for the theory above, in ONE test so a later tidy-up cannot
        // separate the control from the arm that gives it meaning. Same environment, same flags,
        // EXACTLY one input different: what the registered sender answers to CanDeliver. Without the
        // second half, "closed refuses in Production" would go on passing even if the rule
        // had degenerated into "Production always refuses" — which would take the whole host down
        // the day email goes live, i.e. the one day it must let the host boot.
        ValidatorFor("Production", NonDeliveringSender())
            .Validate(null, Options(open: false, confirm: false))
            .Failed.ShouldBeTrue();

        ValidatorFor("Production", DeliveringSender())
            .Validate(null, Options(open: false, confirm: false))
            .Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Every_other_combination_boots_in_Production(bool open, bool confirm)
    {
        // Rule 1 fires in ONE direction. The fail-safe default (both false, i.e. an absent Auth section)
        // must boot clean with a delivering sender — a guard that also broke the safe state would have
        // replaced one outage class with another. Every row's non-delivering half is the theory above.
        ValidatorFor("Production").Validate(null, Options(open, confirm)).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void The_dangerous_combination_is_exempt_in_Development_and_Test(string environmentName)
    {
        ValidatorFor(environmentName).Validate(null, Options(open: true, confirm: false))
            .Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void The_stranding_combination_is_exempt_in_Development_and_Test(string environmentName)
    {
        // Same allowlist, second rule. No composition produces the pair (Development,
        // non-delivering sender): AddEmailSender's Null fallback is gated on !Dev && !Test, the
        // Scaleway arm yields a sender that delivers or throws at registration, and every other value throws
        // (AddEmailSenderGateTests.AddEmailSender_InDevelopmentOrTest_CanDeliver measures it). The
        // pair is therefore declared unreachable, and what this pins is the predicate's ORDER: the
        // allowlist short-circuits BEFORE rule 2, so swapping the two checks turns this red.
        ValidatorFor(environmentName, NonDeliveringSender())
            .Validate(null, Options(open: true, confirm: true))
            .Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// The Api/Worker asymmetry, pinned at the call site rather than only in the rule. Both hosts call
    /// <c>AddEmailSender</c>, but only the Api composes a validator over <c>AuthOptions</c> — the
    /// Worker owns no registration or login surface, so a shared env file must not take it down for a
    /// condition it cannot exercise. Without these, the natural "helpful" edit (bind the validator in the Worker
    /// for parity, or move the check into the shared email seam) lands green.
    /// <para>
    /// All three run the same instrument over the same configuration and differ only in which
    /// composition method is called, so the two absences are a MEASUREMENT rather than two silences
    /// beside a differently-measured presence. Note what the shape does and does not catch: asserting
    /// on the registered <c>ServiceType</c> catches a validator placed in either seam, and the
    /// dangerous flags are present so an inline <c>throw</c> in <c>AddEmailSender</c> — that file's own
    /// idiom in the Scaleway arm — would surface as an exception rather than as a failed assertion. A check
    /// that neither registers nor throws would pass.
    /// </para>
    /// </summary>
    public class TheWorkerIsNotSubjectToTheGate
    {
        private static IConfiguration ConfigurationWithTheDangerousFlags() =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Read at registration time, and absence throws: Postgres by both identity
                    // modules, Redis by AddIdentityAndSessions alone. One dictionary for all three
                    // tests, so the only difference between them is which method is called.
                    ["ConnectionStrings:Postgres"] = "Host=localhost;Database=jobbliggaren;Username=x;Password=y",
                    ["ConnectionStrings:Redis"] = "localhost:6379",
                    [$"ConnectionStrings:{DependencyInjection.VolatileRedisConnectionStringName}"] = "localhost:6381",
                    [$"{AuthOptions.SectionName}:{nameof(AuthOptions.RegistrationsOpen)}"] = "true",
                    [$"{AuthOptions.SectionName}:{nameof(AuthOptions.RequireEmailConfirmation)}"] = "true",
                })
                .Build();

        [Fact]
        public void AddIdentityAndSessions_registers_the_validator_for_AuthOptions()
        {
            // The control the two absences below are measured against: same instrument, same
            // configuration, opposite outcome. Without it they would pass just as happily against a
            // build where nothing anywhere registers the validator.
            var services = new ServiceCollection();

            services.AddIdentityAndSessions(ConfigurationWithTheDangerousFlags());

            services.ShouldContain(d => d.ServiceType == typeof(IValidateOptions<AuthOptions>));
        }

        [Fact]
        public void AddCoreIdentityForWorker_registers_no_validator_for_AuthOptions()
        {
            var services = new ServiceCollection();

            services.AddCoreIdentityForWorker(ConfigurationWithTheDangerousFlags());

            services.ShouldNotContain(d => d.ServiceType == typeof(IValidateOptions<AuthOptions>));
        }

        [Fact]
        public void AddEmailSender_registers_no_validator_for_AuthOptions()
        {
            // The seam BOTH hosts share. A rule placed here would refuse the Worker's boot for a
            // registration or login flow the Worker does not serve.
            var env = Substitute.For<IHostEnvironment>();
            env.EnvironmentName.Returns("Production");
            var services = new ServiceCollection();

            services.AddEmailSender(ConfigurationWithTheDangerousFlags(), env);

            services.ShouldNotContain(d => d.ServiceType == typeof(IValidateOptions<AuthOptions>));
        }
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
                    acceptTerms = true,
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
