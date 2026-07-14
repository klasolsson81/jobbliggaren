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
    /// We HOLD it and we CANNOT search it. The column is encrypted at rest under a PER-USER DEK
    /// envelope (ADR 0049 C3 / ADR 0066, Form A), so a plaintext <c>LIKE</c> compares her name
    /// against base64 ciphertext and matches nothing — not "nothing today", but structurally, on
    /// every request, forever.
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
/// The Art. 17 cascade registry for recruiter PII (#842) — every persisted surface, classified,
/// with a written reason. <c>ErasureCascadeRegistryTests</c> pins it: a new <c>DbSet</c> on
/// <c>IAppDbContext</c> that appears in none of the three sets <b>breaks the build</b>.
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
        "ManualAdEntries",
        "CompanyWatchCriteria",
        "ApplicationsReferencingMatchedAds",
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

            // ── saved_searches: MATCHED, a HUMAN erases ─────────────────────────────────────
            ["saved_searches.criteria"] = ErasureColumnDisposition.MatchedHumanErases,
            ["saved_searches.name"] = ErasureColumnDisposition.MatchedHumanErases,
            ["recent_job_searches.filter_hash"] = ErasureColumnDisposition.Erased,

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

            // ── resume_versions: one retired plaintext column, one Form-B ciphertext column ──
            // content: the write path is GONE (Form-B cutover, ADR 0049 Beslut 5 steg 3) and the
            // population was COUNTED at zero before the mapping flip. That is what earns
            // NotRecruiterData — a checkable claim about the write path, not a guess.
            // content_enc: Form B — the VO is JSON-serialised and DEK-encrypted into this shadow. We
            // HOLD user content here and cannot read it. It is NOT NotRecruiterData: that word would
            // claim we hold nothing of hers, when the truth is we cannot look.
            ["resume_versions.content"] = ErasureColumnDisposition.NotRecruiterData,
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
            ["taxonomy_concepts.concept_id"] = ErasureColumnDisposition.NotRecruiterData,
            ["taxonomy_concepts.label"] = ErasureColumnDisposition.NotRecruiterData,
            ["taxonomy_concepts.parent_concept_id"] = ErasureColumnDisposition.NotRecruiterData,
            ["taxonomy_relations.source_concept_id"] = ErasureColumnDisposition.NotRecruiterData,
            ["taxonomy_relations.related_concept_id"] = ErasureColumnDisposition.NotRecruiterData,
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
                + "AdSnapshot.WithoutDescription() can drop the body and could never drop the "
                + "company. We SEARCH it and REPORT it precisely because we do not erase it: a legal "
                + "ground asserted over a population we never counted is a ground asserted over a "
                + "silence, and that is how the last registry stayed wrong. STOPP-3 - Klas affirms.",

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

            // ── NotRecruiterData: the receipts. Each names the WRITE-PATH guarantee. ─────────
            ["applications:NotRecruiterData"] =
                "Closed domain: snapshot_source is the JobSource enum's value and "
                + "snapshot_municipality_concept_id is an Arbetsförmedlingen taxonomy concept id. "
                + "Both are frozen from the AD at apply time, from a fixed set. No user write path "
                + "reaches either, and no free text can land in them.",

            ["job_ads:NotRecruiterData"] =
                "Closed domain: external_id / external_source / source are the ingest tuple, status "
                + "is a JobAdStatus value converter over a fixed set of four strings, and the six "
                + "*_concept_id columns are Arbetsförmedlingen taxonomy codes. The only write path "
                + "is the ingest funnel, which cannot author free text into any of them.",

            ["audit_log:NotRecruiterData"] =
                "Closed domain: event_type and aggregate_type are constants minted by the command "
                + "that raised the row; ip_address and user_agent are the OPERATOR'S request "
                + "metadata (our admin), never the data subject's. No recruiter free text can reach "
                + "them - the one field that could is payload, which is Pseudonymised (HMAC).",

            ["company_register:NotRecruiterData"] =
                "Closed domain: sate_kommun_code / sate_kommun_name / scb_status_raw are SCB "
                + "register fields drawn from a fixed code list. The write path is the SCB import; "
                + "no user, and no free text, reaches them.",

            ["job_ad_snapshot_misses:NotRecruiterData"] =
                "Closed domain: external_id + source are the ingest tuple of an ad that went missing "
                + "from a snapshot. The write path is the miss tracker; there is no free text.",

            ["resume_finding_statuses:NotRecruiterData"] =
                "Closed domain: criterion_id and rubric_version are versioned rubric identifiers "
                + "from the knowledge bank, and target_fingerprint is a hash. All three are minted "
                + "by the CV engine from a fixed vocabulary - no user write path, no free text.",

            ["resume_versions:NotRecruiterData"] =
                "RETIRED WRITE PATH, POPULATION COUNTED AT ZERO. The legacy plaintext `content` jsonb "
                + "is no longer written: the Form-B cutover (ADR 0049 Beslut 5 steg 3, #507a) flipped "
                + "the mapping to content_enc, and a FITNESS GATE proved 0 legacy-only rows "
                + "(content_enc IS NULL AND content IS NOT NULL = 0) BEFORE the flip, then a "
                + "migration nulled the column. The mapping is retained only as an inert read-only "
                + "shadow so the EF snapshot matches the physical schema; the column is dropped in a "
                + "later verified follow-up. This earns NotRecruiterData under shape (2): the write "
                + "path is gone AND somebody counted.",

            ["taxonomy_concepts:NotRecruiterData"] =
                "Closed domain: concept ids and labels replicated from Arbetsförmedlingen's taxonomy. "
                + "The write path is the taxonomy sync - no user, and no free text, can reach these "
                + "columns.",

            ["taxonomy_relations:NotRecruiterData"] =
                "Closed domain: source/related concept ids from the same taxonomy sync as "
                + "taxonomy_concepts. Ids only.",
        };
}
