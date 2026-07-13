namespace Jobbliggaren.Application.Resumes.Abstractions;

/// <summary>
/// The CV-parsing lexicon's RECOGNITION vocabulary, exposed as an Application port (Fas 4b 8b.4a).
/// Implemented in Infrastructure over the versioned embedded lexicon (CLAUDE.md §2.1 / §5 — the
/// vocabulary is data, never inline C# strings).
///
/// <para><b>The boundary this port exists to hold.</b> The lexicon owns <b>RECOGNITION</b>: which
/// strings denote which section ("projekt", "projektportfölj", "selected projects" all denote
/// <c>projekt</c>). A recommendation asset — the SSYK→branschgrupp section rules — owns
/// <b>RECOMMENDATION</b>: which sections to suggest for an occupation. Recommendation cannot do its
/// job without asking recognition a question: <i>"does this CV already have the section I am about
/// to suggest?"</i> (the rule "a section present in the user's file is ALWAYS shown, never
/// re-suggested"). The only alternative to this port is for the asset to carry its OWN synonym list
/// — a second home for one knowledge piece, where a heading added here would silently fail to
/// suppress a suggestion there. That fork is forbidden; this port is what makes it unnecessary.</para>
///
/// <para>Consumers reference a <c>sectionId</c> ONLY, never a synonym. The ids are the lexicon's
/// canonical free-section identities; <see cref="Version"/> lets a consumer asset pin the lexicon
/// version it was authored against and fail loud on drift, rather than degrade silently.</para>
/// </summary>
public interface ICvParsingLexicon
{
    /// <summary>The lexicon's data version — the value a consumer asset pins.</summary>
    int Version { get; }

    /// <summary>
    /// Every canonical free-section id the lexicon knows. A recommendation asset that names an id
    /// outside this set is referring to a section nothing can ever recognise — which is why the
    /// asset's ids are contract-tested against this set rather than trusted.
    /// </summary>
    IReadOnlyCollection<string> SectionIds { get; }

    /// <summary>
    /// Resolves a heading AS THE USER WROTE IT ("PROJEKT", "Utvalda projekt:", "Selected Projects")
    /// to its canonical free-section id, or <c>null</c> when the lexicon does not recognise it.
    /// Normalisation (case, trailing punctuation, whitespace) is the lexicon's own — the caller must
    /// not pre-normalise, or the two normalisations can drift apart.
    /// </summary>
    string? TryResolveSectionId(string heading);
}
