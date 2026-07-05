using System.Reflection;
using System.Text.RegularExpressions;
using FluentValidation;
using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Behaviors;
using Shouldly;

namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// Enforces the server-side re-authentication invariant (C5, epik #481): a sensitive
/// account/credential command cannot be built without re-auth. This is the "can't forget" guard the
/// CTO-bind required — a future change-email / change-password / PII-export command that does not
/// implement <see cref="IReauthenticatingRequest"/> fails the build, so it cannot ship a sensitive
/// operation that a hijacked long-lived session could run without the password.
/// </summary>
public class ReauthenticationTripwireTests
{
    private static readonly Assembly ApplicationAssembly =
        typeof(Jobbliggaren.Application.AssemblyMarker).Assembly;

    // A command is re-auth-sensitive if its name matches a PRECISE account/credential pattern.
    // Namespace-agnostic on purpose (dotnet-architect PR2c-1 hardening): a future op cannot dodge the
    // guard by living outside Auth.Commands (e.g. a change-email modelled as a profile update in
    // JobSeekers.Commands). Precise enough to EXCLUDE domain mutations (DeleteResumeCommand,
    // UpdateMyProfileCommand, DeleteApplicationCommand — none carry an Account/Email/Password/
    // Credential/PersonalData token). Covers the known deferred ops by their likely names:
    // #678 change-password, #679 change-email, #680 PII/CV export. Maintained heuristic — a new
    // sensitive op whose name escapes these patterns must be added (or made to implement the marker).
    private static readonly Regex SensitiveOp = new(
        "(Change|Update|Set|Reset)(Email|Password|Credential)"    // credential / email mutation
        + "|(Delete|Purge|Erase|Anonymi)(Account|User|Identity)"  // account erasure
        + "|Export(PersonalData|MyData|AccountData|Gdpr|Pii)",     // personal-data export / portability
        RegexOptions.Compiled);

    [Fact]
    public void Sensitive_auth_commands_must_require_reauthentication()
    {
        var missing = ApplicationAssembly.GetTypes()
            .Where(t => t is { IsInterface: false, IsAbstract: false }
                        && typeof(IAuthenticatedRequest).IsAssignableFrom(t)
                        && SensitiveOp.IsMatch(t.Name)
                        && !typeof(IReauthenticatingRequest).IsAssignableFrom(t))
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        missing.ShouldBeEmpty(
            "Sensitive Auth commands missing IReauthenticatingRequest — server-side re-auth is NOT " +
            "enforced, so a hijacked session could run these without the password: " +
            string.Join(", ", missing));
    }

    [Fact]
    public void Reauthenticating_requests_must_have_a_validator()
    {
        // Each re-auth-gated request must carry a validated Password (NotEmpty) so an empty password
        // is a 400 (ValidationBehavior runs before ReauthenticationBehavior) rather than reaching the
        // re-auth check — empty vs wrong = 400 vs 401. A validator MUST exist; the NotEmpty(Password)
        // rule itself is pinned by the per-command validator unit tests.
        var reauthTypes = ApplicationAssembly.GetTypes()
            .Where(t => t is { IsInterface: false, IsAbstract: false }
                        && typeof(IReauthenticatingRequest).IsAssignableFrom(t))
            .ToList();

        reauthTypes.ShouldNotBeEmpty(
            "Expected at least DeleteAccountCommand to implement IReauthenticatingRequest.");

        var withoutValidator = reauthTypes
            .Where(t => !ApplicationAssembly.GetTypes().Any(v =>
                v is { IsInterface: false, IsAbstract: false }
                && typeof(IValidator<>).MakeGenericType(t).IsAssignableFrom(v)))
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        withoutValidator.ShouldBeEmpty(
            "IReauthenticatingRequest implementations without a FluentValidation validator (Password " +
            "would be unvalidated → empty vs wrong not distinguished): " +
            string.Join(", ", withoutValidator));
    }

    [Fact]
    public void ReauthenticationBehavior_should_reside_in_Application_Common_Behaviors()
    {
        // Placement is load-bearing (pinned alongside MediatorPipelineBehaviors.InOrder): moving it
        // breaks the build. Parity with AuditBehavior's placement test.
        typeof(ReauthenticationBehavior<,>).Namespace
            .ShouldBe("Jobbliggaren.Application.Common.Behaviors");
    }
}
