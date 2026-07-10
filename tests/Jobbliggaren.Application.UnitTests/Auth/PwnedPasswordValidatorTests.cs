using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Infrastructure.Identity;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Auth;

/// <summary>
/// #616 — pins the PwnedPasswordValidator policy seam: Breached rejects with the stable
/// <c>PwnedPassword</c> code (→ <c>Auth.PwnedPassword</c> through the UserAccountService mapping),
/// NotBreached passes, and — THE fail-open policy pin (CTO-bind FORK 1) — Unavailable passes too:
/// an HIBP outage must never block registration or password change.
/// </summary>
public class PwnedPasswordValidatorTests
{
    [Fact]
    public async Task ValidateAsync_BreachedPassword_FailsWithPwnedPasswordCode()
    {
        var checker = new StubChecker(BreachCheckVerdict.Breached);
        var validator = new PwnedPasswordValidator(checker);

        // The UserManager parameter is unused by the implementation (the checker owns the lookup).
        var result = await validator.ValidateAsync(null!, new ApplicationUser(), "correct horse battery");

        result.Succeeded.ShouldBeFalse();
        var error = result.Errors.ShouldHaveSingleItem();
        error.Code.ShouldBe("PwnedPassword");
        // The rejection must state the fact and the action — never the breach source or count.
        error.Description.ShouldBe(PwnedPasswordValidator.ErrorDescription);
        error.Description.ShouldNotContain("HIBP");
        error.Description.ShouldNotContain("haveibeenpwned", Case.Insensitive);
    }

    [Fact]
    public async Task ValidateAsync_NotBreachedPassword_Succeeds()
    {
        var validator = new PwnedPasswordValidator(new StubChecker(BreachCheckVerdict.NotBreached));

        var result = await validator.ValidateAsync(null!, new ApplicationUser(), "correct horse battery");

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_CheckerUnavailable_Succeeds_FailOpenPolicyPin()
    {
        // CTO-bind FORK 1: Unavailable ≡ pass. Flipping this behavior is a deliberate policy
        // change (selective fail-closed) and must break this pin first.
        var validator = new PwnedPasswordValidator(new StubChecker(BreachCheckVerdict.Unavailable));

        var result = await validator.ValidateAsync(null!, new ApplicationUser(), "correct horse battery");

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task ValidateAsync_EmptyPassword_Succeeds_WithoutCallingChecker()
    {
        // Empty/short input is the built-in RequiredLength validator's concern — no egress spent.
        var checker = new StubChecker(BreachCheckVerdict.Breached);
        var validator = new PwnedPasswordValidator(checker);

        var result = await validator.ValidateAsync(null!, new ApplicationUser(), string.Empty);

        result.Succeeded.ShouldBeTrue();
        checker.Calls.ShouldBe(0);
    }

    private sealed class StubChecker(BreachCheckVerdict verdict) : IBreachedPasswordChecker
    {
        public int Calls { get; private set; }

        public Task<BreachCheckVerdict> CheckAsync(string password, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(verdict);
        }
    }
}
