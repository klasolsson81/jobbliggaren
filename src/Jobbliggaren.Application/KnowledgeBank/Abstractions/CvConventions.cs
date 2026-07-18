using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Application.KnowledgeBank.Abstractions;

/// <summary>
/// The committed, versioned Swedish-ATS CV conventions (Fas 4b 8b.4b, Asset B —
/// <c>cv-conventions.v1.json</c>; ADR 0108). Currently one convention: the recommended
/// SECTION ORDER, which is the rubric's B1 <c>atsPassSignal</c> chain made machine-readable.
/// <para>
/// <b>What this asset is deliberately NOT.</b> ADR 0098 §D3 planned it as a five-field
/// formatting grab-bag (headings, dates, order, fonts, contact labels). ADR 0108 corrected that
/// on the verified consumer graph: those five have five different consumers in four different
/// pipeline stages, so RECOGNITION stays in <c>cv-parsing-lexicon</c> (ADR 0107 §3), assessment
/// THRESHOLDS stay in the rubric, and regex/sort FORM stays in C#. This asset owns
/// RECOMMENDATION only.
/// </para>
/// </summary>
/// <param name="Version">The asset's own version (e.g. "1.0.0"). Validated fail-loud at load and
/// carried for provenance; NOT yet stamped on <c>CvReviewResult</c>/<c>CvImprovementResult</c> the
/// way <c>Rubric.Version</c> is. Said plainly rather than dressed up as "surfaced": a result cannot
/// currently be traced back to the convention that produced it, and pretending otherwise in a doc
/// comment is the drift this step spent its length removing.</param>
/// <param name="SectionOrder">The recommended section order, most-important first. Sections the
/// order does not name (free sections — "Projekt", "Referenser") follow the named ones, keeping
/// their observed relative order; that is the sort's stability property, not data.</param>
/// <param name="FontAllowlist">The rubric D3 <c>atsPassSignal</c> font list made machine-readable
/// (Fas 4b #891, ADR 0108) — a RECOMMENDATION, parallel to <see cref="SectionOrder"/>. An EXEMPLAR
/// set, not exhaustive: Garamond/Lato parse fine yet are absent, so <c>D3StandardFontRule</c> WARNS
/// on a non-member body font, never FAILS. The reader normalises both these entries and each
/// observed font name through one shared normaliser (subset-tag + style-suffix stripping is parser
/// FORM in C#, not data). The pt band is a THRESHOLD and lives in the rubric, NOT here (ADR 0108's
/// kind-boundary: RECOMMENDATION → this asset; THRESHOLDS → rubric).</param>
public sealed record CvConventions(
    string Version,
    IReadOnlyList<CvSectionOrderEntry> SectionOrder,
    IReadOnlyList<string> FontAllowlist);

/// <summary>
/// One position in the recommended section order. <see cref="SectionId"/> is a section identity
/// the lexicon or <see cref="ParsedSectionKind"/> already owns — this asset mints none of its own
/// (the cross-asset pin in the provider refuses to construct otherwise).
/// </summary>
/// <param name="SectionId">The canonical section id ("experience", "projekt").</param>
/// <param name="TypedKind">The typed section this id denotes, or <c>null</c> when it denotes a
/// FREE section (whose identity the parsing lexicon owns). The two id-spaces are distinct and the
/// lexicon's own port says so: <c>TryResolveFreeSectionId("Kompetenser")</c> returns null because
/// Kompetenser is typed, not absent.</param>
public sealed record CvSectionOrderEntry(string SectionId, ParsedSectionKind? TypedKind);
