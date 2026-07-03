namespace Jobbliggaren.Application.Applications.Queries.GetEmployerApplicationCountBatch;

/// <summary>
/// #446 — read-projection for the /jobb per-employer application-count overlay. Maps a JobAdId to the
/// number of the caller's OWN submitted applications to that ad's employer.
/// <para>
/// <b>Positive-only</b> (parity <c>JobAdMatchBatchDto</c>): only ads with a count &gt; 0 appear in
/// <see cref="CountsByJobAdId"/>. An absent JobAdId means zero prior applications — the FE renders no
/// badge, so it needs no "zero" branch. The dictionary is keyed by the raw JobAdId <see cref="Guid"/>
/// (non-PII); the value is a plain <see cref="int"/> count. NO org.nr crosses this boundary — it is a
/// server-side-only GROUP key (ADR 0087 D8 / CLAUDE.md §5).
/// </para>
/// </summary>
public sealed record EmployerApplicationCountBatchDto(
    IReadOnlyDictionary<Guid, int> CountsByJobAdId);
