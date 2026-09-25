using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
/// ADR 0083 Amendment 2026-08-03 + security-auditor Major 12 (#1735) + ADR 0142 D10 — the condition that
/// must not boot outside Development/Test: a sender that cannot deliver, whatever the registration gate says.
/// Prerequisites are owned by <c>docs/runbooks/registration-gate.md</c>.
/// <para>
/// The predicate is unit-tested exhaustively here. What the
/// wiring tests buy is the half a predicate test cannot — that the validator is actually reachable
/// where it must be and absent where it must not be, which is where this class of guard usually dies.
/// </para>
/// </summary>
public class AuthOptionsValidatorTests
{
    /// <summary>
    /// A sender that delivers. The rule keys on <see cref="IEmailSender.CanDeliver"/>, so every case
    /// that is NOT about delivery holds it fixed at the value the real delivering adapters
    /// emit. That the Dev/Test default
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

    private static AuthOptions Options(bool open) => new() { RegistrationsOpen = open };

    public static TheoryData<string, bool> EnvironmentsAndGateStates()
    {
        // Allowlist, not !IsProduction(): Staging and every unrecognised name must be covered, or the
        // guard exempts exactly the environments nobody thought about.
        var rows = new TheoryData<string, bool>();
        foreach (var environmentName in new[] { "Production", "Staging", "SomethingNobodyNamedYet" })
        {
            rows.Add(environmentName, false);
            rows.Add(environmentName, true);
        }

        return rows;
    }

    [Theory]
    [MemberData(nameof(EnvironmentsAndGateStates))]
    public void A_sender_that_cannot_deliver_refuses_to_boot_whatever_the_gate_says(
        string environmentName, bool open)
    {
        // A closed gate is the committed default composition: Email:Provider unset, registration closed.
        var result = ValidatorFor(environmentName, NonDeliveringSender())
            .Validate(null, Options(open));

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
            .Validate(null, Options(open: false))
            .Failed.ShouldBeTrue();

        ValidatorFor("Production", DeliveringSender())
            .Validate(null, Options(open: false))
            .Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_delivering_sender_boots_in_Production_whatever_the_gate_says(bool open)
    {
        // The fail-safe default (closed, i.e. an absent Auth section) must boot clean with a delivering
        // sender — a guard that also broke the safe state would have replaced one outage class with
        // another. Every row's non-delivering half is the theory above.
        ValidatorFor("Production").Validate(null, Options(open)).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void The_stranding_combination_is_exempt_in_Development_and_Test(string environmentName)
    {
        // No composition produces the pair (Development,
        // non-delivering sender): AddEmailSender's Null fallback is gated on !Dev && !Test, the
        // Scaleway arm yields a sender that delivers or throws at registration, and every other value throws
        // (AddEmailSenderGateTests.AddEmailSender_InDevelopmentOrTest_CanDeliver measures it). The
        // pair is therefore declared unreachable, and what this pins is the predicate's ORDER: the
        // allowlist short-circuits BEFORE the delivery check, so swapping the two checks turns this red.
        ValidatorFor(environmentName, NonDeliveringSender())
            .Validate(null, Options(open: true))
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
    /// gate is open so an inline <c>throw</c> in <c>AddEmailSender</c> — that file's own
    /// idiom in the Scaleway arm — would surface as an exception rather than as a failed assertion. A check
    /// that neither registers nor throws would pass.
    /// </para>
    /// </summary>
    public class TheWorkerIsNotSubjectToTheGate
    {
        private static IConfiguration ConfigurationWithTheGateOpen() =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Read at registration time, and absence throws: Postgres by both identity
                    // modules, Redis by AddIdentityAndSessions alone. One dictionary for all three
                    // tests, so the only difference between them is which method is called.
                    ["ConnectionStrings:Postgres"] = "Host=localhost;Database=jobbliggaren;Username=x;Password=y",
                    ["ConnectionStrings:Redis"] = "localhost:6379,user=api-persistent,password=synthetic",
                    [$"ConnectionStrings:{DependencyInjection.VolatileRedisConnectionStringName}"] = "localhost:6381,user=api-volatile,password=synthetic",
                    [$"{AuthOptions.SectionName}:{nameof(AuthOptions.RegistrationsOpen)}"] = "true",
                })
                .Build();

        [Fact]
        public void AddIdentityAndSessions_registers_the_validator_for_AuthOptions()
        {
            // The control the two absences below are measured against: same instrument, same
            // configuration, opposite outcome. Without it they would pass just as happily against a
            // build where nothing anywhere registers the validator.
            var services = new ServiceCollection();

            services.AddIdentityAndSessions(ConfigurationWithTheGateOpen());

            services.ShouldContain(d => d.ServiceType == typeof(IValidateOptions<AuthOptions>));
        }

        [Fact]
        public void AddCoreIdentityForWorker_registers_no_validator_for_AuthOptions()
        {
            var services = new ServiceCollection();

            services.AddCoreIdentityForWorker(ConfigurationWithTheGateOpen());

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

            services.AddEmailSender(ConfigurationWithTheGateOpen(), env);

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

            // The kill-switch is the complete handler's first statement, so a grant that was never issued
            // reaches it: the same probe docs/runbooks/registration-gate.md runs.
            var response = await client.PostAsJsonAsync(
                "/api/v1/auth/challenge/complete",
                new { grantToken = "probe", acceptTerms = true },
                TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(
                HttpStatusCode.ServiceUnavailable, "this host holds the gate closed");
            (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
                .GetProperty("title").GetString().ShouldBe(AuthErrorCodes.RegistrationsClosed);

            var announcement = factory.ClosedHostLogs.SingleOrDefault(l => l.EventId.Id == 4300);
            announcement.ShouldNotBeNull(
                "the gate must announce itself once per process (EventId 4300)");
            announcement.Message.ShouldContain("CLOSED");
        }
    }
}
