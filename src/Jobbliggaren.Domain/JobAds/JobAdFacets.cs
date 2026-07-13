namespace Jobbliggaren.Domain.JobAds;

/// <summary>
/// #841 — the seven taxonomy/employer facets an imported ad carries, as parsed by the ACL from the
/// source payload. Parameter and transport type ONLY: it is never persisted as a value object.
/// <see cref="JobAd"/> projects it onto seven flat, indexed columns (the <c>RecentJobSearch</c> /
/// <c>SearchCriteria</c> pattern — VO in as a method parameter, flat mapped state out).
///
/// <para>
/// <b>Why this type exists at all, rather than seven loose parameters.</b> The facets must be written
/// ATOMICALLY with the payload they are parsed from (<see cref="JobAd.SetSourcePayload"/>), so they
/// travel together as one required argument. Seven positional <c>string?</c> parameters would make every
/// transposition a silently compiling bug — and this repo has already paid for exactly that confusion
/// (the <c>worktime_extent</c> column reads the <c>working_hours_type</c> payload key; see
/// <c>JobAdConfiguration</c>). Construct with NAMED arguments.
/// </para>
///
/// <para>
/// <b>THE EMPTY-STRING INVARIANT — read this before changing anything.</b> Postgres' <c>->>'concept_id'</c>
/// yields SQL <c>NULL</c> for a missing key; a C# parser naturally yields <c>""</c>. And <c>""</c> IS NOT
/// NULL, so an empty string would enter the seven partial <c>WHERE ... IS NOT NULL</c> indexes and then
/// match nothing, forever, silently, with green tests. That is #841's own failure mode — a value that
/// looks present and is functionally absent — walking back in through the front door of its own fix.
/// The constructor therefore normalises blank to <see langword="null"/> as a TYPE invariant, not as an
/// ACL convention: the ACL cannot forget it, because the type will not let it.
/// </para>
/// </summary>
public sealed record JobAdFacets
{
    /// <summary>
    /// The named absence. A manually created ad (<see cref="JobAd.Create"/>) has no source payload and
    /// therefore no source facets — NULL is the true value, and the seven partial indexes are
    /// <c>WHERE ... IS NOT NULL</c> precisely because null-sparsity is expected. Passing this rather than
    /// seven nulls makes the absence deliberate rather than accidental.
    /// </summary>
    public static readonly JobAdFacets None = new(
        ssykConceptId: null,
        occupationGroupConceptId: null,
        municipalityConceptId: null,
        regionConceptId: null,
        employmentTypeConceptId: null,
        worktimeExtentConceptId: null,
        organizationNumber: null);

    public JobAdFacets(
        string? ssykConceptId,
        string? occupationGroupConceptId,
        string? municipalityConceptId,
        string? regionConceptId,
        string? employmentTypeConceptId,
        string? worktimeExtentConceptId,
        string? organizationNumber)
    {
        SsykConceptId = Normalize(ssykConceptId);
        OccupationGroupConceptId = Normalize(occupationGroupConceptId);
        MunicipalityConceptId = Normalize(municipalityConceptId);
        RegionConceptId = Normalize(regionConceptId);
        EmploymentTypeConceptId = Normalize(employmentTypeConceptId);
        WorktimeExtentConceptId = Normalize(worktimeExtentConceptId);
        OrganizationNumber = Normalize(organizationNumber);
    }

    /// <summary>ssyk-level-4 occupation concept (payload: <c>occupation.concept_id</c>, NESTED).</summary>
    public string? SsykConceptId { get; }

    /// <summary>Yrkesgrupp (payload: <c>occupation_group.concept_id</c>, TOP-LEVEL).</summary>
    public string? OccupationGroupConceptId { get; }

    /// <summary>Kommun (payload: <c>workplace_address.municipality_concept_id</c>).</summary>
    public string? MunicipalityConceptId { get; }

    /// <summary>Län (payload: <c>workplace_address.region_concept_id</c>).</summary>
    public string? RegionConceptId { get; }

    /// <summary>Anställningsform (payload: <c>employment_type.concept_id</c>, TOP-LEVEL).</summary>
    public string? EmploymentTypeConceptId { get; }

    /// <summary>
    /// Omfattning/arbetstid. NAME GAP, deliberate: the taxonomy type (and therefore the column, per ADR
    /// 0067 Beslut 2) is <c>worktime-extent</c>, but the payload key is <c>working_hours_type</c>.
    /// </summary>
    public string? WorktimeExtentConceptId { get; }

    /// <summary>
    /// The employer's organisation number (payload: <c>employer.organization_number</c>, NESTED) — the
    /// canonical follow/attribution key (ADR 0087; no fuzzy name matching, the "Volvo×20" trap).
    ///
    /// <para>
    /// <b>PII (highest priority, CLAUDE.md §5).</b> A Swedish sole proprietorship's org.nr IS the owner's
    /// personnummer, in plaintext. It is never logged and never surfaced un-flagged: consumers mask via
    /// <c>OrganizationNumber.IsPersonnummerShaped</c> at the display boundary (ADR 0087 D8(c)). The
    /// build-time guard is <c>JobAdPublicSurfaceGuardTests</c>: it fail-closed-classifies the aggregate's
    /// public surface AND bans <c>{@…}</c> destructuring anywhere in <c>src/</c>. (NOT
    /// <c>OrganizationNumberSurfacingGuardTests</c> — an earlier draft of this comment said so, and it
    /// was false: that class runs a TOKEN scan over an allowlist of paths, and a destructured record
    /// carries none of its tokens. Citing a real class that does not hold the invariant is worse than
    /// citing one that does not exist — the real name makes the claim look verified.)
    /// </para>
    /// </summary>
    public string? OrganizationNumber { get; }

    /// <summary>True when the payload carried no facet at all (parity <see cref="None"/>).</summary>
    public bool IsEmpty =>
        SsykConceptId is null
        && OccupationGroupConceptId is null
        && MunicipalityConceptId is null
        && RegionConceptId is null
        && EmploymentTypeConceptId is null
        && WorktimeExtentConceptId is null
        && OrganizationNumber is null;

    /// <summary>
    /// REDACTED on purpose. A record's compiler-generated <c>ToString()</c> prints every public member —
    /// so <c>LogWarning("bad facets {Facets}", facets)</c>, with NO <c>@</c> anywhere, would print the
    /// <see cref="OrganizationNumber"/> straight into the log via MEL's default formatting. That form
    /// slips past BOTH guards: the destructuring scan sees no <c>{@</c>, and the org.nr token scan sees no
    /// org.nr token in the template. Same lesson as the guard that missed <c>{@jobAd}</c> — the hole is in
    /// the FORM, not the spelling — one level down, in the framework's default rendering.
    ///
    /// <para>
    /// Overriding it makes the leak structurally impossible rather than guard-dependent. A sole
    /// proprietor's org.nr IS a personnummer (ADR 0087 D8(c); CLAUDE.md §5, highest priority). Found by
    /// <c>security-auditor</c>.
    /// </para>
    /// </summary>
    public override string ToString() =>
        IsEmpty ? "JobAdFacets(empty)" : "JobAdFacets(redacted — carries an organisation number)";

    // Blank -> null. See the empty-string invariant above: this is the difference between a row the
    // partial index excludes (correct) and a row the index contains but no IN (...) list can ever match.
    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
