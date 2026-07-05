using Ardalis.SmartEnum;

namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// The visual CV template (Fas 4b PR-3, ADR 0096 — design handoff §5.5/§8: three
/// templates to start, curated total capped at 6–12 per kunskapsbank §5.8). The member
/// names are the templates' product names from the handoff (Klar/Accentlinje/Mörk
/// panel), kept as the stable persisted vocabulary; Swedish display labels resolve in
/// the frontend via <c>messages/sv.json</c>, never here (CLAUDE.md §1/§10). Rendering
/// of the choice is deferred to PR-8b — this type only fixes the stored vocabulary.
/// </summary>
/// <remarks>
/// <see cref="Klar"/> is the default (handoff §8 "Default. Maximal läsbarhet" +
/// kunskapsbank §5.8 "default = most ATS-safe single-column"; the prototype's
/// Mörk panel default is demo-only). Extension is additive — a new member is a code
/// change, no schema change (Name-string column).
/// </remarks>
public sealed class CvTemplate : SmartEnum<CvTemplate>
{
    /// <summary>Single-column, name + thin accent line, uppercase underlined headings. ATS-safe. Default.</summary>
    public static readonly CvTemplate Klar = new(nameof(Klar), 1);

    /// <summary>Single-column, coloured bar before headings, coloured tech rows. ATS-safe.</summary>
    public static readonly CvTemplate Accentlinje = new(nameof(Accentlinje), 2);

    /// <summary>Two-column: dark side panel (photo, contact, skills) + light main column. "För människor" — ATS parallel mandatory.</summary>
    public static readonly CvTemplate MorkPanel = new(nameof(MorkPanel), 3);

    private CvTemplate(string name, int value) : base(name, value) { }
}
