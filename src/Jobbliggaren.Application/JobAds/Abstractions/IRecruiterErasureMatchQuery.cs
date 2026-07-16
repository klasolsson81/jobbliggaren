using Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;

namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Application port for the fail-safe matching an Art. 17 erasure request needs (ADR 0106 D8,
/// #842). The implementation lives in Infrastructure because PostgreSQL full-text search, the
/// <c>jsonb::text</c> cast and the ARE word-boundary regex are Npgsql concerns, which the
/// architecture test forbids in Application (CLAUDE.md §2.1). Same reason, same shape, as
/// <see cref="IJobAdSearchQuery"/>.
/// </summary>
/// <remarks>
/// <b>Every method here is a channel, and every channel must be able to READ what it reports on.</b>
/// Do not add a method that <c>LIKE</c>s a plaintext identifier against a DEK-encrypted column: it
/// compares her name to ciphertext, returns 0 on every request forever, and that 0 is then reported
/// as a search result. What we cannot search is CLASSIFIED as unsearchable
/// (<see cref="ErasureColumnDisposition.HeldButNotSearchable"/>) and DISCLOSED
/// (<see cref="UnsearchableSurfaces"/>) — never scanned and quietly reported as clean.
/// </remarks>
public interface IRecruiterErasureMatchQuery
{
    /// <summary>
    /// Ads matching <paramref name="identifier"/> on ANY channel, in any status <b>except</b>
    /// already-<c>Erased</c>. Returns the ads themselves, because the operator has to review them —
    /// a count cannot be reviewed.
    /// </summary>
    /// <remarks>
    /// <b>FOUR channels, and each exists because the others miss something.</b>
    /// <list type="number">
    /// <item><b>FTS</b> — <c>search_vector @@ websearch_to_tsquery(…)</c>. Her name is a lexeme, so
    /// this reaches forms the substring channel cannot (<i>"Fagerberg, Magnus"</i> vs the query
    /// <i>"Magnus Fagerberg"</i>).</item>
    /// <item><b>Substring over <c>title</c>/<c>description</c>/<c>raw_payload</c></b> — it finds the
    /// identifier <i>as supplied</i>, including forms Postgres's parser will not lexeme the same
    /// way. It does <b>not</b> de-obfuscate: <c>anna@acme.se</c> does not match
    /// <c>anna(at)acme.se</c> on any channel. What serves the obfuscated tail is her NAME, which
    /// sits in the body in plain words.</item>
    /// <item><b>Substring over <c>company_name</c></b> — an <i>enskild firma</i>'s company name IS a
    /// natural person's name, and it is not in <c>search_vector</c> (built from title + description
    /// only). <c>PurgeStaleRawPayloadsJob</c> NULLs <c>raw_payload</c> after 30 days, so without
    /// this channel every ad older than a month would report no match while her name sat in
    /// plaintext in a column we scan.</item>
    /// <item><b>Exact match on <c>organization_number</c></b> — when the identifier IS an org.nr
    /// (normalised in Domain: <c>OrganizationNumber.TryFromWrittenForm</c>), it is matched exactly
    /// against the materialised column (#841). Forced by the SAME 30-day logic as channel 3: after
    /// the <c>raw_payload</c> purge, this column is the ONLY place a sole trader's org.nr — which
    /// IS her personnummer — survives in the row. Without it, an org.nr request would be answered
    /// <i>"no ads"</i> about every ad older than a month while we held her personnummer in a column
    /// we never looked at (#842 CTO ruling 2026-07-14).</item>
    /// </list>
    /// <para>
    /// The union <b>over-matches, deliberately</b>. A false positive costs the operator a second
    /// look at the mandatory dry run; a false negative costs a false Art. 12(3) confirmation to a
    /// named person.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<ErasureJobAdMatch>> FindJobAdsAsync(
        string identifier, CancellationToken cancellationToken);

    /// <summary>
    /// Recent-search rows whose <c>q</c> contains <paramref name="identifier"/> <b>as a whole
    /// word</b>, OR whose <c>employer_list</c> contains it as an <b>exact normalised org.nr</b>.
    /// Returns the rows with their match evidence (the term, or the matched org.nr), because they
    /// are HARD-DELETED and the operator must see what will go.
    /// </summary>
    /// <remarks>
    /// <b>The looseness of a match must be inversely proportional to the strength of its review
    /// gate.</b> Ads are matched by naked substring and then confirmed id-by-id by a human — the
    /// over-match is a fail-safe precisely BECAUSE a human confirms. These rows have no such gate
    /// (they are an auto-captured cache, cap 20 per seeker, self-rebuilding on the next search), so
    /// the same looseness would be an unreviewed deletion of another user's row: erasing
    /// <c>anna</c> would take <c>marianna</c>, <c>johanna</c> and <c>susanna</c> with it. Hence the
    /// word boundary on <c>q</c> — and EXACT equality on <c>employer_list</c>, whose write path
    /// admits only 10-digit org.nr (<c>ValidateEmployerList</c> → <c>OrganizationNumber.Create</c>):
    /// a structured key gets structured matching, never a regex over prose (round 5's vacuous arm).
    /// An employer-only search has <c>q = NULL</c> — the domain's canonical form — and its row is
    /// returned and deleted like any other; round 5 threw exactly that row away after the SQL had
    /// found it.
    /// </remarks>
    Task<IReadOnlyList<ErasureRecentSearchMatch>> FindRecentJobSearchesAsync(
        string identifier, CancellationToken cancellationToken);

    /// <summary>
    /// How many saved searches physically hold <paramref name="identifier"/> in their stored
    /// <c>criteria</c> or their <c>name</c>. <b>Counted and REPORTED — a human erases them.</b> Her
    /// right DOES apply (Art. 6(1)(f) → Art. 21(1) reaches it); report-and-escalate is a
    /// <i>mechanism</i> choice, not a refusal. See <see cref="ErasureCascadeRegistry"/>.
    /// </summary>
    Task<int> CountSavedSearchesAsync(
        string identifier, CancellationToken cancellationToken);

    /// <summary>
    /// How many applicants' frozen ad snapshots (<c>snapshot_company</c> / <c>snapshot_title</c> /
    /// <c>snapshot_description</c> / <c>snapshot_url</c>) hold <paramref name="identifier"/>.
    /// <b>Counted and REPORTED, never erased</b> (Art. 17(3)(e) — STOPP-3).
    /// </summary>
    /// <remarks>
    /// We search it precisely BECAUSE we do not erase it — the retention ground has to be asserted
    /// over a population we counted (see <c>ErasureCascadeRegistry.WrittenGrounds</c>).
    /// <c>snapshot_company</c> is non-nullable, so it is populated on EVERY application, unlike
    /// <c>snapshot_description</c>; scanning the description alone would miss the whole surface.
    /// <c>snapshot_url</c> is in the set because a URL path carries names routinely — the identical
    /// argument as <c>manual_url</c>; it was classified as searched and NOT searched for one round
    /// (round-5 B5-2), which is why the channel list in the registry now drives this method.
    /// </remarks>
    Task<int> CountApplicationSnapshotsAsync(
        string identifier, CancellationToken cancellationToken);

    /// <summary>
    /// The applications whose frozen <c>snapshot_contacts</c> (#842 Tier A) hold
    /// <paramref name="identifier"/> — returned as IDS, because this surface is ERASED surgically
    /// (<c>Application.EraseAdSnapshotContacts</c>) and an erase needs its targets, not a count.
    /// </summary>
    /// <remarks>
    /// <b>Its own surface, deliberately (T2 CTO 2026-07-16)</b> — NOT folded into
    /// <c>ApplicationSnapshots</c>: one surface = one disposition = one honest Matched−Erased
    /// meaning. The four body columns are retained under Art. 17(3)(e) (the applicant's
    /// aktivitetsrapport spine); the contact block is HER data whose follow-up purpose is spent at
    /// the erasure request, and it goes. Folding them would force the Art. 12(3) reply to either
    /// over-claim ("erased from applicants' records" — false for the body) or under-claim
    /// ("retained" — false for the contacts). Also a completeness channel: an ARCHIVED ad's frozen
    /// contact is unreachable via the ad match + FK route, so it must be independently searched.
    /// The funnel never rewrites a snapshot, so the erase here is durable by construction.
    /// </remarks>
    Task<IReadOnlyList<Guid>> FindApplicationSnapshotContactsAsync(
        string identifier, CancellationToken cancellationToken);

    /// <summary>
    /// How many applications hold <paramref name="identifier"/> in the PLAINTEXT columns a user
    /// typed or pasted for an application she tracks herself (<c>manual_company</c> /
    /// <c>manual_title</c> / <c>manual_url</c>). <b>Counted and REPORTED; a human erases them.</b>
    /// </summary>
    /// <remarks>
    /// <b>The surface must be named for what it actually searches</b> — the name is reported to the
    /// data subject as a thing we looked at, so it must not promise more than the query covers.
    /// <para>
    /// <c>manual_url</c> is a 2000-char pasted string with no validation at the persistence
    /// boundary: it is free text with a max length, and a URL path carries names routinely
    /// (<c>linkedin.com/in/&lt;name&gt;</c>). The DEK-encrypted columns on this table and its
    /// children are <see cref="ErasureColumnDisposition.HeldButNotSearchable"/> and are disclosed,
    /// never scanned.
    /// </para>
    /// </remarks>
    Task<int> CountManualAdEntriesAsync(
        string identifier, CancellationToken cancellationToken);

    /// <summary>
    /// How many company-watch criteria carry <paramref name="identifier"/> in their user-authored
    /// <c>label</c>. <b>Counted and REPORTED; a human erases it</b> — and here the remedy is always
    /// constructible and lossless (<c>UpdateLabel(null)</c>: the label is optional, and a criterion
    /// IS its codes).
    /// </summary>
    /// <remarks>
    /// A criterion is a predicate over SNI + kommun codes, so a recruiter's name is an unlikely
    /// thing to type here. <b>Unlikely is not a disposition</b> — the write path accepts 120
    /// characters of anything, so the column is searched rather than assumed empty.
    /// </remarks>
    Task<int> CountCompanyWatchCriteriaAsync(
        string identifier, CancellationToken cancellationToken);

    /// <summary>
    /// How many CVs carry <paramref name="identifier"/> in their PLAINTEXT metadata — <b>five
    /// columns across three tables</b>: the two file names
    /// (<c>parsed_resumes.source_file_name</c> + <c>resume_files.file_name</c> — the same uploaded
    /// file, two tables), the CV's own <c>resumes.name</c> (typed via <c>Rename()</c>), and the
    /// denormalised projections <c>resumes.latest_role</c> + <c>resumes.top_skills</c>.
    /// <b>Counted and REPORTED; a human erases it.</b>
    /// </summary>
    /// <remarks>
    /// A filename is plaintext free text the user typed, and the repo already MASKS personnummer out
    /// of it (#465) — a guard bolted on precisely because users put arbitrary text into filenames.
    /// The column was classified <i>"structurally cannot hold a recruiter's personal data"</i> while
    /// a control in the same aggregate said otherwise. Both file-name tables are searched, because a
    /// registry whose verdicts disagree about identical data is worth nothing — and the three
    /// <c>resumes</c> columns are searched on the same ground that already searches
    /// <c>saved_searches.name</c> (the identical datum, one aggregate over). The CV BODY is not
    /// here: it is DEK-encrypted (<c>HeldButNotSearchable</c>) and disclosed, never quietly
    /// reported as clean. This doc is what the runbook's disclosure is written from — under-describe
    /// the channel and the reply under-describes what we searched (round-5 Minor 11 → B5-3.4).
    /// </remarks>
    Task<int> CountResumeMetadataAsync(
        string identifier, CancellationToken cancellationToken);

    /// <summary>
    /// How many applications REFERENCE one of <paramref name="matchedJobAdIds"/> via
    /// <c>applications.job_ad_id</c>. <b>The structural channel — exact, and with zero
    /// decryption.</b>
    /// </summary>
    /// <remarks>
    /// This is what replaced the ciphertext scan, and it is strictly better than the search it
    /// replaces. <b>If a recruiter's name is in a user's cover letter, it is overwhelmingly a cover
    /// letter for THAT RECRUITER'S AD</b> — <i>"Hej Magnus,"</i> sits in the letter written to
    /// Magnus's ad, and <i>"ringde Magnus"</i> sits in the note on the application to it. The
    /// foreign key hands us that set for free, exactly, where the scan was trying to brute-force it
    /// through a thousand people's private text.
    /// <para>
    /// It does <b>not</b> close the residual — a note could name her without referencing her ad (she
    /// recruits for several). That residual is real, it is DISCLOSED in
    /// <see cref="UnsearchableSurfaces"/>, and it carries an escalation route. It is a tail now,
    /// not the whole surface, and we can say which is which.
    /// </para>
    /// </remarks>
    Task<int> CountApplicationsReferencingAsync(
        IReadOnlyCollection<Guid> matchedJobAdIds, CancellationToken cancellationToken);
}
