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
/// What an Art. 17 erasure of a recruiter does to a given persisted column (#842).
/// </summary>
public enum ErasureColumnDisposition
{
    /// <summary>Destroyed by the erasure. Provable, no detector involved.</summary>
    Erased,

    /// <summary>
    /// Searched and REPORTED; a human erases it. Her right applies — this is a mechanism choice,
    /// never a refusal, and the reply must never tell her the right does not reach it.
    /// </summary>
    MatchedHumanErases,

    /// <summary>
    /// Searched and REPORTED, and deliberately retained on a written legal ground (Art. 17(3)(e)).
    /// We search it precisely BECAUSE we do not erase it: a ground asserted over a population we
    /// never counted is a ground asserted over a silence.
    /// </summary>
    MatchedRetained,

    /// <summary>Held only as an HMAC pseudonym (the accountability record itself).</summary>
    Pseudonymised,

    /// <summary>Structurally cannot hold a recruiter's personal data.</summary>
    NotRecruiterData,

    /// <summary>
    /// A DIFFERENT processing with its own basis, its own DPIA and its own erasure route - not
    /// our copy of the ad. An Art. 17 request about an imported ad does not reach it. Recorded
    /// here rather than omitted, so nobody has to rediscover that.
    /// </summary>
    SeparateProcessing,
}

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
    /// The surfaces the erasure RESPONSE reports counts for — i.e. what we tell the data subject we
    /// looked at. Pinned against <c>ErasureSurfaceCounts</c>'s members by
    /// <c>ErasureCascadeRegistryTests</c>: a surface the registry reasons about but the response
    /// does not report would be something we erased (or knowingly kept) without telling her.
    /// </summary>
    /// <remarks>
    /// <c>Application</c> is absent deliberately: <c>snapshot_description</c> lives inside the
    /// applicant's own aggregate and is disclosed in the reply template and the DPIA (STOPP-3), not
    /// as a per-surface count of ads.
    /// </remarks>
    public static IReadOnlySet<string> ReportedSurfaces { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "JobAds",
        "RecentJobSearches",
        "SavedSearches",
        "ApplicationSnapshots",
        "UserAuthoredText",
    };

    /// <summary>
    /// Every persisted COLUMN that can hold recruiter free text, keyed <c>table.column</c>.
    /// <c>ErasureCascadeRegistryTests</c> drives this from the EF model and breaks the build on any
    /// unclassified text/jsonb column.
    /// </summary>
    /// <remarks>
    /// <b>Why column granularity, and why the aggregate-level version was a false assurance.</b> The
    /// first cut of this registry classified <c>DbSet</c>s. It therefore could not have caught either
    /// of the two real holes in this very PR: <c>job_ads.company_name</c> (an enskild firma's company
    /// name IS a person's name) and <c>applications.snapshot_company</c> (non-nullable, so populated
    /// on EVERY application). Both sit inside aggregates the registry had already ticked off as
    /// classified. <b>A guard one level coarser than its own defect class does not merely miss — it
    /// reassures.</b> That is the mechanism by which ADR 0024's registry stayed wrong for two
    /// releases while an auditor reading it concluded we were compliant.
    /// </remarks>
    public static IReadOnlyDictionary<string, ErasureColumnDisposition> Columns { get; } =
        new Dictionary<string, ErasureColumnDisposition>(StringComparer.Ordinal)
        {
            // ── job_ads: ERASED, whole-record ────────────────────────────────────────────────
            ["job_ads.title"] = ErasureColumnDisposition.Erased,
            ["job_ads.description"] = ErasureColumnDisposition.Erased,
            ["job_ads.company_name"] = ErasureColumnDisposition.Erased,
            ["job_ads.url"] = ErasureColumnDisposition.Erased,
            ["job_ads.raw_payload"] = ErasureColumnDisposition.Erased,
            ["job_ads.extracted_terms"] = ErasureColumnDisposition.Erased,
            ["job_ads.extracted_lexemes"] = ErasureColumnDisposition.Erased,
            ["job_ads.search_vector"] = ErasureColumnDisposition.Erased,
            ["job_ads.organization_number"] = ErasureColumnDisposition.Erased,
            ["job_ads.external_id"] = ErasureColumnDisposition.NotRecruiterData,
            ["job_ads.external_source"] = ErasureColumnDisposition.NotRecruiterData,
            ["job_ads.source"] = ErasureColumnDisposition.NotRecruiterData,
            ["job_ads.status"] = ErasureColumnDisposition.NotRecruiterData,

            // ── recent_job_searches: ERASED (hard-delete of the row) ─────────────────────────
            ["recent_job_searches.q"] = ErasureColumnDisposition.Erased,

            // ── saved_searches: MATCHED, a HUMAN erases ─────────────────────────────────────
            ["saved_searches.criteria"] = ErasureColumnDisposition.MatchedHumanErases,
            ["saved_searches.name"] = ErasureColumnDisposition.MatchedHumanErases,
            ["recent_job_searches.filter_hash"] = ErasureColumnDisposition.Erased,

            // -- user-authored free text: MATCHED, a HUMAN erases -----------------------------
            // A user can absolutely have written "Ringde Magnus Fagerberg" in her own note. That
            // is the recruiter's personal data, sitting in a place nobody enumerated until this
            // registry was driven from the EF model instead of from memory.
            ["applications.cover_letter"] = ErasureColumnDisposition.MatchedHumanErases,
            ["applications.manual_company"] = ErasureColumnDisposition.MatchedHumanErases,
            ["applications.manual_title"] = ErasureColumnDisposition.MatchedHumanErases,
            ["applications.manual_url"] = ErasureColumnDisposition.NotRecruiterData,
            ["application_notes.content"] = ErasureColumnDisposition.MatchedHumanErases,
            ["follow_ups.note"] = ErasureColumnDisposition.MatchedHumanErases,

            // ── applications: MATCHED, RETAINED under Art. 17(3)(e) ─────────────────────────
            ["applications.snapshot_company"] = ErasureColumnDisposition.MatchedRetained,
            ["applications.snapshot_description"] = ErasureColumnDisposition.MatchedRetained,
            ["applications.snapshot_title"] = ErasureColumnDisposition.MatchedRetained,
            ["applications.snapshot_url"] = ErasureColumnDisposition.MatchedRetained,
            ["applications.snapshot_source"] = ErasureColumnDisposition.NotRecruiterData,
            ["applications.snapshot_municipality_concept_id"] = ErasureColumnDisposition.NotRecruiterData,

            // ── audit_log: the accountability record ────────────────────────────────────────
            ["audit_log.payload"] = ErasureColumnDisposition.Pseudonymised,
            ["audit_log.event_type"] = ErasureColumnDisposition.NotRecruiterData,
            ["audit_log.aggregate_type"] = ErasureColumnDisposition.NotRecruiterData,
            ["audit_log.ip_address"] = ErasureColumnDisposition.NotRecruiterData,
            ["audit_log.user_agent"] = ErasureColumnDisposition.NotRecruiterData,

            // ── SCB company register: a SEPARATE processing ─────────────────────────────────
            ["company_register.company_name"] = ErasureColumnDisposition.SeparateProcessing,
            ["company_register.organization_number"] = ErasureColumnDisposition.SeparateProcessing,
            ["company_register.sate_kommun_code"] = ErasureColumnDisposition.NotRecruiterData,
            ["company_register.sate_kommun_name"] = ErasureColumnDisposition.NotRecruiterData,
            ["company_register.scb_status_raw"] = ErasureColumnDisposition.NotRecruiterData,

            // ── ids, concept codes, hashes, encrypted user content: no recruiter free text ──
            ["company_watch_criteria.label"] = ErasureColumnDisposition.NotRecruiterData,
            ["job_ad_snapshot_misses.external_id"] = ErasureColumnDisposition.NotRecruiterData,
            ["job_ad_snapshot_misses.source"] = ErasureColumnDisposition.NotRecruiterData,
            ["job_ads.ssyk_concept_id"] = ErasureColumnDisposition.NotRecruiterData,
            ["job_ads.region_concept_id"] = ErasureColumnDisposition.NotRecruiterData,
            ["job_ads.municipality_concept_id"] = ErasureColumnDisposition.NotRecruiterData,
            ["job_ads.occupation_group_concept_id"] = ErasureColumnDisposition.NotRecruiterData,
            ["job_ads.employment_type_concept_id"] = ErasureColumnDisposition.NotRecruiterData,
            ["job_ads.worktime_extent_concept_id"] = ErasureColumnDisposition.NotRecruiterData,
            ["resume_finding_statuses.criterion_id"] = ErasureColumnDisposition.NotRecruiterData,
            ["resume_finding_statuses.rubric_version"] = ErasureColumnDisposition.NotRecruiterData,
            ["resume_finding_statuses.target_fingerprint"] = ErasureColumnDisposition.NotRecruiterData,
            ["resume_versions.content"] = ErasureColumnDisposition.NotRecruiterData,
            ["resume_versions.content_enc"] = ErasureColumnDisposition.NotRecruiterData,
            ["taxonomy_concepts.concept_id"] = ErasureColumnDisposition.NotRecruiterData,
            ["taxonomy_concepts.label"] = ErasureColumnDisposition.NotRecruiterData,
            ["taxonomy_concepts.parent_concept_id"] = ErasureColumnDisposition.NotRecruiterData,
            ["taxonomy_relations.source_concept_id"] = ErasureColumnDisposition.NotRecruiterData,
            ["taxonomy_relations.related_concept_id"] = ErasureColumnDisposition.NotRecruiterData,
        };

    /// <summary>
    /// The written ground for every surface we search and do NOT erase. Required by
    /// <c>ErasureCascadeRegistryTests</c> — a blank ground is a ground nobody thought about, and
    /// each of these is something we will have to say out loud to a person who asked us to delete
    /// her data.
    /// </summary>
    public static IReadOnlyDictionary<string, string> WrittenGrounds { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["saved_searches"] =
                "HER RIGHT APPLIES. One row carries two data subjects under two bases: the "
                + "JobSeeker's own criteria rest on Art. 6(1)(b) (our contract with HER), but the "
                + "RECRUITER'S NAME sitting inside those criteria does not — Art. 6(1)(b) requires "
                + "the data subject to be a PARTY to the contract, and the recruiter is party to "
                + "nothing. That processing rests on Art. 6(1)(f), which Art. 21(1) reaches. Her "
                + "objection fires and Art. 17(1)(c) is available. We do NOT attempt the "
                + "'compelling legitimate grounds' override: keeping her name in another user's "
                + "filter is a convenience, and a saved search is recreatable in seconds. "
                + "WE OWE HER ERASURE AND WE HONOUR IT IN FULL. What we do not do is AUTOMATE it: "
                + "SavedSearch.SoftDelete() leaves `criteria` in the row (it hides, it does not "
                + "erase — a cascade built on it would BE the #842 defect class), and stripping the "
                + "term is not universally constructible (SearchCriteria's non-empty invariant + "
                + "RelevanceRequiresQ mean a saved search whose ONLY criterion is her name cannot "
                + "have it removed). So a HUMAN does it, inside the Art. 12(3) month, with the "
                + "affected user in the loop. That is a MECHANISM choice, never a refusal — and the "
                + "reply must never tell her the right does not reach it. 'Our code cannot do it' "
                + "has never been a defence. That IS #842.",

            ["application_notes"] =
                "USER-AUTHORED PRIVATE NOTES about her own application - she may well have "
                + "written the recruiter's name in one. That is the RECRUITER'S personal data, "
                + "processed under Art. 6(1)(f), which Art. 21(1) reaches. HER RIGHT APPLIES. We "
                + "SEARCH and REPORT it; a HUMAN erases it, with the affected user in the loop, "
                + "because silently rewriting a user's private note about her own job hunt is not "
                + "something a job may do (CLAUDE.md 5 - a rule engine never rewrites silently). "
                + "This surface was found by driving the registry from the EF model. Nobody had "
                + "enumerated it, and no version of this feature would have searched it.",

            ["follow_ups"] =
                "Same as application_notes: user-authored free text about her own application, "
                + "which can name the recruiter. Her right applies (6(1)(f), reached by Art. "
                + "21(1)); searched, reported, erased by a human.",

            ["company_register"] =
                "A SEPARATE PROCESSING. company_name IS a natural person's name for an enskild "
                + "firma - but this table is a replica of a PUBLIC register (SCB, ADR 0090) with "
                + "its own legal basis, its own DPIA and its own erasure route. It is not our copy "
                + "of an ad, an Art. 17 request about an imported ad does not reach it, and the "
                + "DPIA C-D4 firewall explicitly FORBIDS a handler joining it. An enskild firma "
                + "owner's request against the REGISTER is a different request against a different "
                + "processing, out of scope for #842. Recorded, not omitted.",

            ["applications"] =
                "Art. 17(3)(e) — establishment, exercise or defence of legal claims. The applicant's "
                + "frozen record of an ad she applied to (ADR 0086 exists precisely so the snapshot "
                + "outlives the ad). And the ground is STRONGER for snapshot_company than for the ad "
                + "body, not weaker: a Swedish jobseeker must file an AKTIVITETSRAPPORT to "
                + "Arbetsförmedlingen naming the employer she applied to. The company name is the "
                + "SPINE of her own legal record; the ad body is its colour. That is why "
                + "AdSnapshot.WithoutDescription() can drop the body and could never drop the "
                + "company. We SEARCH it and REPORT it precisely because we do not erase it: a legal "
                + "ground asserted over a population we never counted is a ground asserted over a "
                + "silence, and that is how the last registry stayed wrong. STOPP-3 — Klas affirms.",
        };
}
