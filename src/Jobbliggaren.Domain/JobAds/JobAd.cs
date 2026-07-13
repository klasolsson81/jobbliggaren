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
    public string? RawPayload { get; private set; }

    // F4-4 (ADR 0071/0074 Path C) — deterministic keyword/skill extraction
    // (jsonb i DB). NULL = aldrig extraherat (alla rader importerade före F4-4
    // tills backfillen kör); non-null (inkl. tom) = extraherat. Skillnaden bär
    // backfill-idempotensen via den STORED genererade extracted_lexemes-skuggan
    // (NULL ⟺ extracted_terms NULL). Skrivs aktivt i C# vid ingest + backfill —
    // EJ en Postgres generated column (NLP+taxonomi-lookup går inte att uttrycka
    // i SQL, till skillnad mot ssyk_concept_id/search_vector).
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
        DateTimeOffset publishedAt,
        DateTimeOffset? expiresAt,
        IDateTimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(external);

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
            RawPayload = rawPayload,
        };
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
    /// tombstone that still links to the live ad is not a tombstone); <see cref="RawPayload"/>
    /// (which also NULLs the seven raw_payload-derived generated columns, among them
    /// <c>organization_number</c>, which may be a personnummer); and
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
        DateTimeOffset? expiresAt)
    {
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
        RawPayload = rawPayload;

        return Result.Success();
    }

    // F4-4 — set the deterministic keyword/skill extraction (ADR 0071/0074).
    // Invoked by the ingest hook (UpsertExternalJobAd, both Add + Update paths)
    // and the local backfill. Idempotent: a re-extraction over the same text
    // yields an equal value object. No domain event — extraction is derived
    // state, not a business transition (parity UpdateFromSource).
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
