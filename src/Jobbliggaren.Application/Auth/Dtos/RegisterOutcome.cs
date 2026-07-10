namespace Jobbliggaren.Application.Auth.Dtos;

/// <summary>
/// Outcome of a registration attempt (#714). <see cref="Session"/> is non-null ONLY on the legacy
/// instant-login path (<c>Auth:RequireEmailConfirmation</c> = false) — the Api renders it as
/// <c>200 + { sessionId }</c>. When null, registration is email-confirmation-first: no session was
/// minted (a confirmation link, or an out-of-band account-exists notice for a taken address, was
/// emailed instead), and the Api renders an identical <c>202 Accepted</c> for BOTH a fresh and a taken
/// address — closing the 200-vs-400 account-enumeration status oracle. A taken address is
/// indistinguishable from a fresh one here: both yield <c>Session = null</c>.
/// </summary>
public sealed record RegisterOutcome(SessionDto? Session);
