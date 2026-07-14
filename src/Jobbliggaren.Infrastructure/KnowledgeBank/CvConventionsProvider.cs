using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Abstractions;

namespace Jobbliggaren.Infrastructure.KnowledgeBank;

/// <summary>
/// <see cref="ICvConventionsProvider"/> over the committed, versioned conventions asset (Fas 4b
/// 8b.4b). Loads + maps + validates <c>cv-conventions.v1.json</c> once at construction and serves
/// the cached immutable contract — singleton, parity <see cref="BranschgruppProvider"/>.
/// <para>
/// <b>Fail loud at startup, never mid-request — and that is true only because
/// <c>AddCvLexicon()</c> registers this by INSTANCE, not by type.</b> A type registration would
/// construct it at the first resolve, i.e. inside the first HTTP request that needs it, so a
/// malformed asset would surface as a 500 cached for the life of the process (and
/// <c>ValidateOnBuild</c> does not instantiate singletons, so it would not catch it either). If
/// this registration is ever changed back to <c>AddSingleton&lt;IPort, Impl&gt;()</c>, this
/// sentence becomes a lie and the guarantee is gone. That is not hypothetical: it was 8b.4a's
/// architect-Major, found one commit ago.
/// </para>
/// <para>
/// <b>Cross-asset pin (the no-fork mechanism).</b> Ctor-injects <see cref="ICvParsingLexicon"/> —
/// the owner of FREE-section identity — and refuses to start if the order names a section that is
/// neither one of the six TYPED kinds nor a free section the lexicon owns. An unresolvable id in
/// the order is not a harmless typo: the transform resolves observed headings to ids, so an id
/// nothing can ever resolve to would sit in the recommended order matching NOTHING, and the
/// reorder would silently sort against a phantom. The asset RECOMMENDS; the lexicon RECOGNISES
/// (ADR 0107 §3).
/// </para>
/// </summary>
internal sealed class CvConventionsProvider : ICvConventionsProvider
{
    private readonly CvConventions _conventions;

    public CvConventionsProvider(ICvParsingLexicon lexicon)
    {
        ArgumentNullException.ThrowIfNull(lexicon);

        _conventions = CvConventionsLoader.Load();
        ValidateAgainstLexicon(_conventions, lexicon);
    }

    /// <summary>
    /// The cross-asset pin, as an internal static seam so the tests can drive a SYNTHETIC drifted
    /// asset through the REAL check (the shipped asset is, by construction, the one case that
    /// passes — a test that could only ever see the good asset would prove nothing). Parity
    /// <see cref="BranschgruppProvider.ValidateAgainstLexicon"/>.
    /// </summary>
    internal static void ValidateAgainstLexicon(CvConventions conventions, ICvParsingLexicon lexicon)
    {
        foreach (var entry in conventions.SectionOrder)
        {
            // A TYPED id was already resolved by the loader against ParsedSectionKind — the lexicon
            // has nothing to add (its free-section port returns null for "skills" BY DESIGN, because
            // Kompetenser is typed, not absent; asking it here would reject every typed section).
            if (entry.TypedKind is not null)
            {
                continue;
            }

            if (!lexicon.FreeSectionIds.Contains(entry.SectionId))
            {
                throw new InvalidOperationException(
                    $"cv-conventions-assetet ordnar sektionen '{entry.SectionId}' som varken är en av "
                    + "de sex typade sektionerna eller en fri sektion parsning-lexikonet äger. "
                    + "Assetet REKOMMENDERAR, lexikonet KÄNNER IGEN — en ordning kan bara nämna "
                    + "sektioner som går att känna igen i ett CV, annars sorterar transformen mot ett "
                    + "spöke. Lägg till sektionen i cv-parsing-lexicon.v1.json först, eller ta bort "
                    + "den härifrån.");
            }
        }
    }

    public CvConventions GetConventions() => _conventions;
}
