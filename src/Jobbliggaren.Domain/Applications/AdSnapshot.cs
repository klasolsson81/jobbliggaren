namespace Jobbliggaren.Domain.Applications;

/// <summary>
/// Frozen copy of a Platsbanken <c>JobAd</c>'s text, captured onto the
/// Application aggregate at apply-time (issue #315, ADR 0086). Owned value
/// object, parallel to <see cref="ManualPosting"/>. Solves: the employer
/// archives the ad after the deadline, but the user is interviewed weeks later —
/// the source material must survive the ad's whole lifecycle. Since #892 that
/// includes Art. 17 erasure: the read paths swap summary identity to this
/// snapshot when the ad is an Erased tombstone (list/pipeline/report/detail —
/// CTO R1), so the applicant's preserved record survives the erasure.
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
    /// <see cref="WithoutAdBody"/> on a terminal transition (retention /
    /// GDPR data-minimisation, ADR 0086 D3).</summary>
    public string? Description { get; }

    /// <summary>
    /// The recruiter contacts frozen at apply-time (#842 Tier A, CTO re-bind R1(d)) — the
    /// follow-up person for THIS application. Copied from the post-scrub
    /// <c>JobAd.Contacts</c>; null when the ad held none at capture (including an ad already
    /// archived at apply-time, whose contacts retention had cleared — b1's accepted
    /// consequence). The funnel never rewrites a snapshot, so an erasure here is durable by
    /// construction — this column is the erasure command's surgical arm
    /// (<see cref="Application.EraseAdSnapshotContacts"/>), the proportionate remedy that removes
    /// the recruiter's contact WITHOUT destroying the applicant's own record. Dropped together
    /// with the body on a terminal transition (<see cref="WithoutAdBody"/> — the follow-up
    /// purpose is spent).
    /// </summary>
    public JobAds.AdContacts? Contacts { get; }

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
        JobAds.AdContacts? contacts,
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
        Contacts = contacts;
        CapturedAt = capturedAt;
    }

    /// <summary>
    /// Captures a snapshot from already-validated JobAd data. No
    /// <c>Result&lt;T&gt;</c> — captured data cannot fail validation, unlike the
    /// user-input <see cref="ManualPosting.Create"/>.
    /// <paramref name="municipalityConceptId"/> is the JobAd's raw municipality
    /// concept-id (or null); <paramref name="description"/> is the sanitised
    /// <c>JobAd.Description</c>; <paramref name="contacts"/> is the post-scrub
    /// <c>JobAd.Contacts</c> (#842 Tier A — the snapshot cannot capture what the
    /// aggregate does not hold).
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
        JobAds.AdContacts? contacts,
        DateTimeOffset capturedAt) =>
        new(title, company, municipalityConceptId, url, source, publishedAt, expiresAt,
            description, contacts, capturedAt);

    /// <summary>
    /// Retention / GDPR data-minimisation (ADR 0086 D3 + #842 Tier A re-bind R4(b)): returns a
    /// copy with the ad BODY dropped — the bulky <see cref="Description"/> AND the recruiter
    /// <see cref="Contacts"/> — keeping the minimal stats/identity metadata
    /// (title/company/municipality/url/dates/capturedAt). The follow-up purpose ends when the
    /// application reaches a terminal status; there is nobody left to contact, so the contact
    /// block goes with the body: the SAME rule, on the SAME method, that already dropped the
    /// body ("zero new machinery" — the retention rule for this data class was already written).
    /// Idempotent — an already-minimised snapshot returns itself unchanged. Invoked by
    /// <see cref="Application.TransitionTo"/> on a terminal transition
    /// (Accepted/Rejected/Withdrawn) only. (Renamed from <c>WithoutDescription()</c>, whose name
    /// stopped describing what it drops.)
    /// </summary>
    public AdSnapshot WithoutAdBody() =>
        Description is null && Contacts is null
            ? this
            : new AdSnapshot(
                Title, Company, MunicipalityConceptId, Url, Source, PublishedAt, ExpiresAt,
                description: null, contacts: null, CapturedAt);

    /// <summary>
    /// #842 Tier A surgical erasure (b1 §4.4, T2 CTO 2026-07-16): a copy with ONLY the recruiter
    /// contacts removed — the applicant's own record (title/company/body/url) is untouched. The
    /// proportionality win the whole re-bind was for: we erase HER data without destroying the
    /// applicant's evidence. Idempotent.
    /// </summary>
    public AdSnapshot WithoutContacts() =>
        Contacts is null
            ? this
            : new AdSnapshot(
                Title, Company, MunicipalityConceptId, Url, Source, PublishedAt, ExpiresAt,
                Description, contacts: null, CapturedAt);
}
