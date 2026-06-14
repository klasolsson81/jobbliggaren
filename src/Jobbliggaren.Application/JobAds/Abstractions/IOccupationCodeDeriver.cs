namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Fas 4 STEG 3 (F4-3, ADR 0040 amendment + ADR 0074) — deterministic SSYK
/// level-4 derivation from a free-text occupational title. NO AI/LLM (ADR 0071,
/// CLAUDE.md §5): a CV/free-text title is matched against the JobTech taxonomy's
/// occupation-name labels (reusing <see cref="ITaxonomyReadModel"/>, ADR 0043) and
/// resolved up to the parent ssyk-level-4 yrkesgrupp via the committed
/// occupation-name→ssyk-4 map (ADR 0067 C2). The engine <b>PROPOSES</b> a ranked
/// candidate list; the user <b>CONFIRMS</b> — it never auto-selects and never
/// persists (ADR 0040 Beslut 4, the load-bearing transparency invariant). A title
/// with no taxonomy match yields an empty list → the UX falls to manual SSYK
/// selection (the same confirm surface).
/// <para>
/// <b>PII boundary (ADR 0074 Invariant 3):</b> the port takes a plain title string
/// and reads NO CV PII — the field-encryption-key pipeline + personnummer-guard
/// obligations live at the F4-8 call-site that obtains the title from CV content.
/// The deriver never logs the title (CLAUDE.md §5 / BUILD §13).
/// </para>
/// </summary>
public interface IOccupationCodeDeriver
{
    /// <summary>
    /// Derives the proposed ssyk-level-4 occupation-group candidate(s) for
    /// <paramref name="title"/>, deduplicated per group and ordered
    /// deterministically (exact occupation-name hits before stemmed-overlap
    /// candidates, then group-label Ordinal). Empty
    /// <see cref="OccupationDerivationResult.Candidates"/> ⇒ no match ⇒ manual
    /// selection. Never throws on "no match"; never auto-selects; persists nothing.
    /// </summary>
    ValueTask<OccupationDerivationResult> DeriveAsync(
        string title, CancellationToken cancellationToken);
}
