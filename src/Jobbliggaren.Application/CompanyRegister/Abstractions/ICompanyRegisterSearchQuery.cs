using Jobbliggaren.Application.Common;
using Jobbliggaren.Application.CompanyWatches.Abstractions;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.CompanyWatches;

namespace Jobbliggaren.Application.CompanyRegister.Abstractions;

/// <summary>
/// #560 company-search wave (senior-cto-advisor F1, 2026-07-18) — the GENERAL register search:
/// "browse/search ALL active companies in the local SCB register", with every axis OPTIONAL
/// (SNI, municipality, name prefix, org.nr) and browse-all as the legal no-filter default.
///
/// <para>
/// <b>Deliberately a SEPARATE port from <see cref="ICompanyWatchBrowseQuery"/> — do not merge
/// them (CTO F1, binding).</b> The criterion browse answers "which companies match this SAVED
/// criterion?" where an empty axis is CORRUPTION and throws; this port answers "browse
/// everything, narrowed by whatever the user typed" where an absent axis means the clause is
/// OMITTED from the WHERE — never bound as an empty array, because <c>sni_codes &amp;&amp; '{}'</c>
/// is FALSE and would silently return zero rows (the #805-3 failure shape). Opposite
/// absent-axis semantics is the proof they are different queries; a shared predicate builder
/// would need a mode flag, and the mode flag is the two-responsibilities smell.
/// </para>
///
/// <para>
/// <b>Same firewall as the sibling port (ADR 0091/0043, DPIA C-D4/M-C5):</b> the register is
/// Infrastructure-internal, never a <c>DbSet</c> on <c>IAppDbContext</c> — handlers reach it
/// only through this port, so no handler can join it against personnummer-lookup output. The
/// implementation is raw parametrized SQL for the same reason the sibling's is: the SNI half
/// must be emitted as the array-overlap operator <c>&amp;&amp;</c> (the only shape the GIN
/// index serves), and the name half as a <c>lower(...)</c> prefix (the only shape the
/// functional index serves). Both plans are EXPLAIN-pinned by index NAME.
/// </para>
/// </summary>
public interface ICompanyRegisterSearchQuery
{
    /// <summary>
    /// Returns the page of ACTIVE register companies matching <paramref name="criteria"/>
    /// (<c>status = 'Active'</c> is unconditional — DPIA M-D6), ordered by
    /// <c>company_name, organization_number</c> (a TOTAL order; the column's <c>swedish</c> ICU
    /// collation makes it Å/Ä/Ö-correct).
    ///
    /// <para>
    /// <b><see cref="PagedResult{T}.TotalCount"/> is a PAGINATION QUANTITY, not a magnitude</b> —
    /// it saturates at <see cref="CompanyRegisterSearchCriteria.MaxServableRows"/> so
    /// <c>TotalPages ≤ MaxPage</c> holds by construction (the pager can never advertise a page
    /// the caps reject; see the sibling port for the full argument). The honest headline number
    /// comes from <see cref="CountMatchingAsync"/> with its own product ceiling.
    /// </para>
    /// </summary>
    ValueTask<PagedResult<CompanyBrowseResult>> SearchAsync(
        CompanyRegisterSearchCriteria criteria, CancellationToken cancellationToken);

    /// <summary>
    /// The MAGNITUDE count over the SAME predicate (one predicate authority — the whole reason
    /// this method lives on this port, mirroring the sibling's Fork G3 bind): returns
    /// <c>min(true count, ceiling)</c>. A return value equal to <paramref name="ceiling"/> means
    /// SATURATED and the copy must say "10 000+", never the bare number.
    /// </summary>
    ValueTask<int> CountMatchingAsync(
        CompanyRegisterSearchCriteria criteria, int ceiling, CancellationToken cancellationToken);
}

/// <summary>
/// The validated, NORMALIZED search input — and the SINGLE normalizer for it (house rule: a rule
/// with two normalizers is two rules). <see cref="Create"/> owns trimming, blank-dropping,
/// dedupe, org.nr written-form folding and the personnummer refusal; there is deliberately NO
/// FluentValidation validator for the search queries, so this factory cannot drift from a second
/// authority. Handlers call <see cref="Create"/> and map its <see cref="DomainError"/> straight
/// to 400 via the central mapper.
///
/// <para>
/// <b>Every axis is optional; empty means "do not filter on this axis".</b> A no-axis instance
/// is a legal browse-all (CTO F1). The paging caps are enforced HERE, in the port's input, not
/// only at the edge — the sibling <c>CompanyBrowseCriteria</c> learned that lesson from
/// security-auditor 2026-07-13: an invariant that holds "as long as you came in the front door"
/// is not an invariant, and against a 1,07M-row register an unbounded OFFSET is a DoS surface
/// (§5: an unbounded OFFSET is an unpaginated fetch with a LIMIT on it).
/// </para>
///
/// <para>
/// <b>Personnummer posture (ADR 0087 D8(c), ADR 0088 D4 parity):</b> an org.nr term that is
/// personnummer-shaped is REFUSED with a Validation error — never executed. The register is
/// legal-entities-only (ADR 0091), so the honest result would be zero rows anyway; refusing
/// keeps the posture explicit and the search term out of every downstream surface. The raw
/// org.nr member never leaves the Application boundary un-masked, and <see cref="ToString"/> is
/// redacted (#883) so a <c>{Criteria}</c> MEL placeholder cannot leak it.
/// </para>
/// </summary>
public sealed record CompanyRegisterSearchCriteria
{
    /// <summary>Axis caps — the same sizes the criterion spec allows (SCB's own axis sizes:
    /// 290 kommuner; SNI leaf count is bounded well under 1000).</summary>
    public const int MaxSniCodes = 1000;
    public const int MaxMunicipalityCodes = 290;

    /// <summary>Generous over real legal names; a longer prefix is a paste error, not a search.</summary>
    public const int MaxNamePrefixLength = 100;

    /// <summary>House parity (<c>CompanyBrowseCriteria</c>).</summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// Deep-offset ceiling (parity <c>CompanyBrowseCriteria.MaxPage</c>, same DoS argument —
    /// only worse here, because browse-all matches ALL ~1,07M rows).
    /// </summary>
    public const int MaxPage = 100;

    private CompanyRegisterSearchCriteria(
        IReadOnlyList<string> sniCodes,
        IReadOnlyList<string> municipalityCodes,
        string? namePrefix,
        string? organizationNumber,
        int page,
        int pageSize)
    {
        SniCodes = sniCodes;
        MunicipalityCodes = municipalityCodes;
        NamePrefix = namePrefix;
        OrganizationNumber = organizationNumber;
        Page = page;
        PageSize = pageSize;
    }

    /// <summary>Normalized 5-digit SNI-2025 leaf codes; empty = axis absent.</summary>
    public IReadOnlyList<string> SniCodes { get; }

    /// <summary>Normalized 4-digit SCB kommun codes (leading zero load-bearing); empty = axis absent.</summary>
    public IReadOnlyList<string> MunicipalityCodes { get; }

    /// <summary>Trimmed, LITERAL name prefix (wildcards are data, escaped by the impl); null = absent.</summary>
    public string? NamePrefix { get; }

    /// <summary>The stored 10-digit legal-entity form (written forms folded by
    /// <see cref="Jobbliggaren.Domain.CompanyWatches.OrganizationNumber.TryFromWrittenForm"/>);
    /// null = absent. Never personnummer-shaped — <see cref="Create"/> refuses those.</summary>
    public string? OrganizationNumber { get; }

    public int Page { get; }

    public int PageSize { get; }

    /// <summary>
    /// The most rows this surface can EVER serve — the pagination count's cap
    /// (<c>LIMIT MaxPage × pageSize</c>). Derived, never hand-picked: the page cap and the count
    /// cap are one knowledge piece (see <c>CompanyBrowseCriteria.MaxServableRows</c> for the full
    /// correctness argument — <c>TotalPages ≤ MaxPage</c> by construction).
    /// </summary>
    public static int MaxServableRows(int pageSize) => MaxPage * pageSize;

    /// <summary>
    /// THE single normalizer + validator. Order per axis: trim → drop-blank is an ERROR (a
    /// <c>[null]</c>/blank element is malformed input, not an empty axis — the #167 lesson:
    /// reject with 400, never 500, never silently drop) → format → dedupe (ordinal) → cap.
    /// </summary>
    public static Result<CompanyRegisterSearchCriteria> Create(
        IEnumerable<string?>? sniCodes,
        IEnumerable<string?>? municipalityCodes,
        string? name,
        string? organizationNumber,
        int page,
        int pageSize)
    {
        if (page is < 1 or > MaxPage)
        {
            return Result.Failure<CompanyRegisterSearchCriteria>(DomainError.Validation(
                "CompanyRegisterSearch.InvalidPage",
                $"Sidan måste vara mellan 1 och {MaxPage}."));
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            return Result.Failure<CompanyRegisterSearchCriteria>(DomainError.Validation(
                "CompanyRegisterSearch.InvalidPageSize",
                $"Sidstorleken måste vara mellan 1 och {MaxPageSize}."));
        }

        var sni = NormalizeCodes(
            sniCodes, requiredLength: 5, "CompanyRegisterSearch.InvalidSniCode",
            "Ogiltig SNI-kod: en bransch anges som fem siffror.");
        if (sni.IsFailure)
            return Result.Failure<CompanyRegisterSearchCriteria>(sni.Error);

        if (sni.Value.Count > MaxSniCodes)
        {
            return Result.Failure<CompanyRegisterSearchCriteria>(DomainError.Validation(
                "CompanyRegisterSearch.TooManySniCodes",
                $"Max {MaxSniCodes} branscher per sökning."));
        }

        var kommun = NormalizeCodes(
            municipalityCodes, requiredLength: 4, "CompanyRegisterSearch.InvalidMunicipalityCode",
            "Ogiltig kommunkod: en kommun anges som fyra siffror.");
        if (kommun.IsFailure)
            return Result.Failure<CompanyRegisterSearchCriteria>(kommun.Error);

        if (kommun.Value.Count > MaxMunicipalityCodes)
        {
            return Result.Failure<CompanyRegisterSearchCriteria>(DomainError.Validation(
                "CompanyRegisterSearch.TooManyMunicipalityCodes",
                $"Max {MaxMunicipalityCodes} kommuner per sökning."));
        }

        var namePrefix = name?.Trim();
        if (string.IsNullOrEmpty(namePrefix))
            namePrefix = null;

        if (namePrefix is { Length: > MaxNamePrefixLength })
        {
            return Result.Failure<CompanyRegisterSearchCriteria>(DomainError.Validation(
                "CompanyRegisterSearch.NameTooLong",
                $"Sökordet för företagsnamn får vara högst {MaxNamePrefixLength} tecken."));
        }

        string? orgnr = null;
        if (!string.IsNullOrWhiteSpace(organizationNumber))
        {
            // ONE normalizer for written org.nr forms ("556012-5790", "19560125-7901", …) —
            // the recognizer owns the folding. An unrecognized form is an honest 400, never a
            // silent zero-row search.
            var folded = Domain.CompanyWatches.OrganizationNumber.TryFromWrittenForm(organizationNumber);
            if (folded is null)
            {
                return Result.Failure<CompanyRegisterSearchCriteria>(DomainError.Validation(
                    "CompanyRegisterSearch.InvalidOrganizationNumber",
                    "Ogiltigt organisationsnummer: ange tio siffror, med eller utan bindestreck."));
            }

            // Refuse-posture (ADR 0088 D4 parity; lookup precedent). The register cannot contain
            // the row (ADR 0091) — refusing states the posture instead of resting it on a
            // different subsystem's ingest filter.
            if (folded.IsPersonnummerShaped())
            {
                return Result.Failure<CompanyRegisterSearchCriteria>(DomainError.Validation(
                    "CompanyRegisterSearch.PersonnummerShaped",
                    "Numret kan vara ett personnummer och kan därför inte användas som sökterm."));
            }

            orgnr = folded.Value;
        }

        return Result.Success(new CompanyRegisterSearchCriteria(
            sni.Value, kommun.Value, namePrefix, orgnr, page, pageSize));
    }

    /// <summary>
    /// Test/pin seam ONLY — bypasses validation exactly like the house <c>FromTrusted</c>
    /// idiom. Production paths go through <see cref="Create"/>.
    /// </summary>
    public static CompanyRegisterSearchCriteria FromTrusted(
        IReadOnlyList<string> sniCodes,
        IReadOnlyList<string> municipalityCodes,
        string? namePrefix,
        string? organizationNumber,
        int page,
        int pageSize) =>
        new(sniCodes, municipalityCodes, namePrefix, organizationNumber, page, pageSize);

    /// <summary>
    /// REDACTED (#883): the record carries a raw org.nr (potentially typed by the user in a
    /// personnummer-adjacent form before refusal); the compiler-generated <c>ToString()</c>
    /// would print it for a plain <c>{X}</c> MEL placeholder. Counts and presence only.
    /// </summary>
    public override string ToString() =>
        $"CompanyRegisterSearchCriteria(sni: {SniCodes.Count}, kommun: {MunicipalityCodes.Count}, "
        + $"name: {(NamePrefix is null ? "no" : "yes")}, org.nr redacted, "
        + $"page {Page}/{PageSize})";

    private static Result<IReadOnlyList<string>> NormalizeCodes(
        IEnumerable<string?>? raw, int requiredLength, string errorCode, string errorMessage)
    {
        if (raw is null)
            return Result.Success<IReadOnlyList<string>>([]);

        var normalized = new List<string>();
        foreach (var element in raw)
        {
            // A null/blank ELEMENT is malformed input (JSON `[null]` reaches here), not an
            // empty axis — reject, never skip (a skip is a second, silent normalizer).
            var code = element?.Trim();
            if (string.IsNullOrEmpty(code)
                || code.Length != requiredLength
                // [0-9] explicitly, never char.IsDigit/\d — #865: \p{Nd} folds fullwidth
                // digits into "valid" codes that match nothing in the register.
                || !code.All(c => c is >= '0' and <= '9'))
            {
                return Result.Failure<IReadOnlyList<string>>(
                    DomainError.Validation(errorCode, errorMessage));
            }

            normalized.Add(code);
        }

        return Result.Success<IReadOnlyList<string>>(
            normalized.Distinct(StringComparer.Ordinal).ToList());
    }
}
