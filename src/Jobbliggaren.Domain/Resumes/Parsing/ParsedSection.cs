namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// A section of a parsed CV that is NOT one of the six typed kinds — "Projekt", "Referenser",
/// "Certifieringar", "Kurser" and whatever else the user chose to write (#815).
///
/// Before this existed, such a section terminated nothing: a block ran until the next RECOGNISED
/// heading, so "PROFIL … PROJEKT …" swallowed the entire project list into the summary. The user
/// saw their profile and their projects fused into one run-on blob.
///
/// <para><b>The heading is CONTENT, not a discriminator.</b> It is the user's own line, preserved
/// verbatim (casing and all) — mirroring the canonical <see cref="ResumeSection"/>, whose contract
/// says the same thing (ADR 0093 D1). This is why free sections are an ordered LIST and not a
/// <c>ParsedSectionKind.Other</c> enum member: keying blocks by kind would collide PROJEKT with
/// REFERENSER into one concatenated block — recreating the very spaghetti this fixes, one layer
/// down — and would discard the heading the user wrote, keeping only an enum token. The engine
/// never throws away what the user wrote.</para>
/// </summary>
public sealed record ParsedSection(string Heading, IReadOnlyList<ParsedSectionEntry> Entries);
