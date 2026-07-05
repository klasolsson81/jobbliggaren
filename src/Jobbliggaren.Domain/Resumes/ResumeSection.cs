namespace Jobbliggaren.Domain.Resumes;

/// <summary>
/// A dynamic, profession-driven CV section beyond the four standard ones (Erfarenhet /
/// Utbildning / Kompetenser / Språk) — Fas 4b AppCopy superset <c>sektioner</c>,
/// ADR 0093 D1 / LRM ADR 0095 D-B (design handoff §7: "Projekt och arbetsprov",
/// "Legitimation och intyg", "Kurser och certifikat", "Referenser", …).
/// <paramref name="Heading"/> is <b>free user text</b>, never a SmartEnum: the file may
/// introduce an arbitrary section and it is always shown (design handoff P4 — "the file
/// wins"); the heading is content to preserve verbatim, not a control-flow discriminator.
/// (The profession-driven <em>suggestion</em> vocabulary + ordering, §7's table, is a
/// separate versioned knowledge-bank asset in a later PR — ADR 0093 D3 — not this stored
/// heading.) Both the heading and each entry are CV-PII free text scanned by
/// <c>ResumeContentPersonnummerGuard</c>.
/// </summary>
public sealed record ResumeSection(string Heading, IReadOnlyList<SectionEntry> Entries);
