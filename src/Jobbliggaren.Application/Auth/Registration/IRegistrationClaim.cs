namespace Jobbliggaren.Application.Auth.Registration;

/// <summary>
/// The per-address claim <c>complete</c> takes before it creates an account (ADR 0142 D1): one atomic
/// set-if-absent, so two completions for one address cannot both reach the create. It is not where
/// uniqueness lives — that is Identity's unique user name — so the duplicate error is handled even by the
/// caller that won.
/// </summary>
public interface IRegistrationClaim
{
    /// <summary>
    /// True when this caller took the claim. The claim is never released: a caller that fell over after
    /// winning cannot know whether its Identity write committed, so nothing may retry inside the window. It
    /// goes with its lifetime (<c>LoginChallengePolicy.RegistrationClaimTtl</c>) and nothing else.
    /// </summary>
    Task<bool> TryClaimAsync(string email, CancellationToken ct);
}
