using Jobbliggaren.Application.Dev.Abstractions;
using Mediator;

namespace Jobbliggaren.Application.Dev.Commands.ConfirmEmail;

/// <summary>
/// DEV-ONLY throwaway tool — REMOVE BEFORE LAUNCH (Klas). Force-confirms a test
/// account's email so the Playwright E2E suite (#796) can log in against a flag-ON
/// backend (<c>Auth:RequireEmailConfirmation=true</c>) without a real email
/// round-trip. Deliberately UNAUTHENTICATED (no <c>IAuthenticatedRequest</c>) — the
/// caller has just registered and is login-gated, so there is no session yet.
///
/// <para>
/// Routed through Mediator (parity with <c>ResetMyDataCommand</c>) so the handler is
/// unit-testable with a faked <see cref="IDevEmailConfirmer"/>. The port is DI-
/// registered ONLY in Development, so sending this command in any deployed
/// environment fails to resolve the handler dependency (fail-closed) — in addition
/// to the endpoint being unmapped outside Development.
/// </para>
/// </summary>
public sealed record ConfirmEmailDevCommand(string Email) : ICommand<DevEmailConfirmOutcome>;
