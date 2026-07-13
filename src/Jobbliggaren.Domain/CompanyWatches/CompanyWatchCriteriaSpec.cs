using System.Text.RegularExpressions;
using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.CompanyWatches;

/// <summary>
/// The predicate of a criteria-based company watch (#560, senior-cto-advisor Fork A1/B1
/// 2026-07-12) — the SNI industry codes and SCB seat-municipality codes a user wants to
/// DISCOVER employers by. Evaluated as a query against the local SCB company register
/// (<c>company_register</c>), never expanded into per-company rows (epic bind: "the scan-set
/// explodes").
///
/// <para>
/// <b>House pattern-sibling of <see cref="WatchFilterSpec"/></b>: same normalization (trim →
/// drop blank → distinct ordinal → sort ordinal), same per-element format validation, same
/// explicit structural equality (a record with an <see cref="IReadOnlyList{T}"/> member gets
/// REFERENCE equality by default — the EF value comparison relies on structural equality).
/// </para>
///
/// <para>
/// <b>Both axes are REQUIRED (Fork B1).</b> Unlike <see cref="WatchFilterSpec"/> — where any
/// single axis narrows something — a criterion demands SNI ∧ kommun. A kommun-only criterion
/// ("every company in Göteborg") would scan tens of thousands of rows for no discovery value;
/// an SNI-only one ("every IT company in Sweden") likewise. The two failures carry SEPARATE
/// error codes rather than one shared "Empty", so the picker UI (PR-3) can point at the axis
/// the user actually left blank.
/// </para>
///
/// <para>
/// <b>The codes live in SCB's namespaces, deliberately NOT JobTech's</b> (RF-4, ADR 0105).
/// <see cref="MunicipalityCodes"/> are SCB 4-digit kommun-codes matching
/// <c>company_register.sate_kommun_code</c> — the company's REGISTERED SEAT ("säteskommun"),
/// a different concept from the JobTech <c>municipality_concept_id</c> an AD carries
/// ("annonsens ort"). The copy keeps them apart; the code must never conflate the namespaces.
/// </para>
///
/// <para>
/// <b>Exact 5-digit SNI match (Fork B1), not prefix.</b> An industry-level selection ("62 —
/// IT-tjänster") is expanded to its leaf codes by the picker (Application/FE), never by a
/// prefix query — so the stored predicate is always a concrete leaf set and the register query
/// stays a plain array-overlap (<c>&amp;&amp;</c>), GIN-indexable.
/// </para>
///
/// <para>
/// Domain enforces FORMAT only. Existence against the SNI-2025 / kommun reference data is an
/// Application-validator concern (PR-3) — Domain stays framework-free (DIP, CLAUDE.md §2.1).
/// </para>
/// </summary>
public sealed record CompanyWatchCriteriaSpec
{
    /// <summary>
    /// DoS ceiling, NOT a product limit — the SearchCriteria.MaxConceptIds doctrine ("the cap is
    /// the size of the universe, so 'select all' never bites"). The SNI-2025 leaf universe is
    /// ~800 codes, and expanding one industry section to its leaves (Fork B1) can legitimately
    /// yield several hundred, so a stingy cap (50/100) would break "watch my whole industry".
    /// The real attack vector is an UNBOUNDED list, not 800 codes. Revisit together with the
    /// SNI-2025 reference dataset (PR-3): if the true leaf count exceeds this, the constant follows.
    /// </summary>
    public const int MaxSniCodes = 1000;

    /// <summary>Exactly the Swedish kommun universe (290) — "hela Sverige" stays expressible and
    /// the cap never bites a legitimate selection.</summary>
    public const int MaxMunicipalityCodes = 290;

    // Exact 5-digit SNI-2025 leaf / exact 4-digit SCB kommun code.
    //
    // [0-9], NOT \d — deliberately. In .NET `\d` means `\p{Nd}`: the WHOLE Unicode decimal-digit
    // category, so "٦٢٠١٠" (Arabic-Indic) and "６２０１０" (fullwidth) both satisfy `^\d{5}\z`. Such a
    // code would pass this guard, be stored in sni_codes, and then never overlap the ASCII-only SCB
    // register — the criterion would silently match NOTHING and never error. A silent miss is this
    // product's cardinal sin, so the guard is ASCII-explicit (test-writer probe, 2026-07-13).
    //
    // `\z` (not `$`) so a code cannot smuggle a second line past the guard. NOTE: a TRAILING
    // newline is already removed by NormalizeList's Trim() before the regex ever runs — what `\z`
    // actually buys is rejecting an EMBEDDED newline ("62010\n62020"), which Trim does not touch
    // and which `$` would have accepted.
    private static readonly Regex SniPattern = new(@"^[0-9]{5}\z", RegexOptions.Compiled);
    private static readonly Regex MunicipalityPattern = new(@"^[0-9]{4}\z", RegexOptions.Compiled);

    public IReadOnlyList<string> SniCodes { get; private init; } = [];
    public IReadOnlyList<string> MunicipalityCodes { get; private init; } = [];

    // EF + record copy-semantics
    private CompanyWatchCriteriaSpec() { }

    public static Result<CompanyWatchCriteriaSpec> Create(
        IEnumerable<string>? sniCodes,
        IEnumerable<string>? municipalityCodes)
    {
        var normSni = NormalizeList(sniCodes);
        var normMunicipality = NormalizeList(municipalityCodes);

        // Fork B1 — BOTH axes required. Normalization runs first, so ["  "] is an EMPTY axis,
        // not a one-element one.
        if (normSni.Length == 0)
        {
            return Result.Failure<CompanyWatchCriteriaSpec>(DomainError.Validation(
                "CompanyWatchCriteriaSpec.SniRequired",
                "Minst en bransch (SNI-kod) krävs för en bevakning."));
        }

        if (normMunicipality.Length == 0)
        {
            return Result.Failure<CompanyWatchCriteriaSpec>(DomainError.Validation(
                "CompanyWatchCriteriaSpec.MunicipalityRequired",
                "Minst en kommun krävs för en bevakning."));
        }

        if (normSni.Length > MaxSniCodes)
        {
            return Result.Failure<CompanyWatchCriteriaSpec>(DomainError.Validation(
                "CompanyWatchCriteriaSpec.TooManySniCodes",
                $"Max {MaxSniCodes} branscher per bevakning."));
        }

        if (normMunicipality.Length > MaxMunicipalityCodes)
        {
            return Result.Failure<CompanyWatchCriteriaSpec>(DomainError.Validation(
                "CompanyWatchCriteriaSpec.TooManyMunicipalityCodes",
                $"Max {MaxMunicipalityCodes} kommuner per bevakning."));
        }

        foreach (var sni in normSni)
        {
            if (!SniPattern.IsMatch(sni))
            {
                return Result.Failure<CompanyWatchCriteriaSpec>(DomainError.Validation(
                    "CompanyWatchCriteriaSpec.InvalidSniCode",
                    "Bransch måste vara en 5-siffrig SNI-kod."));
            }
        }

        foreach (var kommun in normMunicipality)
        {
            if (!MunicipalityPattern.IsMatch(kommun))
            {
                return Result.Failure<CompanyWatchCriteriaSpec>(DomainError.Validation(
                    "CompanyWatchCriteriaSpec.InvalidMunicipalityCode",
                    "Kommun måste vara en 4-siffrig kommunkod."));
            }
        }

        return Result.Success(new CompanyWatchCriteriaSpec
        {
            SniCodes = normSni,
            MunicipalityCodes = normMunicipality,
        });
    }

    /// <summary>
    /// Rebuilds the spec from already-validated storage (the two <c>text[]</c> columns), skipping
    /// validation — the values passed <see cref="Create"/> on write. Parity with
    /// <c>OrganizationNumber.FromTrusted</c> and the strongly-typed-id idiom.
    ///
    /// <para>
    /// <b>COPIES the inputs.</b> The aggregate's backing lists are mutable (EF materializes into
    /// them, and <c>UpdateCriteria</c> mutates them in place) — aliasing them here would make this
    /// value object's immutability fictional: a later mutation of the aggregate would silently
    /// rewrite a spec someone else was already holding.
    /// </para>
    /// </summary>
    public static CompanyWatchCriteriaSpec FromTrusted(
        IEnumerable<string> sniCodes,
        IEnumerable<string> municipalityCodes) =>
        new()
        {
            SniCodes = sniCodes.ToArray(),
            MunicipalityCodes = municipalityCodes.ToArray(),
        };

    private static string[] NormalizeList(IEnumerable<string>? values)
    {
        if (values is null)
            return [];

        return values
            .Where(static v => !string.IsNullOrWhiteSpace(v))
            .Select(static v => v.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static v => v, StringComparer.Ordinal)
            .ToArray();
    }

    // Structural VO equality (Evans 2003 ch. 5) — the lists are normalized (sorted+distinct
    // ordinal) in Create, so sequence comparison is deterministic. EF's value comparison for the
    // two text[] columns relies on this.
    public bool Equals(CompanyWatchCriteriaSpec? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return SniCodes.SequenceEqual(other.SniCodes, StringComparer.Ordinal)
            && MunicipalityCodes.SequenceEqual(other.MunicipalityCodes, StringComparer.Ordinal);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var sni in SniCodes)
            hash.Add(sni, StringComparer.Ordinal);
        foreach (var kommun in MunicipalityCodes)
            hash.Add(kommun, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
