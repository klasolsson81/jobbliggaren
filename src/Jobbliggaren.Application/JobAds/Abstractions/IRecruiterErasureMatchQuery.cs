namespace Jobbliggaren.Application.JobAds.Abstractions;

/// <summary>
/// Application port for the two-channel, fail-safe matching an Art. 17 erasure request needs
/// (ADR 0106 D8, #842). The implementation lives in Infrastructure because PostgreSQL full-text
/// search (<c>websearch_to_tsquery</c>/<c>@@</c>) and the <c>jsonb::text</c> cast are Npgsql
/// concerns — an assembly the architecture test forbids in Application (CLAUDE.md §2.1). Same
/// reason, same shape, as <see cref="IJobAdSearchQuery"/>.
/// </summary>
/// <remarks>
/// <b>Two channels, and the second one is the point.</b> Channel 1 is FTS — the exposure we are
/// closing, since <c>search_vector @@ websearch_to_tsquery('swedish', '&lt;email&gt;')</c> is a hit
/// for any logged-in user today, and the recruiter's NAME hits the same way via ordinary word
/// lexemes. Channel 2 is a case-insensitive substring scan over <c>title</c>, <c>description</c>
/// and <c>raw_payload::text</c>, because <b>FTS cannot find an obfuscated address</b>
/// (<c>anna(at)acme.se</c> tokenises as ordinary words) — and the obfuscated tail is exactly the
/// population Tier B exists to serve, the one Tier A's regex is going to miss. Channel 2 also
/// reaches <c>employer.name</c> inside <c>raw_payload</c>, which is how a request naming an
/// <i>enskild firma</i>'s owner finds her ad at all.
/// <para>
/// The union <b>over-matches, deliberately</b>. A false positive costs the operator a second look
/// at the mandatory dry run; a false negative costs a false Art. 12(3) confirmation to a named
/// person. Fail safe means erring toward the first.
/// </para>
/// </remarks>
public interface IRecruiterErasureMatchQuery
{
    /// <summary>
    /// Ids of job ads matching <paramref name="identifier"/> on EITHER channel, in any status
    /// <b>except</b> already-<c>Erased</c> (an erased ad holds nothing to match, and counting it
    /// again would inflate the number we report to the data subject).
    /// </summary>
    Task<IReadOnlyList<Guid>> FindJobAdIdsAsync(
        string identifier, CancellationToken cancellationToken);

    /// <summary>
    /// How many saved searches physically hold <paramref name="identifier"/> in their stored
    /// criteria. <b>Counted and REPORTED — never erased</b>; the ground is in
    /// <see cref="JobAds.Commands.EraseRecruiterAds.ErasureCascadeRegistry"/>. The gap between
    /// this number and the zero we erase IS the disclosure, and it is structural so that nobody
    /// has to remember it.
    /// </summary>
    Task<int> CountSavedSearchesAsync(
        string identifier, CancellationToken cancellationToken);
}
