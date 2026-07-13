using Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;

namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Application port for the fail-safe matching an Art. 17 erasure request needs (ADR 0106 D8,
/// #842). The implementation lives in Infrastructure because PostgreSQL full-text search and the
/// <c>jsonb::text</c> cast are Npgsql concerns, which the architecture test forbids in Application
/// (CLAUDE.md §2.1). Same reason, same shape, as <see cref="IJobAdSearchQuery"/>.
/// </summary>
/// <remarks>
/// <b>THREE channels, and each exists because the other two miss something.</b>
/// <list type="number">
/// <item><b>FTS</b> — the exposure we are closing: <c>search_vector @@ websearch_to_tsquery(…)</c>
/// is a hit for any logged-in user today, and her NAME hits the same way via word lexemes.</item>
/// <item><b>Substring over <c>title</c>/<c>description</c>/<c>raw_payload</c></b> — it finds the
/// identifier <i>as supplied</i>, including forms Postgres's parser will not lexeme the same way,
/// and it reaches <c>employer.name</c> inside <c>raw_payload</c>.
/// <para>
/// ⚠ <b>It does NOT de-obfuscate, and an earlier version of this comment claimed it did.</b>
/// Matching <c>anna@acme.se</c> against an ad that reads <c>anna(at)acme.se</c> misses on BOTH
/// channels — the substring channel compares the string it was given, and de-obfuscation is not a
/// thing it does. What actually serves the obfuscated tail is the <b>NAME</b>: the recruiter asks
/// us to erase <i>Anna Karlsson</i>, and her name is in the body in plain words, where both channels
/// reach it. (Or the operator supplies the obfuscated form himself.) Crediting the wrong mechanism
/// for a control's coverage is the same defect class this PR withdrew from
/// <c>PlatsbankenJobSource</c>. Written down rather than quietly corrected.
/// </para>
/// </item>
/// <item><b>Substring over <c>company_name</c></b> — and this one was MISSING, in a way that would
/// have produced a false <c>NoMatchingDataHeld</c> across most of the corpus. An <i>enskild
/// firma</i>'s company name IS a natural person's name. It is not in <c>search_vector</c> (which is
/// built from title and description only), so channel 1 cannot see it. Channel 2 reached it only
/// through <c>employer.name</c> inside <c>raw_payload</c> — but <c>PurgeStaleRawPayloadsJob</c>
/// NULLs <c>raw_payload</c> 30 days after publication. So for every ad older than 30 days — i.e.
/// most of 93 469 ads collected over months — she would have been told <i>"we hold no data matching
/// this identifier"</i> while her name sat in plaintext in a column we scan on every erasure. The
/// defect this whole issue is about, reproduced inside its own fix, and it survived because the
/// end-to-end test happened to run against a fresh <c>raw_payload</c>.</item>
/// </list>
/// <para>
/// The union <b>over-matches, deliberately</b>. A false positive costs the operator a second look at
/// the mandatory dry run; a false negative costs a false Art. 12(3) confirmation to a named person.
/// </para>
/// </remarks>
public interface IRecruiterErasureMatchQuery
{
    /// <summary>
    /// Ads matching <paramref name="identifier"/> on ANY channel, in any status <b>except</b>
    /// already-<c>Erased</c> (an erased ad holds nothing to match, and counting it again would
    /// inflate the number we report to the data subject). Returns the ads themselves, because the
    /// operator has to review them — a count cannot be reviewed.
    /// </summary>
    Task<IReadOnlyList<ErasureJobAdMatch>> FindJobAdsAsync(
        string identifier, CancellationToken cancellationToken);

    /// <summary>
    /// How many saved searches physically hold <paramref name="identifier"/> in their stored
    /// criteria. <b>Counted and REPORTED — a human erases them.</b> Her right DOES apply here
    /// (Art. 6(1)(f) → Art. 21(1) reaches it); report-and-escalate is a <i>mechanism</i> choice, not
    /// a refusal. See <see cref="ErasureCascadeRegistry"/>.
    /// </summary>
    Task<int> CountSavedSearchesAsync(
        string identifier, CancellationToken cancellationToken);

    /// <summary>
    /// How many applicants' frozen ad snapshots (<c>snapshot_company</c> /
    /// <c>snapshot_description</c>) hold <paramref name="identifier"/>.
    /// <b>Counted and REPORTED, never erased</b> (Art. 17(3)(e) — STOPP-3).
    /// </summary>
    /// <remarks>
    /// We search it precisely BECAUSE we do not erase it: <b>a legal ground asserted over a
    /// population we never counted is a ground asserted over a silence.</b> That is how the last
    /// registry stayed wrong. <c>snapshot_company</c> is non-nullable, so it is populated on EVERY
    /// application — unlike <c>snapshot_description</c>, which was measured at zero rows and is the
    /// only one the original scope reasoned about.
    /// </remarks>
    Task<int> CountApplicationSnapshotsAsync(
        string identifier, CancellationToken cancellationToken);

    /// <summary>
    /// How many USER-AUTHORED free-text rows (cover letters, application notes, follow-up notes)
    /// mention <paramref name="identifier"/>. <b>Counted and REPORTED; a human erases them.</b>
    /// </summary>
    /// <remarks>
    /// A user can absolutely have written "Ringde Magnus Fagerberg" in her own note. That is the
    /// RECRUITER'S personal data (Art. 6(1)(f), reached by Art. 21(1)), so her right applies — but
    /// silently rewriting a user's private note about her own job hunt is not something a job may do.
    /// <para>
    /// <b>This surface was found by driving the cascade registry from the EF model.</b> Nobody had
    /// enumerated it; no version of this feature would have searched it; and every design document
    /// in the issue, including the ones that condemned exactly this, had missed it.
    /// </para>
    /// </remarks>
    Task<int> CountUserAuthoredTextAsync(
        string identifier, CancellationToken cancellationToken);
}
