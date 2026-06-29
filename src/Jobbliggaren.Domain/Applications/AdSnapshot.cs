namespace Jobbliggaren.Domain.Applications;

/// <summary>
/// Frozen copy of a Platsbanken <c>JobAd</c>'s text, captured onto the
/// Application aggregate at apply-time (issue #315, ADR 0086). Owned value
/// object, parallel to <see cref="ManualPosting"/>. Solves: the employer
/// archives the ad after the deadline, but the user is interviewed weeks later —
/// the source material must survive even after the JobAd is soft-deleted.
///
/// The snapshot captures the ad's OWN fields (title/company/url/source/dates/body)
/// plus the JobAd's <see cref="MunicipalityConceptId"/> — the raw taxonomy
/// concept-id, NOT a resolved name (ADR 0086 D4, final ruling: concept-id-at-read).
/// The municipality name is resolved on the READ path
/// (<c>GetApplicationByIdQueryHandler</c>) via the taxonomy ACL, keeping the
/// write side free of <c>ITaxonomyReadModel</c> — the project's codified
/// read-side-only invariant for that port (TaxonomyAclLayerTests) stays intact.
/// Capturing the concept-id (not a name) already survives the JobAd disappearing;
/// our local taxonomy (ADR 0043) is the stable resolver.
///
/// Invariant: an <see cref="AdSnapshot"/> exists only on a JobAd-linked
/// application (snapshot ⇒ <c>JobAdId</c>) — enforced structurally by
/// <see cref="Application.CreateFromJobAd"/>, the only writer (the generic
/// <see cref="Application.Create"/> never sets a snapshot). Symmetry with the
/// <see cref="ManualPosting"/> XOR.
///
/// Public Platsbanken metadata about the employer/job, NOT user PII → plaintext,
/// no DEK envelope (ADR 0086 D5 / ADR 0049 Beslut 1: a JobAd has no owning user,
/// so a per-user DEK is semantically impossible; the user↔ad link is already PII
/// via the owner-scoped Application row). Captured data, not user input:
/// <see cref="Capture"/> performs NO validation (the source JobAd was valid when
/// imported) and returns the VO directly, never a <c>Result</c>
/// (dotnet-architect M1).
/// </summary>
public sealed record AdSnapshot
{
    public string Title { get; }
    public string Company { get; }

    /// <summary>The JobAd's municipality ("ort") taxonomy concept-id, captured
    /// raw. Null when the ad carried no municipality. Resolved to a human name
    /// on the read path (ADR 0086 D4); an opaque concept-id is never surfaced to
    /// the user (CLAUDE.md §5).</summary>
    public string? MunicipalityConceptId { get; }

    public string? Url { get; }

    /// <summary>The <c>JobSource</c> name literal ("Platsbanken"/"LinkedIn"/…)
    /// frozen at capture.</summary>
    public string Source { get; }

    public DateTimeOffset PublishedAt { get; }
    public DateTimeOffset? ExpiresAt { get; }

    /// <summary>The full ad body, copied from the sanitised
    /// <c>JobAd.Description</c> (NEVER <c>raw_payload</c>). Nullable: dropped by
    /// <see cref="WithoutDescription"/> on a terminal transition (retention /
    /// GDPR data-minimisation, ADR 0086 D3).</summary>
    public string? Description { get; }

    /// <summary>When the snapshot was taken (apply-time). Distinct from
    /// <c>Application.CreatedAt</c> in intent; they coincide today because the
    /// snapshot is captured at create.</summary>
    public DateTimeOffset CapturedAt { get; }

    private AdSnapshot(
        string title,
        string company,
        string? municipalityConceptId,
        string? url,
        string source,
        DateTimeOffset publishedAt,
        DateTimeOffset? expiresAt,
        string? description,
        DateTimeOffset capturedAt)
    {
        Title = title;
        Company = company;
        MunicipalityConceptId = municipalityConceptId;
        Url = url;
        Source = source;
        PublishedAt = publishedAt;
        ExpiresAt = expiresAt;
        Description = description;
        CapturedAt = capturedAt;
    }

    /// <summary>
    /// Captures a snapshot from already-validated JobAd data. No
    /// <c>Result&lt;T&gt;</c> — captured data cannot fail validation, unlike the
    /// user-input <see cref="ManualPosting.Create"/>.
    /// <paramref name="municipalityConceptId"/> is the JobAd's raw municipality
    /// concept-id (or null); <paramref name="description"/> is the sanitised
    /// <c>JobAd.Description</c>.
    /// </summary>
    public static AdSnapshot Capture(
        string title,
        string company,
        string? municipalityConceptId,
        string? url,
        string source,
        DateTimeOffset publishedAt,
        DateTimeOffset? expiresAt,
        string? description,
        DateTimeOffset capturedAt) =>
        new(title, company, municipalityConceptId, url, source, publishedAt, expiresAt, description, capturedAt);

    /// <summary>
    /// Retention / GDPR data-minimisation (ADR 0086 D3, GDPR Art. 5(1)(c)):
    /// returns a copy with the bulky <see cref="Description"/> dropped, keeping
    /// the minimal stats/identity metadata
    /// (title/company/municipality/url/dates/capturedAt). Idempotent — an
    /// already-minimised snapshot returns itself unchanged. Invoked by
    /// <see cref="Application.TransitionTo"/> on a terminal transition
    /// (Accepted/Rejected/Withdrawn) only.
    /// </summary>
    public AdSnapshot WithoutDescription() =>
        Description is null
            ? this
            : new AdSnapshot(
                Title, Company, MunicipalityConceptId, Url, Source, PublishedAt, ExpiresAt,
                description: null, CapturedAt);
}
