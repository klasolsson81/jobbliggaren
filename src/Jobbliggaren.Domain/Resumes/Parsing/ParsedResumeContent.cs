namespace Jobbliggaren.Domain.Resumes.Parsing;

/// <summary>
/// Structured content of a parsed CV (F4-8). Net-new value object — NOT the canonical
/// <c>ResumeContent</c> (which has no språk section and whose strict validation is
/// hostile to a degraded parse, CTO Decision 1). Every collection is honest about
/// what the deterministic parser found; nothing is synthesised (CLAUDE.md §5).
/// This is CV-PII: persisted only via the field-encryption pipeline as a
/// JSON-serialised shadow (ADR 0074 Invariant 3, Form B).
/// </summary>
/// <remarks>
/// Equality on the collection properties is reference-based (record-generated
/// <c>Equals</c> compares list references, not elements) — acceptable because the
/// content is replaced wholesale, never mutated field-by-field (parity with
/// <c>ResumeContent</c>).
/// </remarks>
public sealed record ParsedResumeContent
{
    public ParsedContact Contact { get; init; }

    /// <summary>Profil / sammanfattning — the free-text summary, if found.</summary>
    public string? Profile { get; init; }

    public IReadOnlyList<ParsedExperience> Experience { get; init; }

    public IReadOnlyList<ParsedEducation> Education { get; init; }

    public IReadOnlyList<string> Skills { get; init; }

    public IReadOnlyList<string> Languages { get; init; }

    /// <summary>
    /// Sections the CV has that are not one of the six typed kinds — "Projekt", "Referenser",
    /// "Certifieringar" (#815). Ordered as they appear in the document, never merged: two free
    /// sections keep their own verbatim headings. Empty when the CV has none, or when the
    /// artifact predates this field (see the constructor).
    /// </summary>
    public IReadOnlyList<ParsedSection> Sections { get; init; }

    /// <summary>
    /// Text the CV carried ABOVE its first heading that no contact extractor claimed — verbatim and
    /// UNCLASSIFIED (#844).
    ///
    /// <para><b>The engine does not claim this is a profile.</b> A heading-less run may be a summary
    /// the user forgot to head, a tagline, an address block or OCR noise, and shape cannot tell them
    /// apart. Assigning it to <see cref="Profile"/> would be the engine inventing a section the user
    /// did not write — ADR 0071's one absolute prohibition. It is carried so that (a) the user can
    /// decide what it is (ADR 0074 propose-and-approve) and (b) no rule may report it ABSENT: before
    /// this field existed, a CV opening with an un-headed summary had that prose dropped from the
    /// artifact entirely, and A8 then told its author, as a hard Fail, that "Profiltext saknas
    /// helt."</para>
    ///
    /// <para><c>null</c> when the preamble was fully accounted for by name / e-mail / phone /
    /// location extraction — the common case, and the one that keeps A8's honest Fail alive for a CV
    /// that genuinely has no summary.</para>
    /// </summary>
    public string? Preamble { get; init; }

    /// <param name="sections">
    /// Additive trailing parameter — the expand half of expand/contract (ADR 0095 D-D). The parse
    /// artifact is persisted as an encrypted JSON shadow (Form B), so a row written before this
    /// field existed simply has no "sections" key; System.Text.Json binds the constructor, the
    /// parameter takes its default, and the property lands as an empty list. No migration, no DDL,
    /// no backfill — and no guessing about what those older parses contained.
    /// </param>
    /// <param name="preamble">
    /// Additive trailing parameter, the same expand/contract half <paramref name="sections"/> landed
    /// as (#844, ADR 0095 D-D). A row written before this field existed has no "preamble" key;
    /// System.Text.Json binds the constructor, the parameter takes its default, and the property
    /// lands as <c>null</c>. No migration, no DDL, no backfill — and no guessing about what those
    /// older parses carried above their first heading.
    /// </param>
    public ParsedResumeContent(
        ParsedContact contact,
        string? profile = null,
        IReadOnlyList<ParsedExperience>? experience = null,
        IReadOnlyList<ParsedEducation>? education = null,
        IReadOnlyList<string>? skills = null,
        IReadOnlyList<string>? languages = null,
        IReadOnlyList<ParsedSection>? sections = null,
        string? preamble = null)
    {
        Contact = contact;
        Profile = profile;
        Experience = experience ?? [];
        Education = education ?? [];
        Skills = skills ?? [];
        Languages = languages ?? [];
        Sections = sections ?? [];
        Preamble = preamble;
    }

    /// <summary>An empty parse — used when extraction failed and there is nothing to
    /// structure (the artifact still persists with a Failed confidence, OQ5).</summary>
    public static ParsedResumeContent Empty { get; } = new(ParsedContact.Empty);
}
