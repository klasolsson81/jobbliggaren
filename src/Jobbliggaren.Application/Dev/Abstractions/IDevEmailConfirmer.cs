namespace Jobbliggaren.Application.Dev.Abstractions;

/// <summary>
/// DEV-ONLY — REMOVE BEFORE LAUNCH (Klas). Token-free force-confirm of an account's
/// email by address, so the Playwright E2E suite (#796) can obtain a CONFIRMED,
/// login-capable user against a flag-ON backend
/// (<c>Auth:RequireEmailConfirmation=true</c>) without a real out-of-band email
/// round-trip.
///
/// <para>
/// DANGEROUS BY DESIGN: this bypasses the email-confirmation token entirely. It is
/// guarded by TWO independent structural gates, both keyed on
/// <c>IHostEnvironment.IsDevelopment()</c>:
/// </para>
/// <list type="number">
/// <item>the implementation is registered in DI ONLY in Development
/// (<see cref="Jobbliggaren.Infrastructure.DependencyInjection.AddDevOnlyTestingSupport"/>)
/// — in any deployed environment the port is absent from the container, so the
/// command that consumes it cannot resolve (fail-closed);</item>
/// <item>the endpoint that sends that command is mapped ONLY in Development
/// (<c>Program.cs</c> <c>IsDevelopment()</c> gate).</item>
/// </list>
/// It MUST NEVER resolve or be reachable in a deployed environment.
/// </summary>
public interface IDevEmailConfirmer
{
    /// <summary>
    /// Force-confirms the account with <paramref name="email"/> (sets
    /// <c>EmailConfirmed = true</c>, no token). Returns
    /// <see cref="DevEmailConfirmOutcome.Confirmed"/> when a matching account was
    /// confirmed (or was already confirmed — idempotent), or
    /// <see cref="DevEmailConfirmOutcome.NotFound"/> when no account matches.
    /// </summary>
    Task<DevEmailConfirmOutcome> ForceConfirmByEmailAsync(string email, CancellationToken cancellationToken);
}

/// <summary>Outcome of <see cref="IDevEmailConfirmer.ForceConfirmByEmailAsync"/>.</summary>
public enum DevEmailConfirmOutcome
{
    /// <summary>A matching account was confirmed (or already confirmed — idempotent).</summary>
    Confirmed,

    /// <summary>No account matches the given email.</summary>
    NotFound,
}
