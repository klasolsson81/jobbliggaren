using JobbPilot.Domain.Common;
using JobbPilot.Domain.JobAds.Events;

namespace JobbPilot.Domain.JobAds;

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
    public DateTimeOffset? DeletedAt { get; private set; }

    // ADR 0032 §4 — extern referens för imported JobAds. null för Manual.
    public ExternalReference? External { get; private set; }

    // ADR 0032 §4 — raw JobTech-payload för debug/replay (jsonb i DB).
    public string? RawPayload { get; private set; }

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
        if (Status == JobAdStatus.Archived)
            return Result.Failure(
                DomainError.Validation("JobAd.AlreadyArchived", "Annonsen är redan arkiverad."));

        Status = JobAdStatus.Archived;
        RaiseDomainEvent(new JobAdArchivedDomainEvent(Id, clock.UtcNow));
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
