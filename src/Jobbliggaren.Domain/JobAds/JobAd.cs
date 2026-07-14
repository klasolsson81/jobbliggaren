using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds.Events;

namespace Jobbliggaren.Domain.JobAds;

public sealed class JobAd : AggregateRoot<JobAdId>
{
    public string Title { get; private set; } = null!;
    public Company Company { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public string Url { get; private set; } = null!;
    public JobSource Source { get; private set; } = null!;
    public JobAdStatus Status { get; private set; } = null!;
    public DateTimeOffset PublishedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // #821 — JobAd has NO soft-delete axis. Status (Active | Expired | Archived) is the
    // SOLE lifecycle axis: Archive() is the only lifecycle transition, and non-Active ads
    // are excluded from end-user views by JobAdSearchComposition.ApplyFilter (a SPOT
    // Status == Active predicate, ADR 0032-amendment 2026-05-23) — never by a query filter.
    // The retired DeletedAt column + its vacuous HasQueryFilter had no writer for two
    // releases and caused a real defect: the Applications read path delegated "the ad is
    // gone" to it, so PreservedAdPanel (ADR 0086) never rendered in production (#805-3).

    // ADR 0032 §4 — extern referens för imported JobAds. null för Manual.
    public ExternalReference? External { get; private set; }

    // ADR 0032 §4 — raw JobTech-payload för debug/replay (jsonb i DB).
    // RETENTION: PurgeStaleRawPayloadsJob nulls this 30 days after published_at
    // (GDPR Art. 5(1)(c)/(e), ADR 0032 §8). It is the ONLY column on this aggregate
    // with a TTL — which is why nothing durable may be derived from it in the
    // database. See SetSourcePayload.
    public string? RawPayload { get; private set; }

    // #841 — the seven SOURCE FACETS. Ordinary columns, written in C# from the ACL's
    // parse of the payload, ATOMICALLY with RawPayload (SetSourcePayload below).
    //
    // Until 2026-07-13 these were Postgres STORED generated columns derived from
    // raw_payload. Postgres recomputes a stored generated column on every UPDATE of
    // its base, so the 30-day raw_payload purge silently nulled all seven — and the
    // 02:00 sync rewrote the payload and resurrected them. Net effect, proven against
    // real Postgres: filtered search, the per-user matching engine and the
    // company-watch scan dropped still-ACTIVE ads ~21.5 h out of every 24, every day.
    // They ARE the "sanitized fields" ADR 0032 §8 promised to keep INDEFINITELY; they
    // must outlive the payload they were parsed from. That is what this shape buys.
    public string? SsykConceptId { get; private set; }
    public string? OccupationGroupConceptId { get; private set; }
    public string? MunicipalityConceptId { get; private set; }
    public string? RegionConceptId { get; private set; }
    public string? EmploymentTypeConceptId { get; private set; }
    public string? WorktimeExtentConceptId { get; private set; }

    // PII, highest priority (CLAUDE.md §5): a sole proprietor's org.nr IS a personnummer
    // in plaintext. Never logged, never surfaced un-flagged — consumers mask via
    // OrganizationNumber.IsPersonnummerShaped at the display boundary (ADR 0087 D8(c)).
    // Guarded at build time by JobAdPublicSurfaceGuardTests + OrganizationNumberSurfacingGuardTests.
    public string? OrganizationNumber { get; private set; }

    // F4-4 (ADR 0071/0074 Path C) — deterministic keyword/skill extraction
    // (jsonb i DB). NULL = aldrig extraherat (alla rader importerade före F4-4
    // tills backfillen kör); non-null (inkl. tom) = extraherat. Skillnaden bär
    // backfill-idempotensen via den STORED genererade extracted_lexemes-skuggan
    // (NULL ⟺ extracted_terms NULL). Skrivs aktivt i C# vid ingest + backfill.
    // (extracted_lexemes IS still a generated column — legitimately: it derives from
    // extracted_terms, a column with NO TTL. The #841 rule is not "no generated
    // columns"; it is "nothing durable may be derived from raw_payload".)
    public ExtractedTerms? ExtractedTerms { get; private set; }

    // EF Core constructor
    private JobAd() { }

    private JobAd(
        JobAdId id,
        string title,
        Company company,
        string description,
        string url,
        JobSource source,
        DateTimeOffset publishedAt,
        DateTimeOffset? expiresAt,
        DateTimeOffset createdAt) : base(id)
    {
        Title = title;
        Company = company;
        Description = description;
        Url = url;
        Source = source;
        Status = JobAdStatus.Active;
        PublishedAt = publishedAt;
        ExpiresAt = expiresAt;
        CreatedAt = createdAt;
    }

    public static Result<JobAd> Create(
        string? title,
        Company company,
        string? description,
        string? url,
        JobSource source,
        DateTimeOffset publishedAt,
        DateTimeOffset? expiresAt,
        IDateTimeProvider clock)
    {
        var validation = ValidateCore(title, description, url, publishedAt, expiresAt);
        if (validation.IsFailure)
            return Result.Failure<JobAd>(validation.Error);

        var now = clock.UtcNow;
        var id = JobAdId.New();
        var jobAd = new JobAd(id, title!.Trim(), company, description!.Trim(),
                              url!, source, publishedAt, expiresAt, now);
        jobAd.RaiseDomainEvent(new JobAdCreatedDomainEvent(id, title.Trim(), now));
        return Result.Success(jobAd);
    }

    // ADR 0032 §4 — factory för imported JobAds. ExternalReference + RawPayload
    // är obligatoriska. Idempotency hanteras via UNIQUE-index på (Source, ExternalId)
    // + DbUpdateException-catch i upsert-handler (P8c).
    public static Result<JobAd> Import(
        string? title,
        Company company,
        string? description,
        string? url,
        ExternalReference external,
        string? rawPayload,
        JobAdFacets facets,
        DateTimeOffset publishedAt,
        DateTimeOffset? expiresAt,
        IDateTimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(external);
        ArgumentNullException.ThrowIfNull(facets);

        var validation = ValidateCore(title, description, url, publishedAt, expiresAt);
        if (validation.IsFailure)
            return Result.Failure<JobAd>(validation.Error);

        if (string.IsNullOrWhiteSpace(rawPayload))
            return Result.Failure<JobAd>(
                DomainError.Validation("JobAd.RawPayloadRequired",
                    "RawPayload är obligatorisk för importerade annonser."));

        var now = clock.UtcNow;
        var id = JobAdId.New();
        var jobAd = new JobAd(id, title!.Trim(), company, description!.Trim(),
                              url!, external.Source, publishedAt, expiresAt, now)
        {
            External = external,
        };
        jobAd.SetSourcePayload(rawPayload, facets);
        jobAd.RaiseDomainEvent(new JobAdImportedDomainEvent(
            id, external.Source.Value, external.ExternalId, title.Trim(), now));
        return Result.Success(jobAd);
    }

    public Result Archive(IDateTimeProvider clock)
    {
        // #842 — Erased is terminal and outranks every other transition. The two BULK archival
        // writers (ExpireJobAdsJob, JobAdSnapshotMissTracker) bypass the aggregate via
        // ExecuteUpdateAsync, but both already scope on `Status == Active`, so neither can reach
        // an erased row. This guard covers the aggregate path (ArchiveExternalJobAdCommand).
        if (Status == JobAdStatus.Erased)
            return Result.Failure(
                DomainError.Validation("JobAd.Erased", "Annonsen är raderad (GDPR art. 17)."));

        if (Status == JobAdStatus.Archived)
            return Result.Failure(
                DomainError.Validation("JobAd.AlreadyArchived", "Annonsen är redan arkiverad."));

        Status = JobAdStatus.Archived;
        RaiseDomainEvent(new JobAdArchivedDomainEvent(Id, clock.UtcNow));
        return Result.Success();
    }

    /// <summary>
    /// GDPR Art. 17 (ADR 0106 Tier B, #842) — erase the ad. Removes the carrier, not a detected
    /// string: every free-text field the recruiter can appear in goes at once, so completeness
    /// needs no detector, no recall estimate, and no obfuscation argument. Terminal.
    /// </summary>
    /// <remarks>
    /// <b>Why the whole record.</b> The recruiter's <i>name</i> in the ad body is unreachable by
    /// regex and unreachable by any structured field, and it is independently full-text
    /// searchable (`search_vector` = <c>to_tsvector('swedish', title || description)</c>). Erasing
    /// the carrier is the only answer to "erase my name from your copy" that we can prove.
    /// <para>
    /// <b>What this clears, and why each one.</b> <see cref="Title"/> + <see cref="Description"/>
    /// (the FTS source — the STORED generated <c>search_vector</c> recomputes to empty by itself,
    /// PG18 §5.4); <see cref="Company"/> (an enskild firma's name IS a person's name — see
    /// <see cref="JobAds.Company.Erased"/>); <see cref="Url"/> (a JobTech ad URL is not PII, but a
    /// tombstone that still links to the live ad is not a tombstone); <see cref="RawPayload"/>;
    /// <see cref="OrganizationNumber"/> — <b>explicitly, and it used to be free.</b> The seven facet
    /// columns were STORED GENERATED from <c>raw_payload</c>, so nulling the payload nulled the
    /// org.nr and this method never knew the column existed. #841 materialised them, the coincidence
    /// ended, and a sole proprietor's org.nr <b>is a personnummer</b> (CLAUDE.md §5). And
    /// <see cref="ExtractedTerms"/>, where the recruiter's name survives <i>verbatim</i> as a
    /// Display/MatchedOn surface form (F-B — it is C#-written, so it does NOT self-heal on a
    /// description write; the STORED <c>extracted_lexemes</c> shadow follows it).
    /// </para>
    /// <para>
    /// <b>Empty, not null, for the terms.</b> <c>ExtractedTerms == null</c> means "never
    /// extracted" and is what carries BackfillJobAdExtractedTermsJob's idempotence
    /// (<c>extracted_lexemes IS NULL</c>). Nulling it here would make an erased ad look
    /// un-extracted, and the backfill would pick it up. <see cref="ExtractedTerms.Empty"/> is
    /// also simply the truth: re-running the extractor over the erased (empty) text yields
    /// exactly zero terms. The state is what the funnel would produce — which is the invariant
    /// this aggregate keeps having to relearn.
    /// </para>
    /// <para>
    /// <b>Tombstone, not hard delete.</b> <c>applications.job_ad_id</c> FKs point here, and the
    /// row is what refuses the re-import: <see cref="UpdateFromSource"/> returns a failure on an
    /// erased ad, keyed by the existing <c>(source, external_id)</c> UNIQUE tuple. The tombstone
    /// therefore stores <b>no</b> personal data — a source, an external id and a status. That is
    /// what lets us refuse a suppression ledger, which would have had us store the recruiter's
    /// email in order to keep erasing it.
    /// </para>
    /// </remarks>
    public Result Erase(IDateTimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status == JobAdStatus.Erased)
            return Result.Failure(
                DomainError.Validation("JobAd.AlreadyErased", "Annonsen är redan raderad."));

        Title = string.Empty;
        Description = string.Empty;
        Url = string.Empty;
        Company = Company.Erased;
        RawPayload = null;

        // EXPLICITLY, and #841 is the reason. While organization_number was a STORED GENERATED
        // column derived from raw_payload, nulling the payload nulled the org.nr for free — and this
        // method never knew the column existed. #841 materialised it into an ordinary, ingest-written
        // column that persists indefinitely (JobAdConfiguration: "Any Art. 17 erasure path must now
        // clear this column EXPLICITLY; it will not vanish on its own"). A sole proprietor's org.nr
        // IS a personnummer (CLAUDE.md §5). Pinned by the tombstone-shape fitness test, which is
        // derived from ErasureCascadeRegistry — the claim and the proof cannot drift apart.
        //
        // The six *_concept_id facets stay: they are Arbetsförmedlingen taxonomy codes, classified
        // NotRecruiterData, and a tombstone that keeps its SSYK code discloses nothing about her.
        OrganizationNumber = null;

        ExtractedTerms = ExtractedTerms.Empty;
        Status = JobAdStatus.Erased;

        RaiseDomainEvent(new JobAdErasedDomainEvent(Id, External?.ExternalId, clock.UtcNow));
        return Result.Success();
    }

    // ADR 0032 §4 — state-transition vid Stream-update eller Snapshot-upsert
    // mot redan-existerande JobAd. Refreshar mutable fält + raw_payload.
    // Inga domain events — sync-job-runs auditeras aggregerat via
    // JobAdsSyncedDomainEvent (ADR 0032 §8).
    public Result UpdateFromSource(
        string? title,
        string? description,
        string? url,
        string? rawPayload,
        JobAdFacets facets,
        DateTimeOffset? expiresAt)
    {
        ArgumentNullException.ThrowIfNull(facets);

        if (External is null)
            return Result.Failure(
                DomainError.Validation("JobAd.NotImported",
                    "UpdateFromSource får bara anropas på importerade annonser."));

        // #842 / ADR 0106 D7 — THE RE-IMPORT TOMBSTONE, and the single line that makes Art. 17
        // erasure durable. The nightly snapshot sync (0 2 * * *) and the 10-minute stream both
        // funnel into UpsertExternalJobAdCommandHandler, which has no unchanged/hash
        // short-circuit and reassigns Title/Description/Url/RawPayload UNCONDITIONALLY. Without
        // this refusal, an erased ad that is still listed at Arbetsförmedlingen walks straight
        // back in within ≤10 minutes — we would send the recruiter an Art. 12(3) confirmation and
        // then restore her data overnight. That is F-A, and it is the defect that made the old
        // purger worthless.
        //
        // It is placed HERE, in the aggregate, and BEFORE ValidateCore — deliberately (CLAUDE.md
        // §2.2). Do NOT "fix" this by adding a status predicate to the handler's reload query:
        // that moves an invariant out of the aggregate into a caller, and the next caller will
        // not have it. Erased is terminal; nothing re-animates the row.
        if (Status == JobAdStatus.Erased)
            return Result.Failure(
                DomainError.Validation("JobAd.Erased",
                    "Annonsen är raderad (GDPR art. 17) och hämtas inte in igen."));

        var validation = ValidateCore(title, description, url, PublishedAt, expiresAt);
        if (validation.IsFailure)
            return validation;

        if (string.IsNullOrWhiteSpace(rawPayload))
            return Result.Failure(
                DomainError.Validation("JobAd.RawPayloadRequired",
                    "RawPayload är obligatorisk vid update."));

        Title = title!.Trim();
        Description = description!.Trim();
        Url = url!;
        ExpiresAt = expiresAt;
        SetSourcePayload(rawPayload, facets);

        return Result.Success();
    }

    /// <summary>
    /// #841 — THE drift guard, and it is a type-system guarantee rather than a convention.
    ///
    /// <para>
    /// The payload and the seven facets parsed from it are written ATOMICALLY, in one place. They are two
    /// halves of one fact ("this is what the source said"), and the entire defect this method exists to
    /// kill was two write paths disagreeing about a derived value. Because <see cref="JobAdFacets"/> is a
    /// REQUIRED parameter of the only two members that can reach here (<see cref="Import"/> and
    /// <see cref="UpdateFromSource"/> — one production call site each, both in
    /// <c>UpsertExternalJobAdCommandHandler</c>), it is not possible to COMPILE a payload write that omits
    /// the facets. Not merely unlikely: impossible.
    /// </para>
    ///
    /// <para>
    /// Contrast <see cref="SetExtractedTerms"/>, which is a SEPARATE method the caller has to remember —
    /// the weakness this deliberately does not copy (tracked as #874).
    /// </para>
    ///
    /// <para>
    /// <b>The one route around this guard</b> is <c>ExecuteUpdateAsync</c>, which bypasses the aggregate
    /// entirely. Exactly one such writer of <c>RawPayload</c> exists — <c>PurgeStaleRawPayloadsJob</c>,
    /// which writes NULL and, after #841, leaves the seven standing. That exclusivity is not a convention
    /// either: <c>JobAdRawPayloadDerivationGuardTests</c> fails the build if a second one appears.
    /// </para>
    /// </summary>
    private void SetSourcePayload(string rawPayload, JobAdFacets facets)
    {
        RawPayload = rawPayload;
        SsykConceptId = facets.SsykConceptId;
        OccupationGroupConceptId = facets.OccupationGroupConceptId;
        MunicipalityConceptId = facets.MunicipalityConceptId;
        RegionConceptId = facets.RegionConceptId;
        EmploymentTypeConceptId = facets.EmploymentTypeConceptId;
        WorktimeExtentConceptId = facets.WorktimeExtentConceptId;
        OrganizationNumber = facets.OrganizationNumber;
    }

    // F4-4 — set the deterministic keyword/skill extraction (ADR 0071/0074).
    // Invoked by the ingest hook (UpsertExternalJobAd, both Add + Update paths)
    // and the local backfill. Idempotent: a re-extraction over the same text
    // yields an equal value object. No domain event — extraction is derived
    // state, not a business transition (parity UpdateFromSource).
    //
    // KNOWN ASYMMETRY, deliberate and tracked (#874). Unlike the source facets — which
    // SetSourcePayload writes atomically with the payload they are parsed from, so
    // omitting them cannot compile — this is a SEPARATE method the caller must remember
    // to call after UpdateFromSource writes the very Title/Description it derives from.
    // A future write path could update the text and leave the terms stale. Same failure
    // class as #841, one notch milder: title/description carry no retention TTL, so the
    // failure mode is staleness, not destruction — nothing silently zeroes these.
    public void SetExtractedTerms(ExtractedTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        ExtractedTerms = terms;
    }

    private static Result ValidateCore(
        string? title,
        string? description,
        string? url,
        DateTimeOffset publishedAt,
        DateTimeOffset? expiresAt)
    {
        if (string.IsNullOrWhiteSpace(title))
            return Result.Failure(
                DomainError.Validation("JobAd.TitleRequired", "Titel är obligatorisk."));
        if (title.Length > 300)
            return Result.Failure(
                DomainError.Validation("JobAd.TitleTooLong", "Titel får vara max 300 tecken."));
        if (string.IsNullOrWhiteSpace(description))
            return Result.Failure(
                DomainError.Validation("JobAd.DescriptionRequired", "Beskrivning är obligatorisk."));
        // TD-80 — scheme-whitelist (http/https only). `Uri.TryCreate(UriKind.Absolute)`
        // accepterar `javascript:`/`data:`/`vbscript:`/`file:` som vid render i
        // `<a href={url}>` blir XSS-vektor i autentiserad session (cookie-stöld
        // → GDPR Art. 32). Saltzer/Schroeder 1975 default-deny + OWASP A01:2021
        // (Broken Access Control) — whitelist > blacklist. Source: security-
        // auditor F2-P10 frontend-review 2026-05-13.
        if (string.IsNullOrWhiteSpace(url)
            || !Uri.TryCreate(url, UriKind.Absolute, out var parsedUri)
            || (parsedUri.Scheme != Uri.UriSchemeHttp
                && parsedUri.Scheme != Uri.UriSchemeHttps))
            return Result.Failure(
                DomainError.Validation("JobAd.UrlInvalid",
                    "URL måste vara en giltig http(s)-URL."));
        if (expiresAt.HasValue && expiresAt.Value <= publishedAt)
            return Result.Failure(
                DomainError.Validation("JobAd.InvalidDates", "ExpiresAt måste vara efter PublishedAt."));

        return Result.Success();
    }
}
