using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Abstractions;

namespace Jobbliggaren.Infrastructure.KnowledgeBank;

/// <summary>
/// <see cref="IBranschgruppProvider"/> over the committed, versioned branschgrupp asset
/// (Fas 4b 8b.4a). Loads + maps + validates <c>ssyk-branschgrupp.v1.json</c> once at construction
/// (fail loud at startup, never mid-request) and serves the cached immutable contract — singleton,
/// parity <see cref="RubricProvider"/>.
/// <para>
/// <b>Cross-asset pin (the no-fork mechanism).</b> Ctor-injects <see cref="ICvParsingLexicon"/> —
/// the owner of section IDENTITY — and refuses to start if this asset names a section the lexicon
/// does not own, exactly as <c>FrameProvider(IVerbMapper)</c> refuses to start on a frames↔verb
/// drift. Two things are checked, and the second is the one that matters:
/// </para>
/// <list type="number">
///   <item><b>The sectionId exists.</b> Otherwise the asset has forked the vocabulary — invented
///   a section the parser has never heard of.</item>
///   <item><b>The HEADING round-trips.</b> <c>TryResolveFreeSectionId(heading)</c> must return the
///   very sectionId the asset filed it under. The heading is written INTO the user's CV when she
///   accepts the suggestion; if the segmenter cannot resolve it on the next import, that section's
///   body is swallowed by the preceding section. That is not hypothetical — it is the live #815 bug
///   PR-1 fixed for <c>legitimation</c>/<c>korkort</c>. Suggesting a heading the parser cannot see
///   would be shipping that bug back on purpose, so the suggestion set is constrained to be a
///   SUBSET of what the lexicon can recognise.</item>
/// </list>
/// </summary>
internal sealed class BranschgruppProvider : IBranschgruppProvider
{
    private readonly BranschgruppCatalog _catalog;

    public BranschgruppProvider(ICvParsingLexicon lexicon)
    {
        ArgumentNullException.ThrowIfNull(lexicon);

        _catalog = BranschgruppLoader.Load();
        ValidateAgainstLexicon(_catalog, lexicon);
    }

    /// <summary>
    /// The cross-asset pin, as an internal static seam so the tests can drive a SYNTHETIC drifted
    /// catalog through the REAL check (the shipped asset is, by construction, the one case that
    /// passes — a test that could only ever see the good asset would prove nothing).
    /// </summary>
    internal static void ValidateAgainstLexicon(BranschgruppCatalog catalog, ICvParsingLexicon lexicon)
    {
        foreach (var rules in catalog.RulesById.Values)
        {
            foreach (var section in rules.StandardSections.Concat(rules.SuggestedSections))
            {
                if (!lexicon.FreeSectionIds.Contains(section.SectionId))
                {
                    throw new InvalidOperationException(
                        $"ssyk-branschgrupp-assetet föreslår sektionen '{section.SectionId}' " +
                        $"(branschgrupp '{rules.Id}') som parsning-lexikonet inte äger. " +
                        "Assetet REKOMMENDERAR, lexikonet KÄNNER IGEN — förslagsmängden måste vara " +
                        "en delmängd av det lexikonet kan känna igen. Lägg till sektionen i " +
                        "cv-parsing-lexicon.v1.json först, eller ta bort den härifrån.");
                }

                var resolved = lexicon.TryResolveFreeSectionId(section.Heading);
                if (!string.Equals(resolved, section.SectionId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Rubriken '{section.Heading}' (branschgrupp '{rules.Id}') resolvar till " +
                        $"'{resolved ?? "ingenting"}', inte till '{section.SectionId}'. Rubriken skrivs " +
                        "in i användarens CV — en rubrik segmenteraren inte känner igen får sin text " +
                        "uppslukad av föregående sektion vid nästa import (#815).");
                }
            }
        }
    }

    public BranschgruppCatalog GetCatalog() => _catalog;
}
