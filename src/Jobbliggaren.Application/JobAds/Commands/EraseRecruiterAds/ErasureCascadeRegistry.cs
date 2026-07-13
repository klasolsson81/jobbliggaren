using Jobbliggaren.Domain.Auditing;
using Jobbliggaren.Domain.CompanyWatches;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Matching;
using Jobbliggaren.Domain.RecentJobSearches;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Files;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Domain.SavedJobAds;
using Jobbliggaren.Domain.SavedSearches;

namespace Jobbliggaren.Application.JobAds.Commands.EraseRecruiterAds;

/// <summary>
/// The Art. 17 cascade registry for recruiter PII (#842) — every persisted surface, classified,
/// with a written reason. <c>ErasureCascadeRegistryTests</c> pins it: a new <c>DbSet</c> on
/// <c>IAppDbContext</c> that appears in none of the three sets <b>breaks the build</b>.
/// </summary>
/// <remarks>
/// <b>Why this type exists at all.</b> ADR 0024 already had an Art. 17 cascade registry. It listed
/// <c>raw_payload</c> and nothing else — not <c>job_ads.description</c>, where the address actually
/// was. It was prose in a document, so it went stale silently, and it is a large part of why #842
/// survived two releases while an auditor reading that registry would have concluded we were fine.
/// <para>
/// A registry a reviewer has to remember to update is not a registry. This one is a type the
/// compiler and a test enforce, and the erasure command's own response is generated from it — so
/// the thing we <i>tell the data subject</i> and the thing we <i>actually do</i> are derived from
/// one source. That is the only structure that survives the next person who adds a table.
/// </para>
/// <para>
/// <b>The count is not the control.</b> When this was written, <c>recent_job_searches</c> held one
/// row and <c>saved_searches</c> held zero. Keying a control to a row count measured on one
/// afternoon is exactly the mistake #842 <i>is</i> — the vacuous purger was also correct as long
/// as nobody looked. The cascade ships on an empty table, and reports 0 truthfully.
/// </para>
/// </remarks>
public static class ErasureCascadeRegistry
{
    /// <summary>
    /// Surfaces the erasure command searches AND erases.
    /// </summary>
    /// <remarks>
    /// <see cref="JobAd"/> — the carrier. Whole-record erasure (<see cref="JobAd.Erase"/>), which
    /// is what makes the promise provable: <c>description</c>, <c>title</c>, <c>company_name</c>,
    /// <c>url</c>, <c>raw_payload</c>, the seven raw_payload-derived generated columns,
    /// <c>extracted_terms</c>, the STORED <c>extracted_lexemes</c> shadow and the STORED
    /// <c>search_vector</c> all go together. No detector, so no recall question.
    /// <para>
    /// <see cref="RecentJobSearch"/> — hard-delete of the row. A user who searched the recruiter's
    /// name persisted that name into her own search history (the FTS reverse-lookup this issue is
    /// about is exactly what makes that reachable). Nulling <c>q</c> is NOT available: <c>q</c> is
    /// a derivative of <c>FilterHash</c>, which is the row's identity, and the aggregate states it
    /// must never diverge — a nulled <c>q</c> corrupts the row rather than cleaning it. The
    /// aggregate already binds hard-delete as its disposal semantics ("auto-capture-rader har ingen
    /// audit-trail-värdighet"), and ADR 0067 Fas C2 already mass-deleted rows of this table on the
    /// same reasoning. User cost is zero: the cap-20 list rebuilds on her next search.
    /// </para>
    /// </remarks>
    public static IReadOnlySet<Type> Cascaded { get; } = new HashSet<Type>
    {
        typeof(JobAd),
        typeof(RecentJobSearch),
    };

    /// <summary>
    /// Surfaces that CAN hold the recruiter's identifier and are deliberately NOT erased. Each
    /// carries its ground, and each is <b>disclosed</b> — the erasure response reports them as
    /// matched-but-not-erased, the runbook's reply template says so, and the DPIA says so. We do
    /// not claim to have erased what we have not erased. That is #842, applied to ourselves.
    /// </summary>
    public static IReadOnlyDictionary<Type, string> MatchedButNotErased { get; } =
        new Dictionary<Type, string>
        {
            [typeof(SavedSearch)] =
                "Art. 21(1) reaches only processing based on 6(1)(e)/(f). A saved search is "
                + "processed under 6(1)(b) — our contract with the USER — so the recruiter's "
                + "objection never fires against it, and the 17(1)(c) erasure ground that would "
                + "flow from a successful objection never arises. (This is a DIFFERENT mechanism "
                + "from the applicant snapshot below: the snapshot is kept DESPITE Art. 17 "
                + "applying; the saved search is kept because the request does not reach that "
                + "processing at all.) The engineering is also decisive on its own: SavedSearch's "
                + "SoftDelete() leaves `criteria` in the row — it hides, it does not erase, and a "
                + "cascade built on it would BE the #842 defect class — while stripping Q from the "
                + "criteria is not universally constructible (SearchCriteria's non-empty invariant "
                + "and RelevanceRequiresQ mean a saved search whose ONLY criterion is the "
                + "recruiter's name cannot have its Q removed — the remedy fails exactly on the "
                + "case the request would be about). Reported in the dry-run; a human decides, "
                + "with the affected user in the loop (CLAUDE.md §5 — a rule engine never rewrites "
                + "silently).",

            [typeof(DomainApplication)] =
                "applications.snapshot_description — the applicant's own frozen record of the ad "
                + "she applied to. ADR 0086 exists precisely so the snapshot outlives the ad; "
                + "nulling it would destroy HER evidence to serve a third party's request. The "
                + "legal ground (Art. 17(3)(e), establishment/exercise/defence of legal claims) is "
                + "Klas's to affirm — STOPP-3, OPEN. Recorded, not omitted. Tier A reaches this "
                + "surface by minimisation instead (new snapshots are captured from an "
                + "already-scrubbed body), which is why this is a residual and not a hole.",
        };

    /// <summary>
    /// Surfaces that structurally CANNOT hold recruiter free text, each with the reason it cannot.
    /// This is the half of the registry that is easy to get lazily wrong, so the reason is
    /// required rather than assumed — "we looked and it was fine" is what the last registry said.
    /// </summary>
    public static IReadOnlyDictionary<Type, string> NoRecruiterTextSurface { get; } =
        new Dictionary<Type, string>
        {
            [typeof(JobSeeker)] = "User-owned profile. No ad text.",
            [typeof(Resume)] = "User-authored CV content. No ad text.",
            [typeof(ParsedResume)] = "Derived from the user's own CV. No ad text.",
            [typeof(ResumeFile)] = "The user's own uploaded file bytes. No ad text.",
            [typeof(SavedJobAd)] = "Join row: (JobSeekerId, JobAdId). Ids only, no prose.",
            [typeof(UserJobAdMatch)] = "Match result: ids, grade, score. No prose.",
            [typeof(CompanyWatch)] = "Follow keyed on org.nr/internal id. No ad text.",
            [typeof(CompanyWatchCriterion)] = "SNI + kommun concept ids. No free text.",
            [typeof(FollowedCompanyAdHit)] = "Notification hit: ids + timestamps. No prose.",
            [typeof(AuditLogEntry)] =
                "The accountability record itself. It holds the erasure request's HMAC "
                + "pseudonym, never the identifier — and it is EXCLUDED from the cascade by "
                + "design: erasing the record of an erasure would destroy the Art. 5(2) evidence "
                + "that we honoured the request. Art. 17(3)(b)/(e) (legal obligation / legal "
                + "claims) is the ground. Retention is ADR 0024's.",
        };
}
