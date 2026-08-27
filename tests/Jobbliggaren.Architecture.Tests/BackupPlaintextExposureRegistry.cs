namespace Jobbliggaren.Architecture.Tests;

/// <summary>
/// What a restore of an offsite backup exposes without any key.
/// </summary>
public enum BackupExposure
{
    /// <summary>
    /// The column's VALUE carries personal data in itself: an identifier, a name, free text,
    /// content, or something derived from one of those (a hash, a fingerprint, an FTS vector).
    /// Readable in a restore with no key involved.
    /// </summary>
    PlaintextPersonalData,

    /// <summary>
    /// The column's VALUE carries no personal data: a code we mint, a taxonomy concept id, an enum,
    /// a version string, or a fact about a THING rather than a person.
    /// </summary>
    /// <remarks>
    /// <b>The row's user foreign key does not make the column's VALUE a personal datum.</b> That is
    /// the line this bucket draws, and it is drawn deliberately: read strictly, Art. 4(1) reaches
    /// every column in every table that has a person FK, and an enumeration that says "the whole
    /// database" tells Klas nothing he can accept a residual against. The four delivered
    /// enumerations all drew the same line without writing it down — they name emails, names, IPs,
    /// filenames and free text, never status enums. This makes it explicit.
    /// <para>
    /// <b>ADMISSION RULE, inherited verbatim from <c>ErasureCascadeRegistry</c>'s
    /// <c>NotRecruiterData</c>.</b> A column may be classified here ONLY if (1) its content is drawn
    /// from a CLOSED DOMAIN the user cannot author into — ids, concept codes, hashes we mint, enum
    /// values, source names, MIME types, version strings — or (2) its WRITE PATH IS RETIRED and
    /// somebody counted the population at zero (<c>resume_versions.content</c>, nulled at the Form-B
    /// cutover: ADR 0049 Beslut 5 steg 3).
    /// </para>
    /// <para>
    /// <b>A LIVE free-text column is NEVER <c>NoPersonalData</c>, however unlikely a name is to land
    /// in it.</b> "We judged it unlikely" is not a ground. That rule is what caught
    /// <c>parsed_resumes</c> one registry over, and it is why the five shape-validated
    /// <c>recent_job_searches</c> list columns are classified as exposed: #1425 measured that
    /// <c>Karlsson</c>, <c>Anna-Karlsson</c> and a ten-digit org.nr all pass their validator.
    /// </para>
    /// <para>
    /// <b>A pseudonym is a personal datum (Recital 26).</b> A hash or fingerprint DERIVED from
    /// personal data is <see cref="PlaintextPersonalData"/>, not this. A random stamp we mint that
    /// is derived from nothing (<c>security_stamp</c>, <c>concurrency_stamp</c>) is this.
    /// </para>
    /// </remarks>
    NoPersonalData,
}

/// <summary>
/// The DEK-free plaintext columns a <c>pg_dump</c> carries into an offsite backup — the enumeration
/// Klas accepts residual exposure against (ADR 0125's Case 2, #197, #1285).
/// </summary>
/// <remarks>
/// <b>Why this is code and not a paragraph.</b> The same enumeration lived in four prose homes and
/// disagreed with itself in all four: ADR 0050:221 carried four entries, ADR 0125 five, the ROPA's
/// backup entry six, and ADR 0050's own count of the field-encrypted columns said "4" when the
/// registry it cited held seven. Every one of them named <c>waitlist_entries</c>, a table dropped on
/// 2026-06-27. That is ADR 0024's failure exactly — <i>"prose in a document; it listed raw_payload
/// and nothing else, went stale silently, and an auditor reading it would have concluded we were
/// compliant"</i> — and the sibling registry in this project exists because of it.
/// <para>
/// <b>An enumeration a human has to remember to update is not an enumeration.</b> This one is driven
/// by the EF model of BOTH DbContexts, so a text column added anywhere breaks the build until
/// somebody decides what a restore exposes of it — and an entry naming a column the model does not
/// have breaks the build too, which is what makes <c>waitlist_entries</c> unwritable here.
/// </para>
/// <para>
/// <b>This is NOT the Art. 17 cascade, and the difference is the data subject.</b>
/// <c>ErasureCascadeRegistry</c> answers "what does erasing a RECRUITER destroy?" and excludes
/// ASP.NET Identity wholesale. This answers "what does a restore expose about ANYONE?" — so it
/// sweeps <c>AppIdentityDbContext</c> too, and it deliberately does <b>not</b> honour that
/// registry's <c>NonRecruiterTables</c>. Two of the four entries in the oldest enumeration
/// (<c>email</c>, <c>name</c>) live in exactly the model the other registry cannot see.
/// </para>
/// </remarks>
internal static class BackupPlaintextExposureRegistry
{
    /// <summary>
    /// Every DEK-free text-bearing column in both models, and what a restore exposes of it.
    /// </summary>
    internal static IReadOnlyDictionary<string, BackupExposure> Columns { get; } =
        new Dictionary<string, BackupExposure>(StringComparer.Ordinal)
        {
            // ══ ASP.NET Identity (AppIdentityDbContext) ═══════════════════════════════════════
            // The model the Art. 17 registry cannot see, holding two of the four entries the
            // oldest enumeration named. Table names are PascalCase — Identity sets them with an
            // explicit ToTable(), so UseSnakeCaseNamingConvention() rewrites the COLUMNS and leaves
            // the TABLES alone. (`NonRecruiterTables` one registry over keys them `asp_net_users`,
            // which matches nothing in either model. Those entries are inert twice over.)
            ["AspNetUsers.email"] = BackupExposure.PlaintextPersonalData,
            ["AspNetUsers.normalized_email"] = BackupExposure.PlaintextPersonalData,

            // UserName IS the email — UserAccountService.cs:25 writes `UserName = email` on every
            // registration. Both of these are a second and third copy of the address.
            ["AspNetUsers.user_name"] = BackupExposure.PlaintextPersonalData,
            ["AspNetUsers.normalized_user_name"] = BackupExposure.PlaintextPersonalData,
            ["AspNetUsers.phone_number"] = BackupExposure.PlaintextPersonalData,

            // A credential belonging to an identified person. Not readable as a password, but a
            // restore hands an attacker every hash to grind offline, and it is hers.
            ["AspNetUsers.password_hash"] = BackupExposure.PlaintextPersonalData,

            // The OAuth provider's identifier FOR HER — an Art. 4(1) identifier by definition.
            ["AspNetUsers.provider_user_id"] = BackupExposure.PlaintextPersonalData,
            ["AspNetUserLogins.provider_key"] = BackupExposure.PlaintextPersonalData,

            // 2FA and password-reset tokens, per user.
            ["AspNetUserTokens.value"] = BackupExposure.PlaintextPersonalData,

            // Per-USER claims. Our code mints role/policy claims today, but the columns are
            // unvalidated free text on a per-person row — the ADMISSION RULE's first clause fails.
            ["AspNetUserClaims.claim_type"] = BackupExposure.PlaintextPersonalData,
            ["AspNetUserClaims.claim_value"] = BackupExposure.PlaintextPersonalData,

            ["AspNetUsers.concurrency_stamp"] = BackupExposure.NoPersonalData,
            ["AspNetUsers.security_stamp"] = BackupExposure.NoPersonalData,
            ["AspNetUsers.provider"] = BackupExposure.NoPersonalData,
            ["AspNetRoles.name"] = BackupExposure.NoPersonalData,
            ["AspNetRoles.normalized_name"] = BackupExposure.NoPersonalData,
            ["AspNetRoles.concurrency_stamp"] = BackupExposure.NoPersonalData,
            ["AspNetRoleClaims.claim_type"] = BackupExposure.NoPersonalData,
            ["AspNetRoleClaims.claim_value"] = BackupExposure.NoPersonalData,
            ["AspNetUserLogins.login_provider"] = BackupExposure.NoPersonalData,
            ["AspNetUserLogins.provider_display_name"] = BackupExposure.NoPersonalData,
            ["AspNetUserTokens.login_provider"] = BackupExposure.NoPersonalData,
            ["AspNetUserTokens.name"] = BackupExposure.NoPersonalData,

            // ══ applications ══════════════════════════════════════════════════════════════════
            // Her application history. The manual_* columns are what SHE typed; the snapshot_*
            // columns are the frozen ad she applied to, which is her record of having applied.
            ["applications.manual_company"] = BackupExposure.PlaintextPersonalData,
            ["applications.manual_title"] = BackupExposure.PlaintextPersonalData,
            ["applications.manual_url"] = BackupExposure.PlaintextPersonalData,
            ["applications.snapshot_company"] = BackupExposure.PlaintextPersonalData,
            ["applications.snapshot_contacts"] = BackupExposure.PlaintextPersonalData,
            ["applications.snapshot_description"] = BackupExposure.PlaintextPersonalData,
            ["applications.snapshot_title"] = BackupExposure.PlaintextPersonalData,
            ["applications.snapshot_url"] = BackupExposure.PlaintextPersonalData,
            ["applications.snapshot_municipality_concept_id"] = BackupExposure.NoPersonalData,
            ["applications.snapshot_source"] = BackupExposure.NoPersonalData,
            ["applications.status"] = BackupExposure.NoPersonalData,
            ["application_status_changes.from_status"] = BackupExposure.NoPersonalData,
            ["application_status_changes.to_status"] = BackupExposure.NoPersonalData,

            // ══ audit_log ═════════════════════════════════════════════════════════════════════
            // "audit IP" is the third entry in the oldest enumeration, and it is correct.
            ["audit_log.ip_address"] = BackupExposure.PlaintextPersonalData,
            ["audit_log.user_agent"] = BackupExposure.PlaintextPersonalData,

            // Pseudonymised, and a pseudonym is a personal datum (Recital 26). The Art. 17 registry
            // classifies this Pseudonymised for its own axis; for a restore it is exposed.
            ["audit_log.payload"] = BackupExposure.PlaintextPersonalData,
            ["audit_log.event_type"] = BackupExposure.NoPersonalData,
            ["audit_log.aggregate_type"] = BackupExposure.NoPersonalData,

            // ══ company_register ══════════════════════════════════════════════════════════════
            // SCB's register of companies — mostly facts about THINGS. Two exceptions, and they are
            // the same exception: A SOLE TRADER'S ORG.NR IS HER PERSONNUMMER (#841), and a sole
            // trader's company name is very often her own name.
            ["company_register.organization_number"] = BackupExposure.PlaintextPersonalData,
            ["company_register.company_name"] = BackupExposure.PlaintextPersonalData,
            ["company_register.sni_codes"] = BackupExposure.NoPersonalData,
            ["company_register.sate_kommun_code"] = BackupExposure.NoPersonalData,
            ["company_register.sate_kommun_name"] = BackupExposure.NoPersonalData,
            ["company_register.scb_status_raw"] = BackupExposure.NoPersonalData,
            ["company_register.status"] = BackupExposure.NoPersonalData,

            // ══ company_watches / company_watch_criteria ══════════════════════════════════════
            ["company_watches.organization_number"] = BackupExposure.PlaintextPersonalData,
            ["company_watches.filter"] = BackupExposure.PlaintextPersonalData,
            ["company_watch_criteria.label"] = BackupExposure.PlaintextPersonalData,
            ["company_watches.target_type"] = BackupExposure.NoPersonalData,
            ["company_watches.brand_group_id"] = BackupExposure.NoPersonalData,
            ["company_watch_criteria.kommun_codes"] = BackupExposure.NoPersonalData,
            ["company_watch_criteria.sni_codes"] = BackupExposure.NoPersonalData,

            // ══ follow_ups / notification plumbing ════════════════════════════════════════════
            ["follow_ups.channel"] = BackupExposure.NoPersonalData,
            ["follow_ups.outcome"] = BackupExposure.NoPersonalData,
            ["followed_company_ad_hits.notification_status"] = BackupExposure.NoPersonalData,
            ["job_ad_snapshot_misses.external_id"] = BackupExposure.NoPersonalData,
            ["job_ad_snapshot_misses.source"] = BackupExposure.NoPersonalData,

            // ══ job_ads ═══════════════════════════════════════════════════════════════════════
            // The ROPA's sixth entry — "rekryterarkontakter i job_ads.description" — was RIGHT, and
            // ADR 0125's five-item list was the one that was short. The recruiter is a data subject
            // too, and a sole trader's org.nr here is a personnummer exactly as above.
            ["job_ads.description"] = BackupExposure.PlaintextPersonalData,
            ["job_ads.contacts"] = BackupExposure.PlaintextPersonalData,
            ["job_ads.company_name"] = BackupExposure.PlaintextPersonalData,
            ["job_ads.organization_number"] = BackupExposure.PlaintextPersonalData,
            ["job_ads.title"] = BackupExposure.PlaintextPersonalData,
            ["job_ads.url"] = BackupExposure.PlaintextPersonalData,
            ["job_ads.raw_payload"] = BackupExposure.PlaintextPersonalData,

            // Derived FROM the description, so they carry whatever it carried.
            ["job_ads.extracted_terms"] = BackupExposure.PlaintextPersonalData,
            ["job_ads.extracted_lexemes"] = BackupExposure.PlaintextPersonalData,
            ["job_ads.search_vector"] = BackupExposure.PlaintextPersonalData,
            ["job_ads.external_id"] = BackupExposure.NoPersonalData,
            ["job_ads.external_source"] = BackupExposure.NoPersonalData,
            ["job_ads.source"] = BackupExposure.NoPersonalData,
            ["job_ads.status"] = BackupExposure.NoPersonalData,
            ["job_ads.municipality_concept_id"] = BackupExposure.NoPersonalData,
            ["job_ads.region_concept_id"] = BackupExposure.NoPersonalData,
            ["job_ads.occupation_group_concept_id"] = BackupExposure.NoPersonalData,
            ["job_ads.employment_type_concept_id"] = BackupExposure.NoPersonalData,
            ["job_ads.worktime_extent_concept_id"] = BackupExposure.NoPersonalData,
            ["job_ads.ssyk_concept_id"] = BackupExposure.NoPersonalData,

            // ══ job_seekers ═══════════════════════════════════════════════════════════════════
            // #1435 measured all four: display_name refuses only empty/over-length/personnummer,
            // match_preferences is six shape-validated token lists with no taxonomy lookup, and
            // Language — inside the `preferences` ToJson container, THE SAME BYTES — has no
            // server-side validation at all. Live free text, all four.
            ["job_seekers.display_name"] = BackupExposure.PlaintextPersonalData,
            ["job_seekers.match_preferences"] = BackupExposure.PlaintextPersonalData,
            ["job_seekers.preferences"] = BackupExposure.PlaintextPersonalData,
            ["job_seekers.Language"] = BackupExposure.PlaintextPersonalData,

            // ══ parsed_resumes ════════════════════════════════════════════════════════════════
            // The DEK-free projections BESIDE the encrypted CV. #1285's finding: these exist
            // PRECISELY BECAUSE the content is encrypted and opaque to SQL.
            ["parsed_resumes.source_file_name"] = BackupExposure.PlaintextPersonalData,
            ["parsed_resumes.occupation_proposals"] = BackupExposure.PlaintextPersonalData,
            ["parsed_resumes.skill_proposals"] = BackupExposure.PlaintextPersonalData,
            ["parsed_resumes.gap_summary"] = BackupExposure.PlaintextPersonalData,
            ["parsed_resumes.layout_metrics"] = BackupExposure.PlaintextPersonalData,

            // The scan RESULT for a personnummer in her CV. Whatever it holds, it is about her.
            ["parsed_resumes.personnummer_scan"] = BackupExposure.PlaintextPersonalData,
            ["parsed_resumes.source_content_type"] = BackupExposure.NoPersonalData,
            ["parsed_resumes.parse_confidence"] = BackupExposure.NoPersonalData,
            ["parsed_resumes.status"] = BackupExposure.NoPersonalData,

            // ══ recent_job_searches ═══════════════════════════════════════════════════════════
            // ALL of them. #1425: the five list columns are SHAPE-validated against
            // ^[A-Za-z0-9_-]{1,32}\z and never taxonomy-resolved, so `Karlsson`, `Anna-Karlsson`
            // and a ten-digit org.nr all persist. employer_list holds org.nr by design.
            ["recent_job_searches.q"] = BackupExposure.PlaintextPersonalData,
            ["recent_job_searches.employer_list"] = BackupExposure.PlaintextPersonalData,
            ["recent_job_searches.occupation_group_list"] = BackupExposure.PlaintextPersonalData,
            ["recent_job_searches.municipality_list"] = BackupExposure.PlaintextPersonalData,
            ["recent_job_searches.region_list"] = BackupExposure.PlaintextPersonalData,
            ["recent_job_searches.employment_type_list"] = BackupExposure.PlaintextPersonalData,
            ["recent_job_searches.worktime_extent_list"] = BackupExposure.PlaintextPersonalData,

            // Derived from the criteria above — a pseudonym of them (Recital 26).
            ["recent_job_searches.filter_hash"] = BackupExposure.PlaintextPersonalData,

            // ══ resumes / resume_files / resume_versions ══════════════════════════════════════
            // resumes.* are the DEK-FREE denormalised projections #1285 is named for. They are not
            // an oversight: they exist because ResumeVersion.Content is encrypted and unsearchable.
            ["resumes.name"] = BackupExposure.PlaintextPersonalData,
            ["resumes.latest_role"] = BackupExposure.PlaintextPersonalData,
            ["resumes.top_skills"] = BackupExposure.PlaintextPersonalData,
            ["resume_files.file_name"] = BackupExposure.PlaintextPersonalData,
            ["resume_finding_statuses.target_fingerprint"] = BackupExposure.PlaintextPersonalData,
            ["resumes.origin"] = BackupExposure.NoPersonalData,
            ["resumes.template"] = BackupExposure.NoPersonalData,
            ["resumes.template_accent"] = BackupExposure.NoPersonalData,
            ["resumes.template_font"] = BackupExposure.NoPersonalData,
            ["resumes.template_density"] = BackupExposure.NoPersonalData,
            ["resumes.template_photo_shape"] = BackupExposure.NoPersonalData,
            ["resumes.reviewed_rubric_version"] = BackupExposure.NoPersonalData,
            ["resume_files.content_type"] = BackupExposure.NoPersonalData,
            ["resume_files.pnr_consent_dialog_version"] = BackupExposure.NoPersonalData,
            ["resume_finding_statuses.criterion_id"] = BackupExposure.NoPersonalData,
            ["resume_finding_statuses.rubric_version"] = BackupExposure.NoPersonalData,
            ["resume_finding_statuses.status"] = BackupExposure.NoPersonalData,
            ["resume_versions.kind"] = BackupExposure.NoPersonalData,

            // The ADMISSION RULE's SECOND clause, and the only column in this registry that uses
            // it: the write path is retired and the population was counted at zero at the Form-B
            // cutover (ADR 0049 Beslut 5 steg 3, #507a/#482 — `content_enc IS NULL AND content IS
            // NOT NULL` converged to 0, then a fitness-gated migration nulled the column). It is
            // an inert read-only shadow until Beslut 5 steg 4 drops it physically.
            ["resume_versions.content"] = BackupExposure.NoPersonalData,

            // ══ saved_searches ════════════════════════════════════════════════════════════════
            ["saved_searches.name"] = BackupExposure.PlaintextPersonalData,
            ["saved_searches.criteria"] = BackupExposure.PlaintextPersonalData,

            // ══ user_job_ad_matches ═══════════════════════════════════════════════════════════
            // The concept ids are a closed domain, but WHICH of them matched is derived from her
            // CV, so the set is about her.
            ["user_job_ad_matches.matched_skill_concept_ids"] = BackupExposure.PlaintextPersonalData,
            ["user_job_ad_matches.grade"] = BackupExposure.NoPersonalData,
            ["user_job_ad_matches.notification_status"] = BackupExposure.NoPersonalData,

            // ══ user_data_keys ════════════════════════════════════════════════════════════════
            // ⚠ NoPersonalData is NOT "harmless" here, and this is the one place that distinction
            // matters most. A wrapped DEK carries no personal data OF ITS OWN — but ADR 0125 exists
            // because it travels in the same artefact as the ciphertext it opens, which is the
            // whole reason the backup is split into `main/` and `deks/`. Classified by what the
            // COLUMN holds; the risk it carries is the ADR's subject, not this bucket's.
            ["user_data_keys.wrapped_dek"] = BackupExposure.NoPersonalData,
            ["user_data_keys.cmk_key_id"] = BackupExposure.NoPersonalData,

            // ══ taxonomy ══════════════════════════════════════════════════════════════════════
            // Reference data. No user write path reaches any of it.
            ["taxonomy_concepts.concept_id"] = BackupExposure.NoPersonalData,
            ["taxonomy_concepts.kind"] = BackupExposure.NoPersonalData,
            ["taxonomy_concepts.label"] = BackupExposure.NoPersonalData,
            ["taxonomy_concepts.parent_concept_id"] = BackupExposure.NoPersonalData,
            ["taxonomy_relations.kind"] = BackupExposure.NoPersonalData,
            ["taxonomy_relations.source_concept_id"] = BackupExposure.NoPersonalData,
            ["taxonomy_relations.related_concept_id"] = BackupExposure.NoPersonalData,
            ["taxonomy_snapshot_meta.taxonomy_version"] = BackupExposure.NoPersonalData,
        };

    /// <summary>
    /// The exposed set, DERIVED — the enumeration ADR 0125, the ROPA and ADR 0050 point at instead
    /// of carrying a hand-written copy of.
    /// </summary>
    internal static IReadOnlyList<string> PlaintextPersonalDataColumns { get; } =
    [
        .. Columns
            .Where(kv => kv.Value == BackupExposure.PlaintextPersonalData)
            .Select(kv => kv.Key)
            .Order(StringComparer.Ordinal),
    ];
}
