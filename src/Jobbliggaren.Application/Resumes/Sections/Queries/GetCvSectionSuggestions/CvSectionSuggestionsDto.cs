namespace Jobbliggaren.Application.Resumes.Sections.Queries.GetCvSectionSuggestions;

/// <summary>
/// The occupation-driven section suggestions for one parsed CV (Fas 4b 8b.4a, ADR 0107).
/// </summary>
/// <param name="Branschgrupp">The resolved branschgrupp slug (<c>it</c>/<c>vard</c>/<c>skola</c>/
/// <c>ovriga</c>). The FE keys its i18n on this stable id — the payload carries no UI label
/// (parity <c>CvTemplateCatalogDto</c>).</param>
/// <param name="HasOccupationPreference">
/// Whether the user has stated ANY occupation in her match preferences.
/// <para>
/// <b>This flag is why there are two Övriga states, and they must never be merged</b> (CTO bind,
/// 2026-07-13). <c>false</c> → she has not told us her occupation: the guide shows the generic row
/// AND asks her for one (handoff rule (d)). <c>true</c> with <see cref="Branschgrupp"/> =
/// <c>ovriga</c> → she HAS told us, and her occupation is one of the 17 fields with no specialised
/// rule-table (the 62.1 % majority): she gets the same generic suggestions but is NOT asked again.
/// Asking a user for something she already gave you is the product telling her it wasn't listening.
/// </para>
/// </param>
/// <param name="Rationale">The badge copy for the resolved branschgrupp ("Vanligt inom vård och
/// omsorg"). KB-sourced Swedish — never prose the engine synthesised (§5).</param>
/// <param name="Suggestions">Sections to offer, in asset order (standard first). Sections the CV
/// ALREADY has are excluded, and so are the ones this branschgrupp suppresses. May be empty — an
/// honest "nothing to add", never a fabricated row.</param>
public sealed record CvSectionSuggestionsDto(
    string Branschgrupp,
    bool HasOccupationPreference,
    string Rationale,
    IReadOnlyList<SectionSuggestionDto> Suggestions);

/// <summary>
/// One offered section.
/// </summary>
/// <param name="SectionId">The lexicon's canonical section id — the identity the FE dedupes on.</param>
/// <param name="Heading">The Swedish heading written INTO the CV when the user accepts. Comes from
/// the asset (document content, not UI chrome) and is guaranteed to round-trip through the parsing
/// lexicon back to <paramref name="SectionId"/> — the provider refuses to start otherwise.</param>
/// <param name="IsStandard">True = the handoff's "extra standardsektion" (a section this occupation
/// is EXPECTED to have); false = merely common. The guide surfaces the two differently.</param>
public sealed record SectionSuggestionDto(string SectionId, string Heading, bool IsStandard);
