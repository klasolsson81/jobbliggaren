using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;

/// <summary>
/// What the erasure request did. <b>Never a bare number again.</b>
/// </summary>
/// <remarks>
/// The old endpoint answered every request with <c>200 OK, rowsAffected: 0</c> and no code path
/// distinguished "erased nothing" from "erased something" — so the runbook told the operator to
/// confirm erasure regardless, and 0 was the only value it could ever return. Art. 12(3) requires
/// us to say <i>what we did</i>. A number cannot.
/// <para>
/// <see cref="JsonStringEnumConverter"/> is not decoration. Without it System.Text.Json serialises
/// this as an INTEGER — <c>{"outcome": 1}</c> — which is a bare opaque number, i.e. exactly the
/// thing this type exists to abolish, smuggled back in through the wire format. (It shipped that
/// way for one commit. The test that caught it was asserting the wrong type and blew up rather than
/// passing quietly, which is the only reason it was caught at all.)
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ErasureOutcome>))]
public enum ErasureOutcome
{
    /// <summary>
    /// We hold nothing matching this identifier — and because every channel ran over every
    /// free-text surface, this is now a statement that is <b>true</b> when we make it. It is the
    /// sentence the old mechanism said on every request and could never actually mean.
    /// </summary>
    NoMatchingDataHeld,

    /// <summary>Nothing was written. <see cref="EraseRecruiterAdsResponse.Matches"/> is the list to review.</summary>
    DryRun,

    /// <summary>Ads were erased. Irreversible.</summary>
    AdsErased,

    /// <summary>
    /// We DO hold matching data, and none of it was erased — because the operator confirmed no ads
    /// (he reviewed the matches and rejected them), or because everything we matched lives on a
    /// surface a human has to settle. <b>Not the same as <see cref="NoMatchingDataHeld"/>, and
    /// collapsing the two would be the old <c>rowsAffected: 0</c> ambiguity rebuilt:</b> "we found
    /// nothing" and "we found things and removed none of them" are different sentences, and only one
    /// of them can honestly be sent to a data subject as a completed erasure.
    /// </summary>
    NothingErased,
}

/// <summary>
/// One ad the identifier matched — <b>the unit of human review</b>.
/// </summary>
/// <remarks>
/// The dry run must hand the operator the ADS, not a count. A count cannot be reviewed: a recruiter
/// named <i>Anna</i> substring-matches <i>Johanna</i>, <i>Susanna</i> and <i>Marianna</i> across
/// thousands of ads, and an operator who sees <c>4127</c> and retypes <c>4127</c> has reviewed
/// nothing while irreversibly destroying 4 127 ads. ADR 0106 D8 bound a list; a later ruling
/// reshaped it to counts, in the safety-critical direction. It is a list.
/// </remarks>
public sealed record ErasureJobAdMatch(
    Guid JobAdId,
    string? ExternalId,
    string Title,
    string MatchedExcerpt);

/// <summary>
/// Per-surface counts, generated from <see cref="ErasureCascadeRegistry"/>. Reported for BOTH
/// "matched" and "erased" — and <b>the gap between them is the disclosure.</b>
/// </summary>
/// <remarks>
/// A saved search that mentions the recruiter is <c>Matched</c> and not <c>Erased</c> (a human
/// settles it — her right DOES apply, so this is a mechanism choice, never a refusal). An applicant
/// snapshot naming her is likewise matched and not erased (Art. 17(3)(e)). The two numbers therefore
/// differ, the operator cannot miss it, and the reply template is forced to say so. The alternative
/// is a single number that quietly means "what we felt like erasing" — the shape of every defect in
/// this issue.
/// </remarks>
public sealed record ErasureSurfaceCounts(
    int JobAds,
    int RecentJobSearches,
    int SavedSearches,
    int ApplicationSnapshots,
    int UserAuthoredText)
{
    public static ErasureSurfaceCounts None { get; } = new(0, 0, 0, 0, 0);

    public int Total =>
        JobAds + RecentJobSearches + SavedSearches + ApplicationSnapshots + UserAuthoredText;
}

/// <summary>
/// The Art. 17 erasure response (ADR 0106 D8, #842).
/// </summary>
/// <param name="RequestId">Correlates this reply with its <c>audit_log</c> row.</param>
/// <param name="Outcome">What happened.</param>
/// <param name="DryRun">True ⇒ nothing was written.</param>
/// <param name="Matched">What the scan found, per surface.</param>
/// <param name="Erased">
/// What was destroyed, per surface. <c>Matched &gt; Erased</c> on a surface is expected, disclosed,
/// and never silently reconciled.
/// </param>
/// <param name="Matches">
/// The ads themselves — <b>what the operator reviews</b>. Populated on a dry run.
/// </param>
/// <param name="ErasedExternalIds">
/// Arbetsförmedlingen's ids for the erased ads. Not personal data, and the accountability spine
/// (Art. 5(2)/30): this is what an auditor, or the recruiter, can check us against.
/// </param>
public sealed record EraseRecruiterAdsResponse(
    Guid RequestId,
    ErasureOutcome Outcome,
    bool DryRun,
    ErasureSurfaceCounts Matched,
    ErasureSurfaceCounts Erased,
    IReadOnlyList<ErasureJobAdMatch> Matches,
    IReadOnlyList<string> ErasedExternalIds);
