using JobbPilot.Application.Waitlist.Dtos;
using JobbPilot.Domain.Common;
using Mediator;

namespace JobbPilot.Application.Waitlist.Commands.RequestWaitlistEntry;

/// <summary>
/// Anonym besökare skriver upp sig på väntelistan via /vantelista.
/// Ingen autentisering krävs. Rate-limit hanteras av API-lagret
/// (WaitlistSignupPolicy 3/24h per IP). Kill-switch via
/// <see cref="JobbPilot.Application.Common.Abstractions.IFeatureFlags.RegistrationsOpen"/>.
///
/// <para>
/// Användarvillkor + nödvändiga cookies levereras under Art. 6(1)(b)
/// "performance of contract" — submit-knappen = acceptance, ingen separat
/// consent-checkbox. Endast <see cref="MarketingEmailAccepted"/> är genuint
/// GDPR Art. 7-samtycke (Art. 6(1)(a) consent) som användaren explicit
/// opt-in-ar för. PrivacyPolicyVersion stämplas server-side via
/// <see cref="JobbPilot.Application.Waitlist.PrivacyPolicyOptions"/>.
/// CTO-dom 2026-05-24 Fynd 1 Approach B.
/// </para>
/// </summary>
public sealed record RequestWaitlistEntryCommand(
    string? Email,
    string? Name,
    string? Motivation,
    bool MarketingEmailAccepted)
    : ICommand<Result<WaitlistEntryRequestedDto>>;
