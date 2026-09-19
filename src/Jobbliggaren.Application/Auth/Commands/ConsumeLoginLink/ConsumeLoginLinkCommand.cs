using Jobbliggaren.Application.Auth.LoginChallenges;
using Jobbliggaren.Domain.Common;
using Mediator;

namespace Jobbliggaren.Application.Auth.Commands.ConsumeLoginLink;

/// <summary>#1735 — present the token a login challenge's link carried (ADR 0142 D3, "Page form").</summary>
public sealed record ConsumeLoginLinkCommand(string? Token) : ICommand<Result<LoginOutcome>>;
