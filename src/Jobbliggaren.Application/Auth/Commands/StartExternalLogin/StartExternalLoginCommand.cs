using Jobbliggaren.Application.Auth.ExternalLogins;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.StartExternalLogin;

/// <summary>
/// #1744 — start a login at a provider (ADR 0142 D8). <see cref="Next"/> is the post-login path the web has already
/// made safe; the flow carries it to the callback as an echo.
/// </summary>
public sealed record StartExternalLoginCommand(string? Provider, string? Next) : ICommand<Result<ExternalLoginStart>>;

/// <summary>Where to send the browser, and the state the web binds to it in its Lax cookie.</summary>
public sealed record ExternalLoginStart(Uri AuthorizeUrl, OAuthState State);
