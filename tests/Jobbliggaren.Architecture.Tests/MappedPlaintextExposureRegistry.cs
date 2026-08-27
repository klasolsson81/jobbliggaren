namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// What a restore exposes of a column, with no key involved.
/// </summary>
internal enum PlaintextExposure
{
    /// <summary>
    /// Readable in a restore, and it is personal data.
    /// </summary>
    PlaintextPersonalData,

    /// <summary>
    /// Readable in a restore, and it is not personal data: reference data, a role vocabulary we
    /// mint, or bookkeeping about a THING.
    /// </summary>
    /// <remarks>
    /// <b>Reachable only for a table that PASSES the row test</b> — see
    /// <see cref="MappedPlaintextExposureRegistry.PersonGrainedTables"/>. On a row that can be
    /// attributed to a natural person, a closed domain is still that person's data, so this bucket
    /// is not available there at all.
    /// <para>
    /// <b>ADMISSION RULE for the tables this bucket lives in:</b> the column's content is drawn from
    /// a domain the user cannot author into — concept ids, a role vocabulary, source names. A LIVE
    /// FREE-TEXT COLUMN IS NEVER <c>NoPersonalData</c>, however unlikely a name is to land in it.
    /// "We judged it unlikely" is not a ground. That rule is what caught <c>parsed_resumes</c> one
    /// registry over.
    /// </para>
    /// </remarks>
    NoPersonalData,
}

/// <summary>
/// The DEK-free plaintext columns the two EF models map, and what a restore exposes of each — the
/// enumeration Klas accepts residual exposure against (ADR 0125's Case 2, #197, #1285).
/// </summary>
/// <remarks>
/// <b>Why this is code and not a paragraph.</b> The same enumeration lived in three normative prose
/// homes and disagreed with itself in all three: ADR 0050:221 carried four entries, ADR 0125 five,
/// the ROPA's backup entry six. <b>All three named <c>waitlist_entries</c></b>, a table dropped on
/// 2026-06-27. In the same sentence as its own list, ADR 0050:221 also counted the field-encrypted
/// columns at "4" when the registry it cited held seven. That is ADR 0024's failure exactly —
/// <i>"prose in a document; it listed raw_payload and nothing else, went stale silently, and an
/// auditor reading it would have concluded we were compliant"</i> — and the sibling registry in this
/// project exists because of it.
/// <para>
/// <b>THE NAME IS DELIBERATE AND IT IS NOT "Backup".</b> This registry covers <b>what the two EF
/// models map</b>. A <c>pg_dump -d jobbliggaren</c> carries more: every schema in the database,
/// including <c>hangfire</c> (same database, `docker-compose.yml`), <c>audit_log</c>'s runtime
/// partitions, and two <c>__EFMigrationsHistory</c> tables. Calling this a *backup* registry would
/// assert exactly the coverage <c>security-auditor</c> measured as missing (PR #1530 Major 1) — the
/// false-completeness defect #1285 reported, reproduced in an identifier. <b>The gap is named in
/// every pointer to this file, never implied away</b>, and closing it is a follow-up PR's
/// change-reason: *the dump carries only schemas that are classified*.
/// </para>
/// <para>
/// <b>An enumeration a human has to remember to update is not an enumeration.</b> A text column
/// added to either model breaks the build until somebody decides what a restore exposes of it, and
/// an entry naming a column or table the models do not have breaks the build too — which is what
/// makes a <c>waitlist_entries</c> entry unwritable rather than merely wrong.
/// </para>
/// <para>
/// <b>This is NOT the Art. 17 cascade, and the difference is the data subject.</b>
/// <c>ErasureCascadeRegistry</c> answers "what does erasing a RECRUITER destroy?" and excludes
/// ASP.NET Identity wholesale. This answers "what does a restore expose about ANYONE?" — so it
/// sweeps <c>AppIdentityDbContext</c> too. Two of the four entries in the oldest enumeration
/// (<c>email</c>, <c>name</c>) live in exactly the model the other registry cannot see.
/// </para>
/// </remarks>
internal static class MappedPlaintextExposureRegistry
{
    /// <summary>
    /// <b>STEP 1 — THE ROW TEST (Art. 4(1)).</b> Tables whose row can be attributed to an
    /// identifiable natural person. <b>Every DEK-free text-bearing column on such a table is
    /// <see cref="PlaintextExposure.PlaintextPersonalData"/>, derived, with no per-column entry and
    /// no way to opt one out.</b>
    /// </summary>
    /// <remarks>
    /// <b>This replaced an admission rule imported from the wrong question, and the replacement was
    /// forced by measurement</b> (<c>security-auditor</c>, PR #1530 Major 2; rule bound by
    /// <c>senior-cto-advisor</c>, who withdrew his own earlier bind). The first cut inherited
    /// <c>ErasureCascadeRegistry</c>'s <c>NotRecruiterData</c> admission rule verbatim. But that rule
    /// answers a WRITE-PATH question — *can a recruiter's free text land here?* — and this registry
    /// answers a RELATIONAL one: *does a restore expose personal data?* Art. 4(1) is
    /// <i>"any information relating to an identified or identifiable natural person"</i>, so
    /// <b>a closed domain beside a person's id is still that person's personal data.</b>
    /// <para>
    /// Three collisions measured the error, all of them in sibling columns of a row already
    /// classified as exposed: <c>user_job_ad_matches.grade</c> is the PROFILING OUTCOME (Art. 4(4),
    /// the subject of ADR 0090's DPIA) while its inputs were listed;
    /// <c>parsed_resumes.parse_confidence</c> is derived from her file exactly as
    /// <c>layout_metrics</c> is; and on <c>company_register</c> two columns were exposed and five
    /// were not — on the same row, about the same person.
    /// </para>
    /// <para>
    /// ⚠ <b>A table falls the row test WHOLE if it falls for any subset of its rows</b>, because a
    /// column cannot be classified per row. That is what puts <c>company_register</c> and
    /// <c>job_ads</c> here despite having no person foreign key: <b>a sole trader's organisation
    /// number IS her personnummer</b> (#841), and her company name is very often her own.
    /// </para>
    /// <para>
    /// <b>A wholesale verdict is the cheapest possible false verdict in the system</b>, so this list
    /// carries the same mitigation the sibling registry's <c>NonRecruiterTables</c> carries: a
    /// written ground per table, pinned by a test. Here the wholesale verdict is the SAFE one —
    /// every column exposed — so the ground exists to stop a table being LEFT OUT, which is the
    /// direction that loses a column from the enumeration.
    /// </para>
    /// </remarks>
    internal static IReadOnlyDictionary<string, string> PersonGrainedTables { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ── ASP.NET Identity: one row per user. ───────────────────────────────────────────
            ["AspNetUsers"] = "One row per registered user. Holds email, normalized_email, "
                + "user_name and normalized_user_name (UserAccountService writes UserName = email, "
                + "so three of these are the address), phone_number, password_hash, and the OAuth "
                + "provider's identifier for her. Two of the four entries in the oldest delivered "
                + "enumeration are here.",
            ["AspNetUserClaims"] = "One row per user claim. Both claim_type and claim_value are "
                + "unvalidated text on a per-person row.",
            ["AspNetUserLogins"] = "One row per external login. provider_key is the OAuth "
                + "provider's identifier FOR HER — an Art. 4(1) identifier by definition.",
            ["AspNetUserTokens"] = "One row per user token: 2FA and password-reset values, per "
                + "person.",
            ["AspNetUserRoles"] = "Join table, one row per user-role pair. No text-bearing column "
                + "today; listed so a column added here cannot land outside the row test.",

            // ── The seeker's own records. ─────────────────────────────────────────────────────
            ["job_seekers"] = "One row per seeker (UserId). display_name, match_preferences, the "
                + "preferences ToJson container and Language inside it — #1435 measured all four as "
                + "free text with no taxonomy lookup and, for Language, no server-side validation.",
            ["applications"] = "One row per application she made (JobSeekerId). Her manual_* entries "
                + "and the frozen snapshot_* block are both her record of having applied.",
            ["application_status_changes"] = "One row per status transition on an application, so "
                + "one row about her progress. from_status and to_status are her history.",
            ["application_notes"] = "Her notes on an application. Every text column is DEK-encrypted "
                + "and therefore absent from the enumeration; the table is listed so a future "
                + "plaintext column cannot land outside the row test.",
            ["follow_ups"] = "Her follow-ups on an application. channel and outcome are facts about "
                + "what she did and when.",
            ["saved_searches"] = "Her saved searches (JobSeekerId): name is hers to type, criteria "
                + "is jsonb carrying her free-text q.",
            ["saved_job_ads"] = "One row per ad she saved (JobSeekerId). No text-bearing column "
                + "today; listed so a column added here cannot land outside the row test.",
            ["recent_job_searches"] = "Her search history (JobSeekerId). #1425 measured the five "
                + "list columns as shape-validated only (^[A-Za-z0-9_-]{1,32}\\z), so Karlsson, "
                + "Anna-Karlsson and a ten-digit org.nr all persist; employer_list holds org.nr by "
                + "design, and filter_hash is a pseudonym of the criteria (Recital 26).",
            ["company_watches"] = "Her company watches (UserId), including the organisation numbers "
                + "she follows and the filter she set.",
            ["company_watch_criteria"] = "Her saved watch criteria (UserId): label is free text she "
                + "types, and kommun_codes and sni_codes are the set SHE chose to follow — a "
                + "selection about her interests even though each code is a closed domain.",
            ["followed_company_ad_hits"] = "One row per ad hit delivered to her (UserId). "
                + "notification_status is a fact about what she was sent.",
            ["user_job_ad_matches"] = "One row per match computed FOR HER (UserId). grade is the "
                + "PROFILING OUTCOME (Art. 4(4), ADR 0090's DPIA) and matched_skill_concept_ids is a "
                + "selection derived from her CV — the concept ids are a closed domain, but WHICH "
                + "of them matched is about her.",
            ["user_data_keys"] = "Her DEK envelope (JobSeekerId). ⚠ The wrapped key carries no "
                + "personal data OF ITS OWN, and it is listed here anyway because the row is hers "
                + "and a future column on it must not land outside the row test. ADR 0125 exists "
                + "because this row travels near the ciphertext it opens; that risk is the ADR's "
                + "subject, not this registry's verdict.",
            ["audit_log"] = "One row per audited event (UserId). ip_address and user_agent are hers; "
                + "payload is pseudonymised, and a pseudonym is a personal datum (Recital 26). The "
                + "Art. 17 registry classifies payload Pseudonymised for its own axis; for a restore "
                + "it is exposed.",

            // ── The CV lane: her file and everything derived from it. ─────────────────────────
            ["resumes"] = "Her CVs (JobSeekerId). name, latest_role and top_skills are the DEK-FREE "
                + "denormalised projections #1285 is named for — they exist PRECISELY BECAUSE the "
                + "CV body is encrypted and opaque to SQL. The template_* columns are her choices.",
            ["resume_versions"] = "A version of her CV (FK to resumes). content_enc is encrypted and "
                + "absent from the enumeration; kind and the retired content column are on her row.",
            ["resume_files"] = "Her uploaded CV file (JobSeekerId). file_name is often her own name "
                + "and is the fifth entry in ADR 0125's delivered list.",
            ["resume_finding_statuses"] = "Review findings on her CV (FK to resumes). "
                + "target_fingerprint is derived from a span of her CV — a pseudonym of it.",
            ["parsed_resumes"] = "Her parsed CV (JobSeekerId). raw_text and parsed_content_enc are "
                + "encrypted; source_file_name, the proposal columns, gap_summary, layout_metrics "
                + "and parse_confidence are all DEK-free and all derived from her file. "
                + "personnummer_scan is the scan RESULT for a personnummer in her CV.",

            // ── No person FK, and they fall the row test anyway. ──────────────────────────────
            ["company_register"] = "SCB's register of companies — mostly facts about THINGS, and it "
                + "falls the row test WHOLE on a subset. A SOLE TRADER'S ORGANISATION NUMBER IS HER "
                + "PERSONNUMMER (#841) and her company name is very often her own, so those rows are "
                + "about a natural person. There is no join to discount: her kommun, her SNI codes "
                + "and her registration status are on the same row, and a column cannot be "
                + "classified per row.",
            ["job_ads"] = "Job ads, falling the row test WHOLE on the same subset ground: "
                + "organization_number is a personnummer for a sole trader, contacts is the "
                + "structured recruiter contact block, and description carries recruiter contact "
                + "details — the ROPA's sixth entry, which ADR 0125's five-item list omitted. "
                + "extracted_terms, extracted_lexemes and search_vector are derived FROM the "
                + "description and carry whatever it carried. The recruiter is a data subject too.",
        };

    /// <summary>
    /// <b>STEP 2 — THE COLUMN TEST.</b> Per-column verdicts for the tables that PASS the row test.
    /// </summary>
    /// <remarks>
    /// Six tables reach this step, measured 2026-08-27. Reference data and a role vocabulary: no row
    /// here is about a natural person, so a column-level judgement is meaningful.
    /// </remarks>
    internal static IReadOnlyDictionary<string, PlaintextExposure> Columns { get; } =
        new Dictionary<string, PlaintextExposure>(StringComparer.Ordinal)
        {
            // ── Role vocabulary: per ROLE, never per person. ──────────────────────────────────
            ["AspNetRoles.name"] = PlaintextExposure.NoPersonalData,
            ["AspNetRoles.normalized_name"] = PlaintextExposure.NoPersonalData,
            ["AspNetRoles.concurrency_stamp"] = PlaintextExposure.NoPersonalData,
            ["AspNetRoleClaims.claim_type"] = PlaintextExposure.NoPersonalData,
            ["AspNetRoleClaims.claim_value"] = PlaintextExposure.NoPersonalData,

            // ── Ingestion bookkeeping about an AD we failed to snapshot. No person. ───────────
            ["job_ad_snapshot_misses.external_id"] = PlaintextExposure.NoPersonalData,
            ["job_ad_snapshot_misses.source"] = PlaintextExposure.NoPersonalData,

            // ── Taxonomy reference data. No user write path reaches any of it. ───────────────
            ["taxonomy_concepts.concept_id"] = PlaintextExposure.NoPersonalData,
            ["taxonomy_concepts.kind"] = PlaintextExposure.NoPersonalData,
            ["taxonomy_concepts.label"] = PlaintextExposure.NoPersonalData,
            ["taxonomy_concepts.parent_concept_id"] = PlaintextExposure.NoPersonalData,
            ["taxonomy_relations.kind"] = PlaintextExposure.NoPersonalData,
            ["taxonomy_relations.source_concept_id"] = PlaintextExposure.NoPersonalData,
            ["taxonomy_relations.related_concept_id"] = PlaintextExposure.NoPersonalData,
            ["taxonomy_snapshot_meta.taxonomy_version"] = PlaintextExposure.NoPersonalData,
        };

    /// <summary>
    /// The exposed set, DERIVED — the enumeration ADR 0125, the ROPA and ADR 0050 point at instead
    /// of carrying a hand-written copy of.
    /// </summary>
    /// <remarks>
    /// <b>Read with the coverage limit its pointers carry:</b> these are the columns the two EF
    /// models map. A <c>pg_dump</c> also carries <c>hangfire</c>, the <c>audit_log</c> runtime
    /// partitions and the migration histories — see the type's own remarks.
    /// </remarks>
    internal static IReadOnlyList<string> PlaintextPersonalDataColumns { get; } =
    [
        .. ModelSweep.AllModelsTextColumnsByTable()
            .Where(kv => PersonGrainedTables.ContainsKey(kv.Key))
            .SelectMany(kv => kv.Value)
            .Except(ModelSweep.AllModelsEncryptedColumns())
            .Concat(Columns
                .Where(kv => kv.Value == PlaintextExposure.PlaintextPersonalData)
                .Select(kv => kv.Key))
            .Order(StringComparer.Ordinal),
    ];
}
