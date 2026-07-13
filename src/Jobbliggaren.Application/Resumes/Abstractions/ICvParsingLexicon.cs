namespace Jobbliggaren.Application.Resumes.Abstractions;

/// <summary>
/// The CV-parsing lexicon's FREE-section vocabulary, exposed as an Application port (Fas 4b 8b.4a).
/// Implemented in Infrastructure over the versioned embedded lexicon (CLAUDE.md §2.1 / §5 — the
/// vocabulary is data, never inline C# strings).
///
/// <para><b>The boundary this port exists to hold.</b> The lexicon owns <b>RECOGNITION</b>: which
/// strings denote which section ("projekt", "projektportfölj", "selected projects" all denote
/// <c>projekt</c>). A recommendation asset — the SSYK→branschgrupp section rules — owns
/// <b>RECOMMENDATION</b>: which sections to suggest for an occupation. Recommendation cannot do its
/// job without asking recognition a question: <i>"does this CV already have the section I am about to
/// suggest?"</i> (the rule "a section present in the user's file is ALWAYS shown, never
/// re-suggested"). The only alternative to this port is for the asset to carry its OWN synonym list —
/// a second home for one knowledge piece, where a heading added there would silently fail to suppress
/// a suggestion here. That fork is forbidden; this port is what makes it unnecessary.</para>
///
/// <para><b>FREE sections only — deliberately.</b> The six TYPED sections (Kontakt, Profil,
/// Erfarenhet, Utbildning, Kompetenser, Språk) live in a different id-space (<c>ParsedSectionKind</c>,
/// a Domain enum) and are the always-present baseline: they are never suggested, so a recommendation
/// rule never names one. <see cref="TryResolveFreeSectionId"/> therefore returns <c>null</c> for
/// "Kompetenser" — not because the CV lacks it, but because it is not a free section. The names say
/// "Free" so a caller cannot mistake that null for an absence.</para>
///
/// <para><b>The cross-asset guarantee is <see cref="FreeSectionIds"/>, not a version number.</b> A
/// recommendation asset references ids; the contract test that every id it names exists in this set
/// goes red exactly when an id is removed or renamed — and stays green when a SYNONYM is added, which
/// is how the vocabulary is meant to grow. A version-equality pin (the frames↔verb-mapping precedent)
/// would be the wrong axis here: it would fail loud on synonym growth, which cannot invalidate an
/// id-keyed asset. Right signal, right axis.</para>
/// </summary>
public interface ICvParsingLexicon
{
    /// <summary>
    /// Every canonical free-section id the lexicon knows. A recommendation asset that names an id
    /// outside this set is referring to a section nothing can ever recognise — which is why the
    /// asset's ids are contract-tested against this set rather than trusted.
    /// </summary>
    IReadOnlySet<string> FreeSectionIds { get; }

    /// <summary>
    /// Resolves a heading AS THE USER WROTE IT ("PROJEKT", "Utvalda projekt:", "Selected Projects")
    /// to its canonical free-section id; <c>null</c> when the lexicon does not recognise it as a free
    /// section. Normalisation (case, trailing punctuation, whitespace) is the lexicon's own — the
    /// caller must NOT pre-normalise, or the two normalisations can drift apart.
    ///
    /// <para>A COMPOUND heading resolves to the concept it LEADS with: "Legitimation och intyg" →
    /// <c>legitimation</c>, "Kurser och certifikat" → <c>kurser</c>. Deterministic and explainable —
    /// and never a substring match, which would make "Kurser" match inside "Kurser i franska". A rule
    /// that covers more than one concept names more than one id.</para>
    /// </summary>
    string? TryResolveFreeSectionId(string heading);
}
