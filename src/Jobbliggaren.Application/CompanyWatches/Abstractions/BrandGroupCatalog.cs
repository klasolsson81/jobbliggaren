namespace Jobbliggaren.Application.CompanyWatches.Abstractions;

/// <summary>
/// #311 PR-5 (ADR 0087 D4) — the curated brand-group catalogue as the Application layer consumes it:
/// a slug → <see cref="BrandGroup"/> map, where each group carries a display name and an EXPLICIT list
/// of member org.nrs (never name-matched — the "Volvo×20" trap is the whole reason org.nr is
/// canonical). This is deploy-versioned reference data, curated only via PR; there is no runtime
/// mutation surface.
///
/// <para>
/// Referential integrity (well-formed slugs, non-empty member lists, well-formed member org.nrs, no
/// personnummer-shaped members, no duplicate group ids) is enforced fail-loud by the Infrastructure
/// loader at host build — a malformed catalogue never becomes a running host. An EMPTY catalogue
/// (zero groups) is a LEGAL state (the mechanism ships before any group is curated), unlike some
/// sibling loaders whose empty level is a bug.
/// </para>
/// </summary>
public sealed class BrandGroupCatalog
{
    private readonly IReadOnlyDictionary<string, BrandGroup> _groupsById;
    private readonly IReadOnlyCollection<BrandGroup> _groups;

    public BrandGroupCatalog(string version, IReadOnlyDictionary<string, BrandGroup> groupsById)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(groupsById);

        Version = version;
        _groupsById = groupsById;
        // Materialise once (IReadOnlyDictionary.Values is typed IEnumerable<T> — a runtime cast would be
        // brittle against a non-Dictionary implementation).
        _groups = groupsById.Values.ToArray();
    }

    /// <summary>Dataset version stamp (e.g. "1.0") — surfaced so a stale cache is diagnosable.</summary>
    public string Version { get; }

    /// <summary>All curated groups (possibly empty). Order is unspecified.</summary>
    public IReadOnlyCollection<BrandGroup> Groups => _groups;

    /// <summary>
    /// Resolves a slug to its group, or <see langword="null"/> when the slug is not curated — the
    /// follow handler turns a null into a <c>DomainError.NotFound</c>, and the scan/read paths treat an
    /// orphaned slug (a persisted watch whose group was later removed from the catalogue) as zero
    /// members (matches nothing), never an exception.
    /// </summary>
    public BrandGroup? Find(string brandGroupId) =>
        _groupsById.GetValueOrDefault(brandGroupId);
}

/// <summary>
/// A curated brand group: a stable <paramref name="Id"/> slug (the follow key + FE i18n key), a
/// human <paramref name="DisplayName"/> (the SSOT for the group's name — never derived from job_ads),
/// and an explicit list of <paramref name="MemberOrgNrs"/> (public AB org.nrs; never
/// personnummer-shaped — the loader rejects those).
/// </summary>
public sealed record BrandGroup(string Id, string DisplayName, IReadOnlyList<string> MemberOrgNrs)
{
    /// <summary>
    /// Redacting override (#883, fail-closed by <c>OrgNrRecordLoggingGuardTests</c>): a record's
    /// compiler-generated <c>ToString()</c> would print every member org.nr, and MEL renders a plain
    /// <c>{X}</c> placeholder through <c>ToString()</c>. The members here are public legal-entity org.nrs
    /// (the loader rejects personnummer-shaped ones), so this is defense-in-depth rather than a live pnr
    /// risk — but the guard is structural, and redacting-by-default is the correct posture. Keep the id +
    /// display name (both non-PII) so a log line stays debuggable without re-logging the members.
    /// </summary>
    public override string ToString() =>
        $"BrandGroup {{ Id = {Id}, DisplayName = {DisplayName}, Members = <{MemberOrgNrs.Count} redacted> }}";
}
