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
    /// We HOLD it and we CANNOT search it. The column is encrypted at rest under PER-USER keys —
    /// Form A in-place text, Form B encrypted VO shadows, or Form C sealed binary (ADR 0049
    /// C3/C4 / ADR 0066) — so a plaintext <c>LIKE</c> compares her name against ciphertext and
    /// matches nothing — not "nothing today", but structurally, on every request, forever.
    /// </summary>
    /// <remarks>
    /// Reading these would mean decrypting EVERY user's private texts to serve ONE third party's
    /// request. That is refused on the merits, not on difficulty: the envelope exists precisely so
    /// that no single operation can read everyone's content (Art. 25(2)/32), and we have no lawful
    /// basis toward the other data subjects (Art. 6). <b>We refuse the mechanism, never the
    /// person</b> — a targeted, consented decryption of ONE identified user's application is
    /// proportionate and buildable, and that escalation route is offered in every reply
    /// (<c>UnsearchableSurfaces</c>). Her right reaches these columns; what we decline is the
    /// corpus-wide scan.
    /// <para>
    /// <c>ErasureCascadeRegistryTests</c> cross-checks this against
    /// <c>Infrastructure.Security.EncryptedFieldRegistry</c>: a DEK-encrypted column classified as
    /// SEARCHED (<see cref="MatchedHumanErases"/> / <see cref="MatchedRetained"/>) <b>breaks the
    /// build</b>. The two registries had to be taught about each other, because Form-A encryption
    /// is invisible in the EF model this registry is driven from.
    /// </para>
    /// </remarks>
    HeldButNotSearchable,

    /// <summary>
    /// Searched and REPORTED, and deliberately retained on a written legal ground (Art. 17(3)(e) —
    /// see <see cref="ErasureCascadeRegistry.WrittenGrounds"/>). We search it precisely BECAUSE we
    /// do not erase it: the ground has to be asserted over a population we actually counted.
    /// </summary>
    MatchedRetained,

    /// <summary>Held only as an HMAC pseudonym (the accountability record itself).</summary>
    Pseudonymised,

    /// <summary>
    /// The WRITE PATH cannot put a recruiter's free text here. This is a claim about the write path,
    /// verifiable by reading it — <b>NEVER a claim about likelihood.</b> Exactly two shapes qualify:
    /// <list type="number">
    /// <item><b>CLOSED DOMAIN</b> — the content is drawn from a set the user cannot author into: ids,
    /// concept codes, hashes, enum values, source names, organisation numbers, fingerprints.</item>
    /// <item><b>RETIRED WRITE PATH, POPULATION COUNTED AT ZERO</b> — nothing writes it any more, and
    /// somebody counted the rows (<c>resume_versions.content</c>, nulled at the Form-B cutover:
    /// ADR 0049 Beslut 5 steg 3).</item>
    /// </list>
    /// <para>
    /// A LIVE free-text column is NEVER <c>NotRecruiterData</c>, however unlikely her name is to land
    /// in it. <b>"We judged it unlikely" is not a disposition.</b> A free-text column has exactly two
    /// honest homes: we SEARCH it (<see cref="MatchedHumanErases"/> / <see cref="MatchedRetained"/>),
    /// or we CANNOT search it (<see cref="HeldButNotSearchable"/>, and the reply says so out loud).
    /// </para>
    /// <para>
    /// This bucket makes the STRONGEST claim in the registry, so it is the one that must cost the
    /// most to join: every column here carries a written ground in
    /// <see cref="ErasureCascadeRegistry.WrittenGrounds"/> naming its write-path guarantee, and the
    /// test enforces it.
    /// </para>
    /// </summary>
    NotRecruiterData,

    /// <summary>
    /// A DIFFERENT processing with its own basis, its own DPIA and its own erasure route - not
    /// our copy of the ad. An Art. 17 request about an imported ad does not reach it. Recorded
    /// here rather than omitted, so nobody has to rediscover that.
    /// </summary>
    SeparateProcessing,
}

/// <summary>
/// One search channel of the Art. 17 match — the registry-side declaration that DRIVES the port
/// (#842 round 6). <paramref name="Surface"/> is the ONE name (a member of
/// <see cref="ErasureCascadeRegistry.ReportedSurfaces"/> and of <c>ErasureSurfaceCounts</c>);
/// <paramref name="Columns"/> are the <c>table.column</c> keys the channel's SQL claims to search;
/// <paramref name="PortMethod"/> is the <c>IRecruiterErasureMatchQuery</c> method that runs it.
/// </summary>
public sealed record ErasureChannel(
    string Surface,
    IReadOnlyList<string> Columns,
    string PortMethod);

/// <summary>
/// The Art. 17 cascade registry for recruiter PII (#842) — every persisted surface, classified,
/// with a written reason. <c>ErasureCascadeRegistryTests</c> pins it: a new <c>DbSet</c> on
/// <c>IAppDbContext</c> that appears in neither of the two sets (<see cref="Columns"/>, the
/// wholesale-exclusion list in the tests) <b>breaks the build</b>.
/// </summary>
/// <remarks>
/// <b>Why this type exists, and must not become prose again.</b> ADR 0024's cascade registry listed
/// <c>raw_payload</c> and nothing else — not <c>job_ads.description</c>, where the address actually
/// was. It was prose in a document, so it went stale silently. A registry a reviewer has to remember
/// to update is not a registry. This one is a type the compiler and a test enforce, and the erasure
/// command's own response is generated from it — so the thing we <i>tell the data subject</i> and
/// the thing we <i>actually do</i> are derived from one source.
/// <para>
/// <b>The count is not the control.</b> Do not key a classification to how many rows a table
/// happens to hold: <c>recent_job_searches</c> held one row and <c>saved_searches</c> zero when this
/// was written. The cascade ships on an empty table and reports 0 truthfully.
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
    /// The applicant's frozen ad snapshot IS one of these counts (<c>ApplicationSnapshots</c> —
    /// <c>snapshot_company/_title/_description/_url</c>, searched and retained under Art. 17(3)(e)).
    /// An earlier remark here claimed the opposite; the registry's own channel list is the truth.
    /// </remarks>
    public static IReadOnlySet<string> ReportedSurfaces { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "JobAds",
        "RecentJobSearches",
        "SavedSearches",
        "ApplicationSnapshots",
        "ManualAdEntries",
        "CompanyWatchCriteria",
        "ResumeMetadata",
        "ApplicationsReferencingMatchedAds",
    };

    /// <summary>
    /// <b>The registry DRIVES the port (round 5, both reviewers).</b> One channel per reported
    /// surface: the surface's ONE name, the columns its port method's SQL claims to search, and
    /// the port method that runs the search. Adding a searched column is now ONE edit here —
    /// <c>ErasureCascadeRegistryTests</c> breaks the build until the column belongs to a channel,
    /// the channel names a real port method, and the channel set equals
    /// <see cref="ReportedSurfaces"/>.
    /// </summary>
    /// <remarks>
    /// <b>What this pin CANNOT reach, said out loud:</b> no reflection can prove that a port
    /// method's SQL body actually touches the columns its channel claims. The claim is pinned
    /// here; the QUERY is pinned by <c>RecruiterErasureIngestTests</c>, which seeds one row per
    /// channel column whose identifier lives in THAT COLUMN ALONE and requires a non-zero match.
    /// Five rounds of this issue were the gap between those two pins.
    /// <para>
    /// <c>ApplicationsReferencingMatchedAds</c> has an empty column list deliberately: it is the
    /// structural FK channel (<c>applications.job_ad_id</c>), not a text search — there is no text
    /// column for it to claim.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ErasureChannel> Channels { get; } =
    [
        new("JobAds",
            [
                "job_ads.title",
                "job_ads.description",
                "job_ads.company_name",
                "job_ads.raw_payload",
                "job_ads.search_vector",
                "job_ads.organization_number",
            ],
            nameof(Abstractions.IRecruiterErasureMatchQuery.FindJobAdsAsync)),

        new("RecentJobSearches",
            [
                "recent_job_searches.q",
                "recent_job_searches.employer_list",
            ],
            nameof(Abstractions.IRecruiterErasureMatchQuery.FindRecentJobSearchesAsync)),

        new("SavedSearches",
            [
                "saved_searches.criteria",
                "saved_searches.name",
            ],
            nameof(Abstractions.IRecruiterErasureMatchQuery.CountSavedSearchesAsync)),

        new("ApplicationSnapshots",
            [
                "applications.snapshot_company",
                "applications.snapshot_title",
                "applications.snapshot_description",
                "applications.snapshot_url",
            ],
            nameof(Abstractions.IRecruiterErasureMatchQuery.CountApplicationSnapshotsAsync)),

        new("ManualAdEntries",
            [
                "applications.manual_company",
                "applications.manual_title",
                "applications.manual_url",
            ],
            nameof(Abstractions.IRecruiterErasureMatchQuery.CountManualAdEntriesAsync)),

        new("CompanyWatchCriteria",
            [
                "company_watch_criteria.label",
            ],
            nameof(Abstractions.IRecruiterErasureMatchQuery.CountCompanyWatchCriteriaAsync)),

        new("ResumeMetadata",
            [
                "parsed_resumes.source_file_name",
                "resume_files.file_name",
                "resumes.name",
                "resumes.latest_role",
                "resumes.top_skills",
            ],
            nameof(Abstractions.IRecruiterErasureMatchQuery.CountResumeMetadataAsync)),

        new("ApplicationsReferencingMatchedAds",
            [],
            nameof(Abstractions.IRecruiterErasureMatchQuery.CountApplicationsReferencingAsync)),
    ];

    /// <summary>
    /// The <see cref="ErasureColumnDisposition.Erased"/> columns that have NO search channel of
    /// their own, each with the written derivation that makes that safe — keyed
    /// <c>table.column</c>. <c>ErasureCascadeRegistryTests</c> requires every Erased column to be
    /// EITHER in a channel's column list OR here: there is no third state, so the next column that
    /// enters the Erased bucket cannot slip through silently (round-5 security m2 — the Minor that
    /// predicted round 5's Blocker one round in advance).
    /// </summary>
    /// <remarks>
    /// An <c>Erased</c> column dies with its carrier when the MATCH finds the carrier — so a
    /// column here is safe only if a row whose SOLE occurrence of her identifier is this column
    /// cannot exist, and the ground must say WHY against the write path. "It is erased anyway" is
    /// not a ground; that was round 5's Blocker.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> ErasedWithoutSearchChannel { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["job_ads.url"] =
                "DERIVATION: erased with the record; matched via the ad's searched channels. The "
                + "write path is JobAd.Import/UpdateFromSource taking webpage_url from the ingest "
                + "funnel, and the Platsbanken form is id-shaped "
                + "(arbetsformedlingen.se/platsbanken/annonser/<id>) — it cannot carry a name. The "
                + "RESIDUAL is an upstream form change that embeds free text in the URL; it is "
                + "accepted and recorded here (ADR 0106 D9) rather than certified away: a row whose "
                + "ONLY carrier of her name is the URL would be missed by a name request. The "
                + "structured org.nr identifier does not reach URLs either — an org.nr in a URL is "
                + "not a written form the identifier normaliser admits.",

            ["job_ads.extracted_terms"] =
                "DERIVATION: extracted_terms is C#-written FROM title + description "
                + "(JobAdKeywordExtractor — Display/MatchedOn are surface forms recovered from the "
                + "ad's own text, or taxonomy labels). A lexeme cannot carry text absent from its "
                + "source, and both sources are searched channels. Erased explicitly by Erase() "
                + "(ExtractedTerms.Empty) because it does NOT self-heal on a description write.",

            ["job_ads.extracted_lexemes"] =
                "DERIVATION: the STORED shadow of extracted_terms — same source text, same "
                + "argument, one storage form over. Follows extracted_terms to [] on Erase().",

            ["recent_job_searches.filter_hash"] =
                "DERIVATION: filter_hash is derived from the criteria (q + employer_list + the "
                + "closed-domain concept-id lists) by RecentJobSearch.Capture and 'får aldrig "
                + "divergera' from them. Both free-text carriers in the derivation (q, "
                + "employer_list) are searched channels; a hash is one-way and cannot be searched "
                + "for an identifier. The row is hard-deleted when either carrier matches.",
        };

    /// <summary>
    /// Every persisted COLUMN that can hold recruiter free text, keyed <c>table.column</c>.
    /// <c>ErasureCascadeRegistryTests</c> drives this from the EF model and breaks the build on any
    /// unclassified text/jsonb column.
    /// </summary>
    /// <remarks>
    /// <b>COLUMN granularity, never <c>DbSet</c> granularity.</b> A per-aggregate registry cannot see
    /// the two holes that mattered here: <c>job_ads.company_name</c> (an enskild firma's company name
    /// IS a person's name) and <c>applications.snapshot_company</c> (non-nullable, so populated on
    /// EVERY application). Both sit inside aggregates a coarser registry would already have ticked
    /// off as classified.
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

            // employer_list holds 10-DIGIT ORG.NR — the write path is ValidateEmployerList →
            // OrganizationNumber.Create (^[0-9]{10}\z); an employer NAME cannot be written here.
            // (Round 5 classified it on the column's NAME — "employer sounds like names" — the
            // fifth ground in this issue written against something other than the mapping.)
            // A sole trader's org.nr IS her personnummer — the identical datum this registry
            // tombstones in job_ads.organization_number — so her right reaches it (Art. 4(1),
            // 6(1)(f) → 21(1)), and the CTO ruling (2026-07-14-842-pr2-employer-list-cto.md)
            // makes org.nr a first-class Art. 17 identifier form: normalised in Domain
            // (OrganizationNumber.TryFromWrittenForm) and matched EXACTLY against this column.
            // The row is hard-deleted when matched — by the SQL-returned ids, never a filtered
            // projection (round 5's second defect: `.Where(r => r.Q is not null)` threw away the
            // employer-only match, the canonical q = NULL form, after the SQL had found it).
            ["recent_job_searches.employer_list"] = ErasureColumnDisposition.Erased,

            // Derived from the criteria; hard-deleted with the row (see ErasedWithoutSearchChannel).
            ["recent_job_searches.filter_hash"] = ErasureColumnDisposition.Erased,

            // The other five list columns are Arbetsförmedlingen taxonomy concept ids.
            ["recent_job_searches.occupation_group_list"] = ErasureColumnDisposition.NotRecruiterData,
            ["recent_job_searches.municipality_list"] = ErasureColumnDisposition.NotRecruiterData,
            ["recent_job_searches.region_list"] = ErasureColumnDisposition.NotRecruiterData,
            ["recent_job_searches.employment_type_list"] = ErasureColumnDisposition.NotRecruiterData,
            ["recent_job_searches.worktime_extent_list"] = ErasureColumnDisposition.NotRecruiterData,

            ["company_watch_criteria.sni_codes"] = ErasureColumnDisposition.NotRecruiterData,
            ["company_watch_criteria.kommun_codes"] = ErasureColumnDisposition.NotRecruiterData,
            ["company_register.sni_codes"] = ErasureColumnDisposition.NotRecruiterData,
            ["resumes.reviewed_rubric_version"] = ErasureColumnDisposition.NotRecruiterData,

            // ── saved_searches: MATCHED, a HUMAN erases ─────────────────────────────────────
            ["saved_searches.criteria"] = ErasureColumnDisposition.MatchedHumanErases,
            ["saved_searches.name"] = ErasureColumnDisposition.MatchedHumanErases,

            // ── user-authored, PLAINTEXT: searched, a HUMAN erases ───────────────────────────
            // What a user typed for an application she tracks outside Platsbanken. manual_url is a
            // 2000-char pasted string with NO validation at the persistence boundary — it is free
            // text with a max length, and a URL path carries names routinely.
            ["applications.manual_company"] = ErasureColumnDisposition.MatchedHumanErases,
            ["applications.manual_title"] = ErasureColumnDisposition.MatchedHumanErases,
            ["applications.manual_url"] = ErasureColumnDisposition.MatchedHumanErases,

            // ── company_watch_criteria: user-authored label, searched, a HUMAN erases ────────
            // 120 chars, no content validation. A criterion is a PREDICATE over SNI + kommun codes,
            // so a recruiter's name is an unlikely thing to type here — but unlikely is not a
            // disposition, and this column sat in NotRecruiterData ("structurally cannot") while the
            // write path accepted arbitrary text.
            ["company_watch_criteria.label"] = ErasureColumnDisposition.MatchedHumanErases,

            // ── user-authored, DEK-ENCRYPTED: held, and NOT searchable ───────────────────────
            // Form A (ADR 0049 C3 / 0066) — the column carries `v1:<base64>` at rest, sealed under
            // the OWNING USER'S key. Cross-checked against EncryptedFieldRegistry by
            // ErasureCascadeRegistryTests: classifying one of these as searched breaks the build.
            ["applications.cover_letter"] = ErasureColumnDisposition.HeldButNotSearchable,
            ["application_notes.content"] = ErasureColumnDisposition.HeldButNotSearchable,
            ["follow_ups.note"] = ErasureColumnDisposition.HeldButNotSearchable,

            // ── applications: MATCHED, RETAINED under Art. 17(3)(e) ─────────────────────────
            ["applications.snapshot_company"] = ErasureColumnDisposition.MatchedRetained,
            ["applications.snapshot_description"] = ErasureColumnDisposition.MatchedRetained,
            ["applications.snapshot_title"] = ErasureColumnDisposition.MatchedRetained,
            ["applications.snapshot_url"] = ErasureColumnDisposition.MatchedRetained,
            ["applications.snapshot_source"] = ErasureColumnDisposition.NotRecruiterData,
            ["applications.snapshot_municipality_concept_id"] = ErasureColumnDisposition.NotRecruiterData,
            ["applications.status"] = ErasureColumnDisposition.NotRecruiterData,

            // ── application_status_changes: the status timeline (append-only enum pairs) ────
            ["application_status_changes.from_status"] = ErasureColumnDisposition.NotRecruiterData,
            ["application_status_changes.to_status"] = ErasureColumnDisposition.NotRecruiterData,

            // ── follow_ups: the enum plumbing around the encrypted note ──────────────────────
            ["follow_ups.channel"] = ErasureColumnDisposition.NotRecruiterData,
            ["follow_ups.outcome"] = ErasureColumnDisposition.NotRecruiterData,

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
            ["company_register.status"] = ErasureColumnDisposition.NotRecruiterData,

            // ── resume_versions: one retired plaintext column, one Form-B ciphertext column ──
            // content: the write path is GONE (Form-B cutover, ADR 0049 Beslut 5 steg 3) and the
            // population was COUNTED at zero before the mapping flip. That is what earns
            // NotRecruiterData — a checkable claim about the write path, not a guess.
            // content_enc: Form B — the VO is JSON-serialised and DEK-encrypted into this shadow. We
            // HOLD user content here and cannot read it. It is NOT NotRecruiterData: that word would
            // claim we hold nothing of hers, when the truth is we cannot look.
            // ── parsed_resumes + resume_files: the CV, and the columns that nearly escaped ───
            // raw_text (Form A) and parsed_content_enc (Form B) are DEK-encrypted under the owning
            // user's key and hold the SAME content as resume_versions.content_enc — which this
            // registry already calls HeldButNotSearchable because "a CV can name a recruiter she
            // wrote to". Both tables were excluded WHOLESALE, one level above this registry, by a
            // list claiming its tables are "structurally incapable of holding recruiter free text".
            // About the tables holding the raw CV text and the CV file.
            ["parsed_resumes.raw_text"] = ErasureColumnDisposition.HeldButNotSearchable,
            ["parsed_resumes.parsed_content_enc"] = ErasureColumnDisposition.HeldButNotSearchable,
            ["resume_files.content"] = ErasureColumnDisposition.HeldButNotSearchable,

            // ── CV METADATA: plaintext, user-authored, and SEARCHED ─────────────────────────
            // The FILE NAME is free text the user typed, and the repo already MASKS personnummer out
            // of it (#465) — a guard bolted on precisely because users put arbitrary text into
            // filenames. Both tables, because searching one and not the identical column a table
            // over would be the same defect restated.
            ["parsed_resumes.source_file_name"] = ErasureColumnDisposition.MatchedHumanErases,
            ["resume_files.file_name"] = ErasureColumnDisposition.MatchedHumanErases,

            // resumes.name is the CV's own name, typed by her via Rename(). It is the SAME datum in
            // the SAME form as saved_searches.name — which this registry already SEARCHES, on the
            // ground that "a user who names a saved search 'Anna Karlssons annonser' holds the
            // recruiter's name in it". A CV named "CV – Skill Rekrytering" is the identical case.
            // latest_role and top_skills are denormalised projections of her CV content.
            ["resumes.name"] = ErasureColumnDisposition.MatchedHumanErases,
            ["resumes.latest_role"] = ErasureColumnDisposition.MatchedHumanErases,
            ["resumes.top_skills"] = ErasureColumnDisposition.MatchedHumanErases,

            ["parsed_resumes.source_content_type"] = ErasureColumnDisposition.NotRecruiterData,
            ["resume_files.content_type"] = ErasureColumnDisposition.NotRecruiterData,
            ["resumes.language"] = ErasureColumnDisposition.NotRecruiterData,

            // ── parsed_resumes: the converter-mapped jsonb metadata the FORM sweep exposed ──
            // Each write path is re-derived in the written ground. The scan outcome NEVER stores
            // the personnummer itself; the proposals carry TAXONOMY labels, never the CV's words;
            // the confidence evidence is fixed literals or heading-lexicon keys.
            ["parsed_resumes.status"] = ErasureColumnDisposition.NotRecruiterData,
            ["parsed_resumes.parse_confidence"] = ErasureColumnDisposition.NotRecruiterData,
            ["parsed_resumes.personnummer_scan"] = ErasureColumnDisposition.NotRecruiterData,
            ["parsed_resumes.layout_metrics"] = ErasureColumnDisposition.NotRecruiterData,
            ["parsed_resumes.gap_summary"] = ErasureColumnDisposition.NotRecruiterData,
            ["parsed_resumes.occupation_proposals"] = ErasureColumnDisposition.NotRecruiterData,
            ["parsed_resumes.skill_proposals"] = ErasureColumnDisposition.NotRecruiterData,

            // ── resumes: the SmartEnum template options + origin (stored by Name) ────────────
            ["resumes.origin"] = ErasureColumnDisposition.NotRecruiterData,
            ["resumes.template"] = ErasureColumnDisposition.NotRecruiterData,
            ["resumes.template_accent"] = ErasureColumnDisposition.NotRecruiterData,
            ["resumes.template_font"] = ErasureColumnDisposition.NotRecruiterData,
            ["resumes.template_density"] = ErasureColumnDisposition.NotRecruiterData,
            ["resumes.template_photo_shape"] = ErasureColumnDisposition.NotRecruiterData,

            ["resume_versions.content"] = ErasureColumnDisposition.NotRecruiterData,
            ["resume_versions.kind"] = ErasureColumnDisposition.NotRecruiterData,
            ["resume_versions.content_enc"] = ErasureColumnDisposition.HeldButNotSearchable,

            // ── ids, concept codes, hashes: CLOSED DOMAINS, no user write path ──────────────
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
            ["resume_finding_statuses.status"] = ErasureColumnDisposition.NotRecruiterData,
            ["taxonomy_concepts.concept_id"] = ErasureColumnDisposition.NotRecruiterData,
            ["taxonomy_concepts.label"] = ErasureColumnDisposition.NotRecruiterData,
            ["taxonomy_concepts.parent_concept_id"] = ErasureColumnDisposition.NotRecruiterData,
            ["taxonomy_concepts.kind"] = ErasureColumnDisposition.NotRecruiterData,
            ["taxonomy_relations.source_concept_id"] = ErasureColumnDisposition.NotRecruiterData,
            ["taxonomy_relations.related_concept_id"] = ErasureColumnDisposition.NotRecruiterData,
            ["taxonomy_relations.kind"] = ErasureColumnDisposition.NotRecruiterData,
        };

    /// <summary>
    /// Every column we HOLD but cannot search — derived from <see cref="Columns"/>, never
    /// hand-maintained. <see cref="EraseRecruiterAdsResponse.CouldNotSearch"/> is built from this,
    /// and it is a REQUIRED member of every response.
    /// </summary>
    /// <remarks>
    /// This is the mechanism that makes <i>"we hold no data matching this identifier"</i>
    /// <b>unconstructible</b> — a sentence we could never truthfully mean while these columns exist.
    /// The strongest available outcome word is <c>NoMatchInSearchableSurfaces</c>, and this list
    /// rides along with it on every reply, so it is structurally impossible to answer a data subject
    /// without naming what we did not look at.
    /// </remarks>
    public static IReadOnlyList<string> UnsearchableColumns { get; } =
    [
        .. Columns
            .Where(kv => kv.Value == ErasureColumnDisposition.HeldButNotSearchable)
            .Select(kv => kv.Key)
            .Order(StringComparer.Ordinal),
    ];

    /// <summary>
    /// The written ground for every <c>(table, disposition)</c> pair in <see cref="Columns"/>, keyed
    /// <c>"table:Disposition"</c>. Required by <c>ErasureCascadeRegistryTests</c> for
    /// <see cref="ErasureColumnDisposition.MatchedRetained"/>,
    /// <see cref="ErasureColumnDisposition.MatchedHumanErases"/>,
    /// <see cref="ErasureColumnDisposition.HeldButNotSearchable"/> <b>and
    /// <see cref="ErasureColumnDisposition.NotRecruiterData"/></b>.
    /// </summary>
    /// <remarks>
    /// <b>Why <c>NotRecruiterData</c> needs a ground.</b> It makes the STRONGEST claim in the
    /// registry and must not be the cheapest to enter. <c>Erased</c> costs a query;
    /// <c>MatchedHumanErases</c> costs a channel, a surface and a count; <c>MatchedRetained</c> costs
    /// a written ground. Left exempt, <c>NotRecruiterData</c> would cost a dictionary entry — the
    /// verdict with the highest burden of proof would have the lowest cost of entry, and that is
    /// where the awkward columns go.
    /// <para>
    /// <b>Why the key carries the disposition, not just the table.</b> One table holds several
    /// verdicts — <c>applications</c> alone carries four (<c>MatchedRetained</c> on the snapshots,
    /// <c>MatchedHumanErases</c> on the manual columns, <c>HeldButNotSearchable</c> on
    /// <c>cover_letter</c>, <c>NotRecruiterData</c> on <c>snapshot_source</c>). A table-keyed ground
    /// would be one vague paragraph standing in for four different legal verdicts.
    /// </para>
    /// <para>
    /// <c>Erased</c> and <c>Pseudonymised</c> need no ground: an erasure needs no excuse, and the
    /// HMAC has its own Art. 30 entry.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, string> WrittenGrounds { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["saved_searches:MatchedHumanErases"] =
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

            ["company_watch_criteria:MatchedHumanErases"] =
                "USER-AUTHORED FREE TEXT: 120 chars, no content validation (NormalizeLabel only "
                + "trims and length-caps). A criterion is a PREDICATE over SNI + kommun codes, so a "
                + "recruiter's name is an unlikely thing to type there - but UNLIKELY IS NOT A "
                + "DISPOSITION. This column sat in NotRecruiterData ('structurally cannot hold a "
                + "recruiter's personal data') and that word was simply FALSE: the write path "
                + "accepts arbitrary text. Her right reaches it (Art. 6(1)(f), which Art. 21(1) "
                + "reaches). We SEARCH it and REPORT it; a HUMAN erases it. Unlike saved_searches "
                + "the remedy is ALWAYS constructible and LOSSLESS: the label is optional and "
                + "nullable, so UpdateLabel(null) leaves the criterion intact and watching exactly "
                + "what it watched - it IS its codes, and the UI derives a display label from them. "
                + "One branch, no rights collision, nothing of the user's destroyed.",

            ["application_notes:HeldButNotSearchable"] =
                "HELD, AND WE CANNOT SEARCH IT. application_notes.content is encrypted at rest "
                + "under a PER-USER DEK envelope (ADR 0049 C3 / 0066, Form A) - the column holds "
                + "'v1:<base64>', so a plaintext LIKE for her name matches nothing, structurally, "
                + "on every request, forever. An earlier cut of this feature ran exactly that LIKE "
                + "and reported the 0 it produced as a search result. "
                + "HER RIGHT STILL APPLIES: a user may well have written 'Ringde Magnus "
                + "Fagerberg' in her own note, and that is the RECRUITER'S personal data under "
                + "Art. 6(1)(f), which Art. 21(1) reaches. What we decline is the MECHANISM, not "
                + "the person. Scanning would mean decrypting EVERY user's private texts to serve "
                + "ONE third party's request - it would not use an exception to the envelope, it "
                + "would BUILD the capability permanently (Art. 25(2)/32), and we have no lawful "
                + "basis toward those other data subjects (Art. 6). We would breach a thousand "
                + "people's Art. 6 to honour one person's Art. 17. "
                + "WHAT WE DO INSTEAD: (1) the STRUCTURAL channel - applications.job_ad_id names "
                + "every application written TO a matched ad, exactly, with zero decryption, and "
                + "that is where the overlap actually lives ('Hej Magnus,' sits in the cover "
                + "letter written to Magnus's ad); (2) we DISCLOSE the limit in every reply and "
                + "name this column; (3) we offer the escalation route - if she knows she appears "
                + "in a specific application, a TARGETED decryption of ONE identified user's "
                + "record is proportionate and buildable, and a human does it. A residual remains "
                + "(a note naming her without referencing her ad) and it is disclosed, not hidden.",

            ["follow_ups:HeldButNotSearchable"] =
                "Same as application_notes, same column form, same ground: follow_ups.note is "
                + "DEK-encrypted under the owning user's key (Form A), so it is HELD and NOT "
                + "SEARCHABLE. Her right reaches it (6(1)(f), via Art. 21(1)); we refuse the "
                + "corpus-wide decrypt-and-scan, not her request. The structural job_ad_id channel "
                + "covers the follow-ups attached to a matched ad; the reply discloses the "
                + "residual and gives her the targeted-escalation route.",

            ["parsed_resumes:HeldButNotSearchable"] =
                "THE SAME CV, THE OTHER COPY - and it nearly escaped the registry entirely. raw_text "
                + "is the normalised raw CV text (Form A, encrypted in place); parsed_content_enc is "
                + "the structured parse (Form B, JSON-serialised into an encrypted shadow). Both are "
                + "sealed under the OWNING USER'S DEK, exactly like resume_versions.content_enc, and "
                + "they hold the same thing: her CV. A CV can name a recruiter she wrote to. We HOLD "
                + "it and we CANNOT read it. "
                + "HOW IT NEARLY GOT AWAY: the table was excluded WHOLESALE by a test-side list of "
                + "tables 'structurally incapable of holding recruiter free text' - a claim made "
                + "about the table that holds the raw CV text. And the encrypted-column cross-check "
                + "could not catch it, because it filtered on columns PRESENT in this dictionary: an "
                + "encrypted column that was entirely ABSENT passed the guard against encrypted "
                + "columns. Emptiness was guarded; INCOMPLETENESS was not. Same refusal, same ground, "
                + "same escalation route as the Form-A columns on applications.",

            ["parsed_resumes:MatchedHumanErases"] =
                "USER-AUTHORED PLAINTEXT: source_file_name is the name of the file she uploaded - 400 "
                + "chars, no content validation beyond length. "
                + "AND THE REPO ALREADY KNEW: ParsedResume.Create MASKS personnummer-shaped spans OUT "
                + "of this exact column (#465, Art. 5(1)(c)/25 minimisation) - a guard bolted on "
                + "precisely BECAUSE users put arbitrary text into a filename. Classifying it "
                + "'structurally cannot hold a recruiter's personal data' contradicted a control "
                + "living in the same aggregate. 'Ansokan_Magnus_Fagerberg.pdf' is not exotic. Her "
                + "right reaches it (Art. 6(1)(f) -> Art. 21(1)); it is PLAINTEXT, so "
                + "HeldButNotSearchable would be a lie in the other direction. We SEARCH it and "
                + "REPORT it; a HUMAN erases it, with that user in the loop.",

            ["parsed_resumes:NotRecruiterData"] =
                "Closed domains, each re-derived against its write path. source_content_type is "
                + "the upload's MIME type from a validated allowlist. status is the "
                + "ParsedResumeStatus SmartEnum stored by name. layout_metrics is numbers plus a "
                + "geometry-status enum (CvLayoutMetrics: page count, file size, margin). "
                + "gap_summary is nine booleans (ParsedGapSummary). personnummer_scan is bools, a "
                + "count and a kind enum - the scan outcome NEVER stores the personnummer itself "
                + "(#426). occupation_proposals holds taxonomy concept-ids, taxonomy labels and an "
                + "integer year count - MatchedOn is the LEXICON's occupation-name label "
                + "(OccupationCodeDeriver writes entry.OccupationNameLabel), never the CV's own "
                + "title. skill_proposals holds taxonomy concept-ids and canonical labels "
                + "(SkillTaxonomyIndex resolution; unresolvable names DROP, they are not stored). "
                + "parse_confidence's Evidence strings are fixed literals or heading-lexicon keys "
                + "- TryMatchHeading only matches when the normalised line IS a lexicon entry, so "
                + "the CV's own wording cannot enter. The CV's actual text lives in raw_text / "
                + "parsed_content_enc (HeldButNotSearchable) and source_file_name (searched).",

            ["resume_files:MatchedHumanErases"] =
                "THE SAME UPLOADED FILE, ONE TABLE OVER. file_name is the identical datum as "
                + "parsed_resumes.source_file_name and carries the identical risk. Searching one and "
                + "not the other would be this issue's own defect restated inside its own fix: a "
                + "registry whose verdicts disagree about identical data is worth nothing. Searched, "
                + "reported, erased by a human.",

            ["resumes:MatchedHumanErases"] =
                "THE GROUND THAT EXCLUDED THIS TABLE WAS WRITTEN AGAINST THE AGGREGATE'S DOCSTRING, "
                + "NOT AGAINST ITS MAPPING - and that is the fourth time in this issue. Resume.cs "
                + "says the root 'holds no content', which is true about the CV's BODY and false "
                + "about the ROW: ResumeConfiguration maps name (varchar 200, free text she types via "
                + "Rename(), no content validation), latest_role (varchar 500) and top_skills "
                + "(text[]). "
                + "resumes.name is the SAME DATUM IN THE SAME FORM as saved_searches.name, which this "
                + "registry already SEARCHES on the ground that 'a user who names a saved search "
                + "\"Anna Karlssons annonser\" holds the recruiter's name in it'. A CV named "
                + "'CV - Skill Rekrytering' is the identical case, and a registry whose verdicts "
                + "disagree about identical data is worth nothing. latest_role and top_skills are "
                + "denormalised projections of her CV content, in plaintext. "
                + "Her right reaches all three (Art. 6(1)(f) -> Art. 21(1)). Searched, reported, "
                + "erased by a HUMAN with that user in the loop. "
                + "AN AGGREGATE'S PROSE DESCRIBES WHAT IT IS FOR; THE EF CONFIGURATION DESCRIBES WHAT "
                + "IT HOLDS. Only the second is a fact about the database.",

            ["resumes:NotRecruiterData"] =
                "Closed domains: `language` is a ResumeLanguage enum value written through a value "
                + "converter; `origin` is the ResumeSourceOrigin enum stored by name, set by "
                + "construction and immutable (ADR 0096); reviewed_rubric_version is a versioned "
                + "rubric token minted by the review engine from the knowledge bank; and template / "
                + "template_accent / template_font / template_density / template_photo_shape are "
                + "CvTemplateOptions SmartEnums persisted by Name through FromName converters - a "
                + "value outside the fixed set cannot even materialise. No user write path reaches "
                + "any stored value as free text.",

            ["resume_files:NotRecruiterData"] =
                "Closed domain: content_type is the MIME type from the same validated allowlist as "
                + "parsed_resumes.source_content_type. No user write path into the value.",

            ["resume_files:HeldButNotSearchable"] =
                "resume_files.content (the domain property is SealedContent) is THE CV FILE ITSELF - the bytes the raw_text we cannot read was "
                + "extracted from. Form C (IBinaryFieldSealer/IBinaryFieldOpener): sealed at rest "
                + "under the owning user's key, and deliberately OUTSIDE EncryptedFieldRegistry "
                + "because its read path is streaming and never engages the materialisation "
                + "interceptor. We HOLD it and we CANNOT search it - and a PDF is not text-searchable "
                + "in SQL even in plaintext, so this is doubly true. NOTE FOR THE NEXT PERSON: Form C "
                + "has no allowlist for the architecture cross-check to read, so this column is "
                + "enumerated BY HAND there. Add a Form-C column and you must add it there too - the "
                + "enumeration is manual because the codebase gives us no other handle, and saying so "
                + "beats a guard that reads as if it covered all three forms.",

            ["resume_versions:HeldButNotSearchable"] =
                "content_enc is Form B (ADR 0049 C4 #1c): the ResumeVersion.Content VO is "
                + "JSON-serialised, DEK-encrypted under the OWNING USER'S key and written to this "
                + "shadow column. It is her CV - and a CV can name a recruiter she wrote to. We "
                + "HOLD it and we CANNOT read it, so it is not NotRecruiterData (that word would "
                + "claim we hold nothing of hers; the truth is we cannot look). Same refusal, same "
                + "ground, as the Form-A columns: a corpus-wide decrypt-and-scan would build a "
                + "read-everyone's-CV capability permanently (Art. 25(2)/32) with no basis toward "
                + "those users (Art. 6). Disclosed in every reply, with the targeted-escalation "
                + "route. NOTE FOR THE NEXT PERSON: EncryptedFieldRegistry's Map is the FORM A "
                + "allowlist only, so the architecture cross-check enumerates the Form B columns "
                + "EXPLICITLY. Add a Form-B column and you must add it there too - the enumeration "
                + "is manual because the registry gives us no other handle.",

            ["company_register:SeparateProcessing"] =
                "A SEPARATE PROCESSING. company_name IS a natural person's name for an enskild "
                + "firma - but this table is a replica of a PUBLIC register (SCB, ADR 0090) with "
                + "its own legal basis, its own DPIA and its own erasure route. It is not our copy "
                + "of an ad, an Art. 17 request about an imported ad does not reach it, and the "
                + "DPIA C-D4 firewall explicitly FORBIDS a handler joining it. An enskild firma "
                + "owner's request against the REGISTER is a different request against a different "
                + "processing, out of scope for #842. Recorded, not omitted.",

            ["applications:MatchedRetained"] =
                "Art. 17(3)(e) - establishment, exercise or defence of legal claims. The applicant's "
                + "frozen record of an ad she applied to (ADR 0086 exists precisely so the snapshot "
                + "outlives the ad). And the ground is STRONGER for snapshot_company than for the ad "
                + "body, not weaker: a Swedish jobseeker must file an AKTIVITETSRAPPORT to "
                + "Arbetsförmedlingen naming the employer she applied to. The company name is the "
                + "SPINE of her own legal record; the ad body is its colour. That is why "
                + "AdSnapshot.WithoutDescription() can drop the body (snapshot_description) and "
                + "could never drop the company. snapshot_title is the title she applied under - "
                + "same frozen record, same ground. snapshot_url is the frozen ad URL, and a URL "
                + "path carries names routinely (linkedin.com/in/<name>) - the IDENTICAL argument "
                + "that put applications.manual_url in scope, and a registry whose verdicts "
                + "disagree about identical data is worth nothing; it went unsearched for exactly "
                + "one round after that sentence was written (round-5 B5-2). We SEARCH all four and "
                + "REPORT them precisely because we do not erase them: a legal ground asserted over "
                + "a population we never counted is a ground asserted over a silence, and that is "
                + "how the last registry stayed wrong. STOPP-3 - Klas affirms.",

            ["applications:MatchedHumanErases"] =
                "MANUALLY ENTERED ad details - the user typed or pasted them for an application she "
                + "tracks outside Platsbanken. manual_company and manual_title can carry a "
                + "recruiter's or a sole trader's name. manual_url is a 2000-char pasted string with "
                + "NO validation at the persistence boundary: it is free text with a max length, and "
                + "a URL path carries names routinely (linkedin.com/in/<name>, a company contact "
                + "page). Her right reaches all three (Art. 6(1)(f) -> Art. 21(1)). We SEARCH and "
                + "REPORT them; a HUMAN erases them, with the affected user in the loop, because "
                + "silently rewriting the record she keeps of her own job hunt is not something a "
                + "job may do (CLAUDE.md 5).",

            ["applications:HeldButNotSearchable"] =
                "cover_letter is Form-A DEK-encrypted (ADR 0049 C3 / 0066) under the owning user's "
                + "key - the column holds 'v1:<base64>' at rest, so a plaintext LIKE for her name "
                + "matches nothing, structurally, forever. See application_notes for the full ground: "
                + "her right reaches it, we refuse the corpus-wide decrypt-and-scan rather than the "
                + "person, the structural job_ad_id channel covers the letters written TO her ads, "
                + "and the residual is disclosed with a targeted-escalation route.",

            // ── NotRecruiterData: the receipts. Each names the WRITE-PATH guarantee, and it
            // names EVERY COLUMN in its bucket — a column its own ground never mentions has
            // inherited a verdict, not earned one (round-5 M2; the test enforces it). ──────────
            ["applications:NotRecruiterData"] =
                "Closed domain: snapshot_source is the JobSource enum's value, "
                + "snapshot_municipality_concept_id is an Arbetsförmedlingen taxonomy concept id, "
                + "and status is the ApplicationStatus SmartEnum stored by name, minted only by "
                + "TransitionTo from the fixed status vocabulary. The snapshots are frozen from the "
                + "AD at apply time, from a fixed set. No user write path reaches any of them, and "
                + "no free text can land in them.",

            ["application_status_changes:NotRecruiterData"] =
                "Closed domain: from_status and to_status are the ApplicationStatus SmartEnum "
                + "stored by name (StatusChangeConfiguration), written only by "
                + "Application.TransitionTo from the fixed status vocabulary. The timeline row "
                + "carries no other text column and no user-authored value.",

            ["follow_ups:NotRecruiterData"] =
                "Closed domain: channel and outcome are SmartEnums stored by name from fixed "
                + "vocabularies (FollowUpConfiguration value converters). The one free-text column "
                + "on this table is `note`, which is Form-A encrypted and classified "
                + "HeldButNotSearchable — see that ground.",

            ["job_ads:NotRecruiterData"] =
                "Closed domain: external_id / external_source / source are the ingest tuple, status "
                + "is a JobAdStatus value converter over a fixed set of four strings, and "
                + "ssyk_concept_id / region_concept_id / municipality_concept_id / "
                + "occupation_group_concept_id / employment_type_concept_id / "
                + "worktime_extent_concept_id are Arbetsförmedlingen taxonomy codes. The only "
                + "write path is the ingest funnel, which cannot author free text into any of "
                + "them.",

            ["audit_log:NotRecruiterData"] =
                "Closed domain: event_type and aggregate_type are constants minted by the command "
                + "that raised the row; ip_address and user_agent are the OPERATOR'S request "
                + "metadata (our admin), never the data subject's. No recruiter free text can reach "
                + "them - the one field that could is payload, which is Pseudonymised (HMAC).",

            ["company_register:NotRecruiterData"] =
                "Closed domain: sate_kommun_code / sate_kommun_name / scb_status_raw are SCB "
                + "register fields drawn from a fixed code list, sni_codes is the SCB/SNI "
                + "industry-code array (codes only, GIN-indexed for browse), and status is the "
                + "register-lifecycle enum stored by name (HasConversion<string>, max 20). The "
                + "write path is the SCB import; no user, and no free text, reaches them.",

            ["job_ad_snapshot_misses:NotRecruiterData"] =
                "Closed domain: external_id + source are the ingest tuple of an ad that went missing "
                + "from a snapshot. The write path is the miss tracker; there is no free text.",

            ["resume_finding_statuses:NotRecruiterData"] =
                "Closed domain: criterion_id and rubric_version are versioned rubric identifiers "
                + "from the knowledge bank, target_fingerprint is a hash, and status is the "
                + "ReviewFindingStatus SmartEnum stored by name. All four are minted by the CV "
                + "engine from a fixed vocabulary - no user write path, no free text.",

            ["resume_versions:NotRecruiterData"] =
                "RETIRED WRITE PATH, POPULATION COUNTED AT ZERO. The legacy plaintext `content` jsonb "
                + "is no longer written: the Form-B cutover (ADR 0049 Beslut 5 steg 3, #507a) flipped "
                + "the mapping to content_enc, and a FITNESS GATE proved 0 legacy-only rows "
                + "(content_enc IS NULL AND content IS NOT NULL = 0) BEFORE the flip, then a "
                + "migration nulled the column. The mapping is retained only as an inert read-only "
                + "shadow so the EF snapshot matches the physical schema; the column is dropped in a "
                + "later verified follow-up. This earns NotRecruiterData under shape (2): the write "
                + "path is gone AND somebody counted. `kind` is closed-domain under shape (1): the "
                + "ResumeVersionKind SmartEnum stored by name - the version-lineage vocabulary, no "
                + "user write path.",

            ["recent_job_searches:NotRecruiterData"] =
                "Closed domain: occupation_group_list / municipality_list / region_list / "
                + "employment_type_list / worktime_extent_list hold Arbetsförmedlingen taxonomy "
                + "CONCEPT IDS, captured from the search filter's code values, never from free "
                + "text. The two columns that CAN hold her identifier - `q` and `employer_list` - "
                + "are Erased with the row and searched by the RecentJobSearches channel.",

            ["company_watch_criteria:NotRecruiterData"] =
                "Closed domain: sni_codes and kommun_codes are SCB/SNI industry codes and kommun "
                + "codes from fixed code lists. The criterion IS its codes; the only free-text column "
                + "on this table is `label`, which is searched.",

            ["taxonomy_concepts:NotRecruiterData"] =
                "Closed domain: concept_id / parent_concept_id are taxonomy identifiers, label is "
                + "the taxonomy's own display label, and kind is the concept-type enum stored by "
                + "name - all replicated from Arbetsförmedlingen's taxonomy. The write path is the "
                + "taxonomy sync - no user, and no free text, can reach these columns.",

            ["taxonomy_relations:NotRecruiterData"] =
                "Closed domain: source_concept_id / related_concept_id are taxonomy identifiers "
                + "and kind is the relation-type enum stored by name, from the same taxonomy sync "
                + "as taxonomy_concepts. Ids and enum names only.",
        };
}
