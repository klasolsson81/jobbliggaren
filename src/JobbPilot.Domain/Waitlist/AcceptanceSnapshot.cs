namespace JobbPilot.Domain.Waitlist;

/// <summary>
/// Audit-bevis för vad användaren faktiskt accepterade vid waitlist-signup.
/// Bär tre värden:
/// <list type="bullet">
///   <item><see cref="MarketingEmailAccepted"/> — genuin GDPR Art. 7-samtycke
///   (Art. 6(1)(a) consent) för icke-transaktionell e-post. Valfritt opt-in.</item>
///   <item><see cref="AcceptedAt"/> — timestamp för när användaren skickade
///   in anmälan. Användarvillkor + nödvändiga cookies levereras under
///   Art. 6(1)(b) "performance of contract" — submit-knappen = acceptance,
///   ingen separat consent-checkbox behövs.</item>
///   <item><see cref="PrivacyPolicyVersion"/> — vilken version av integritets-
///   policyn som var aktiv vid acceptance-tidpunkten.</item>
/// </list>
///
/// <para>
/// CTO-dom 2026-05-24 (Fynd 1 Approach B) baserat på EDPB Guidelines 03/2022
/// "Deceptive design patterns": pre-ifyllda checkboxes för obligatoriska
/// villkor är dark pattern. Korrekt legal-framing är Art. 6(1)(b) för
/// nödvändiga villkor, Art. 6(1)(a) endast för genuint valfri marketing.
/// </para>
///
/// <para>
/// Mappas via EF Core OwnsOne till separata kolumner på
/// <c>waitlist_entries</c> (queryable för audit-export).
/// </para>
/// </summary>
public sealed record AcceptanceSnapshot(
    bool MarketingEmailAccepted,
    DateTimeOffset AcceptedAt,
    string PrivacyPolicyVersion);
