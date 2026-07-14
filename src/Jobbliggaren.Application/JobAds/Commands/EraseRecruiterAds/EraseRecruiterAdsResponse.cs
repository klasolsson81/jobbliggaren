using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;

/// <summary>
/// What the erasure request did. <b>Never a bare number.</b> Art. 12(3) requires us to say what we
/// did, and a number cannot.
/// </summary>
/// <remarks>
/// <see cref="JsonStringEnumConverter"/> is not decoration. Without it System.Text.Json serialises
/// this as an INTEGER — <c>{"outcome": 1}</c> — which is a bare opaque number, i.e. exactly the
/// thing this type exists to abolish, smuggled back in through the wire format.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ErasureOutcome>))]
public enum ErasureOutcome
{
    /// <summary>
    /// Nothing matched in the surfaces we are ABLE to search.
    /// </summary>
    /// <remarks>
    /// <b>It does not say "we hold no data about you", and it must not be allowed to.</b> Seven
    /// columns are encrypted under per-user keys (Forms A, B and C — the notes, the cover
    /// letters, and the CV in all three of its stored shapes) and cannot be scanned at all
    /// (<see cref="ErasureColumnDisposition.HeldButNotSearchable"/>), so a claim of total absence
    /// is one we cannot verify — and therefore one we must not be able to type.
    /// <see cref="EraseRecruiterAdsResponse.CouldNotSearch"/> ships with every reply, including
    /// this one, and names them.
    /// </remarks>
    NoMatchInSearchableSurfaces,

    /// <summary>Nothing was written. <see cref="EraseRecruiterAdsResponse.Matches"/> is the list to review.</summary>
    DryRun,

    /// <summary>Ads were erased. Irreversible.</summary>
    AdsErased,

    /// <summary>
    /// No ad was erased, but cascade rows were (today: recent searches). A recruiter whose only
    /// trace is a user's cached search term is a real case, and it must not be reported as
    /// <see cref="AdsErased"/> — the runbook chains that word to <i>"vi har tagit bort hela
    /// annonsen"</i>, which would be a false statement to a named person.
    /// </summary>
    CascadeErasedOnly,

    /// <summary>
    /// We DO hold matching data, and none of it was erased — the operator reviewed the matches and
    /// confirmed none, or everything we matched lives on a surface a human must settle.
    /// <b>Not the same as <see cref="NoMatchInSearchableSurfaces"/>:</b> "we found nothing" and "we
    /// found things and removed none of them" are different sentences, and only one of them can
    /// honestly be sent to a data subject as a completed erasure.
    /// </summary>
    NothingErased,
}

/// <summary>
/// Which channel produced a hit. The operator must be able to tell a real match from a false
/// positive, and a window with no hit in it is evidence of nothing.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ErasureMatchChannel>))]
public enum ErasureMatchChannel
{
    /// <summary>Literal hit in the ad body. <c>MatchedExcerpt</c> is a window around it.</summary>
    Description,

    /// <summary>Literal hit in the title.</summary>
    Title,

    /// <summary>Literal hit in <c>company_name</c> — the <i>enskild firma</i> case.</summary>
    CompanyName,

    /// <summary>
    /// The hit came from the FTS lexemes or from <c>raw_payload</c>, so there is NO literal
    /// substring to window. <c>MatchedExcerpt</c> is empty, deliberately: an excerpt that showed
    /// the head of an unrelated body would be a control that degrades silently on exactly the
    /// channels that were added because they were missing. The operator opens the ad.
    /// </summary>
    FullTextOrRawPayload,

    /// <summary>
    /// Exact match on <c>job_ads.organization_number</c> — the identifier was an org.nr, and for
    /// an <i>enskild firma</i> that org.nr IS her personnummer (#842 CTO ruling 2026-07-14).
    /// <c>MatchedExcerpt</c> is the normalised org.nr that matched, suffixed
    /// <c>(personnummer-format)</c> when it is personnummer-shaped — a personnummer is never
    /// surfaced un-flagged, even in the operator's review payload (ADR 0087 D8(c)).
    /// </summary>
    OrganizationNumber,
}

/// <summary>
/// One ad the identifier matched — <b>the unit of human review</b>. The dry run hands the operator
/// the ADS, not a count: a count cannot be reviewed.
/// </summary>
public sealed record ErasureJobAdMatch(
    Guid JobAdId,
    string? ExternalId,
    string Title,
    string Company,
    ErasureMatchChannel MatchedChannel,
    string MatchedExcerpt);

/// <summary>
/// One recent-search row the identifier matched — and WHY. <c>Q</c> is the user's free-text term
/// when the word-boundary channel hit; <c>MatchedEmployerOrgNr</c> is the normalised org.nr when
/// the employer-filter channel hit. At least one is always present — the constructor enforces it.
/// There is deliberately NO user id — the operator reviews the STRINGS that will be deleted, and
/// needs to identify nobody to do it.
/// </summary>
/// <remarks>
/// <c>Q</c> is nullable because an employer-only search (<c>q = NULL</c>, the domain's own
/// canonical form) is a real matched row. Round 5's non-nullable <c>Q</c> made that row
/// UNREPRESENTABLE, so <c>.Where(r =&gt; r.Q is not null)</c> was the only way to compile — and
/// precisely there the match died: found by the SQL, discarded by the projection, never deleted,
/// never counted, while the registry certified the column erased. The type forced the bug; the
/// type now forbids it in BOTH directions: a match must carry its evidence, whichever channel
/// produced it, and a match carrying NO evidence cannot be constructed (round-6 M6-2 — two
/// nullables invite the both-null state the query can never produce; the guard makes the
/// invariant a fact about the type instead of a <c>!</c> in a handler).
/// </remarks>
public sealed record ErasureRecentSearchMatch
{
    public Guid Id { get; }

    public string? Q { get; }

    public string? MatchedEmployerOrgNr { get; }

    public ErasureRecentSearchMatch(Guid id, string? q, string? matchedEmployerOrgNr)
    {
        if (q is null && matchedEmployerOrgNr is null)
        {
            throw new ArgumentException(
                "A recent-search match must carry its evidence: q or the matched org.nr. A row "
                + "with neither matched on nothing and must not exist.",
                nameof(q));
        }

        Id = id;
        Q = q;
        MatchedEmployerOrgNr = matchedEmployerOrgNr;
    }
}

/// <summary>
/// Per-surface counts, whose member names are pinned against
/// <see cref="ErasureCascadeRegistry.ReportedSurfaces"/>. Reported for BOTH "matched" and "erased"
/// — and <b>the gap between them is the disclosure.</b>
/// </summary>
/// <remarks>
/// A saved search that mentions the recruiter is <c>Matched</c> and not <c>Erased</c> (a human
/// settles it — her right DOES apply, so this is a mechanism choice, never a refusal). An applicant
/// snapshot naming her is likewise matched and not erased (Art. 17(3)(e)). The two numbers
/// therefore differ, the operator cannot miss it, and the reply template is forced to say so.
/// <para>
/// ⚠ <b>The gap doctrine has a hole, and <see cref="EraseRecruiterAdsResponse.CouldNotSearch"/> is
/// the patch:</b> a surface we cannot SEARCH produces no gap at all, because it never appears in
/// <c>Matched</c> either. Counts alone would have reported a clean, complete erasure precisely when
/// three encrypted columns were never looked at.
/// </para>
/// </remarks>
public sealed record ErasureSurfaceCounts(
    int JobAds,
    int RecentJobSearches,
    int SavedSearches,
    int ApplicationSnapshots,
    int ManualAdEntries,
    int CompanyWatchCriteria,
    int ResumeMetadata,
    int ApplicationsReferencingMatchedAds)
{
    public static ErasureSurfaceCounts None { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// The sum of every surface. <b>Hand-written, and load-bearing twice</b> — it decides
    /// <c>NoMatchInSearchableSurfaces</c> and it feeds <see cref="EraseRecruiterAdsResponse.Outcome"/>.
    /// Add a surface and forget this line, and we tell a data subject we found nothing on a surface
    /// we searched and found her on. <c>ErasureCascadeRegistryTests</c> pins it against the reflected
    /// member set: the sum must include EVERY int property.
    /// </summary>
    public int Total =>
        JobAds + RecentJobSearches + SavedSearches + ApplicationSnapshots
        + ManualAdEntries + CompanyWatchCriteria + ResumeMetadata
        + ApplicationsReferencingMatchedAds;
}

/// <summary>
/// The surfaces we HOLD and cannot search, why, and what she can do about it. Generated from
/// <see cref="ErasureCascadeRegistry.UnsearchableColumns"/>, so it cannot drift from the
/// classification the build enforces.
/// </summary>
/// <remarks>
/// Non-positional with a private constructor, deliberately: the earlier positional record's doc
/// said <i>"it can neither be forgotten nor faked"</i> while
/// <c>new UnsearchableSurfaces([], "", "")</c> compiled (round-3 Minor 1, carried three rounds).
/// Now <see cref="FromRegistry"/> IS the only construction route, and the claim is a fact about
/// the type instead of a sentence about it.
/// </remarks>
public sealed record UnsearchableSurfaces
{
    public IReadOnlyList<string> Columns { get; }

    public string Reason { get; }

    public string Escalation { get; }

    private UnsearchableSurfaces(IReadOnlyList<string> columns, string reason, string escalation)
    {
        Columns = columns;
        Reason = reason;
        Escalation = escalation;
    }

    /// <summary>The only construction route. Derived, so it can neither be forgotten nor faked.</summary>
    public static UnsearchableSurfaces FromRegistry() => new(
        columns: ErasureCascadeRegistry.UnsearchableColumns,

        // SEVEN columns across all three encryption forms — not "three notes columns" (that text
        // survived two rounds after the list outgrew it, on a member handed to a data subject).
        reason:
            "Encrypted at rest under a per-user key envelope (ADR 0049 C3/C4 / 0066 — Form A "
            + "in-place text: the application notes, follow-up notes, cover letters and the raw CV "
            + "text; Form B encrypted shadows: the structured CV content; Form C sealed binary: "
            + "the uploaded CV FILE itself). Scanning them would mean decrypting every user's "
            + "private texts and documents to serve one third party's request — building a "
            + "read-everyone's-content capability permanently (Art. 25(2)/32), with no lawful "
            + "basis toward those other data subjects (Art. 6). We refuse the MECHANISM, not the "
            + "person.",

        escalation:
            "applications.job_ad_id already names every application written TO a matched ad, "
            + "exactly and without decryption — that is where the overlap lives, and it is reported "
            + "as ApplicationsReferencingMatchedAds. For the residual: if she knows she appears in a "
            + "specific application or a specific CV, a TARGETED decryption of that one identified "
            + "user's record is proportionate and buildable, and a human does it. The reply must "
            + "offer her that route.");
}

/// <summary>
/// The Art. 17 erasure response (ADR 0106 D8, #842).
/// </summary>
/// <param name="RequestId">Correlates this reply with its <c>audit_log</c> row.</param>
/// <param name="DryRun">True ⇒ nothing was written.</param>
/// <param name="Matched">What the scan found, per surface.</param>
/// <param name="Erased">
/// What was destroyed, per surface. <c>Matched &gt; Erased</c> on a surface is expected, disclosed,
/// and never silently reconciled.
/// </param>
/// <param name="Matches">The ads themselves — <b>what the operator reviews</b>. Populated on a dry run.</param>
/// <param name="MatchedRecentSearchTerms">
/// The distinct match EVIDENCE, one line per matched row's reason, WITHOUT user ids: the <c>q</c>
/// term for a word-boundary hit, or <c>arbetsgivarfilter: &lt;org.nr&gt;</c> for an employer-only
/// hit (suffixed <c>(personnummer-format)</c> when the org.nr is personnummer-shaped — ADR 0087
/// D8(c), never surfaced un-flagged). These rows are hard-deleted with no per-id confirmation
/// ceremony (they are an auto-captured, self-rebuilding cache — cap 20, evict oldest), so the
/// operator must at least SEE what will go, and WHY it matched. <b>A count cannot be reviewed</b> —
/// this PR's own doctrine, applied to the one surface where it had been forgotten.
/// </param>
/// <param name="ErasedExternalIds">
/// Arbetsförmedlingen's ids for the erased ads. Not personal data, and the accountability spine
/// (Art. 5(2)/30): this is what an auditor, or the recruiter, can check us against.
/// </param>
/// <param name="CouldNotSearch">
/// <b>Required on EVERY outcome, including the ones that found nothing.</b> A convention a reviewer
/// must remember is weaker than a type the compiler enforces, so this is a positional member: it is
/// structurally impossible to reply to her without naming what we could not look at.
/// </param>
public sealed record EraseRecruiterAdsResponse(
    Guid RequestId,
    bool DryRun,
    ErasureSurfaceCounts Matched,
    ErasureSurfaceCounts Erased,
    IReadOnlyList<ErasureJobAdMatch> Matches,
    IReadOnlyList<string> MatchedRecentSearchTerms,
    IReadOnlyList<string> ErasedExternalIds,
    UnsearchableSurfaces CouldNotSearch)
{
    /// <summary>
    /// <b>DERIVED from the counts, never supplied.</b> The outcome word is what the runbook keys its
    /// reply template on, so it is the one value in this response that must not be able to lie —
    /// and the way to guarantee that is to remove the argument someone could get wrong.
    /// </summary>
    /// <remarks>
    /// The invariant is a theorem rather than a convention: <b>no outcome can name a surface whose
    /// erased count is zero</b>, because the word is a pure function of the numbers. As a
    /// constructor parameter it could be passed <c>AdsErased</c> with <c>Erased.JobAds == 0</c> —
    /// and the runbook chains that word to <i>"vi har tagit bort hela annonsen"</i>, a false
    /// statement to a named person.
    /// </remarks>
    public ErasureOutcome Outcome =>
        Matched.Total == 0 ? ErasureOutcome.NoMatchInSearchableSurfaces
        : DryRun ? ErasureOutcome.DryRun
        : Erased.JobAds > 0 ? ErasureOutcome.AdsErased
        : Erased.Total > 0 ? ErasureOutcome.CascadeErasedOnly
        : ErasureOutcome.NothingErased;
}
