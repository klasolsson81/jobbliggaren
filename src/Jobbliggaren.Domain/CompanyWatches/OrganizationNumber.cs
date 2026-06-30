using System.Text.RegularExpressions;
using Jobbliggaren.Domain.Common;

namespace Jobbliggaren.Domain.CompanyWatches;

/// <summary>
/// #311 PR-3 (ADR 0087 D3) — value object for a Swedish employer identity: the
/// 10-digit organisationsnummer (org.nr) issued by Bolagsverket. The canonical follow
/// key for <see cref="CompanyWatch"/> (ADR 0087: org.nr is the only correct key — no
/// fuzzy name matching, no "Volvo×20" trap). Stored verbatim, no hyphen normalisation
/// (parity with the <c>job_ads.organization_number</c> generated column form, PR-1).
///
/// <para>
/// <b>FORK A1 (senior-cto-advisor 2026-06-30) — CONTAINED:</b> this VO is consumed by
/// <see cref="CompanyWatch"/> only. The PR-2 live-search filter path
/// (<c>ListJobAdsQueryValidator.OrganizationNumberPattern</c>) is NOT refactored onto
/// it here — that consolidation belongs to PR-2b (#415), which already owns the
/// SearchCriteria-VO threading. The transient format duplication is a planned
/// consolidation seam, not a DRY violation.
/// </para>
///
/// <para>
/// <b>GDPR / sole-proprietorship (enskild firma) — D8(c), highest-priority guard:</b> a
/// Swedish enskild firma org.nr CAN EQUAL the owner's personnummer (a 10-digit national
/// identity number). <see cref="IsPersonnummerShaped"/> is the surfacing/log-boundary
/// detector that lets a display projection flag/mask such a value. It is a deliberately
/// CONSERVATIVE, NON-PRIMARY heuristic (safe-default-sensitive on any uncertainty); the
/// primary protections are owner-scoped access + Art.17 cascade erasability (ADR 0087
/// D8(b)) and never logging org.nr un-flagged (CLAUDE.md §5). The VALUE itself is never
/// re-shaped or excluded (D8 rejected that as a feature gap on an imperfect heuristic).
/// </para>
/// </summary>
public sealed record OrganizationNumber
{
    // A Swedish org.nr is exactly 10 digits, no hyphen (live-verified JobStream form, e.g.
    // 5592804784). \z (not $) against newline-injection — parity with ListJobAdsQueryValidator
    // (PR-2) and SearchCriteria.ConceptIdPattern. Default-deny (Saltzer/Schroeder 1975).
    private static readonly Regex Pattern = new(@"^\d{10}\z", RegexOptions.Compiled);

    /// <summary>The verbatim 10-digit org.nr. Never null on a validly-constructed instance.</summary>
    public string Value { get; }

    private OrganizationNumber(string value) => Value = value;

    /// <summary>
    /// Validates and constructs. Returns a Validation error (never throws) for null/blank or
    /// non-10-digit input — the expected-failure idiom (CLAUDE.md §3 Result idiom).
    /// </summary>
    public static Result<OrganizationNumber> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<OrganizationNumber>(DomainError.Validation(
                "OrganizationNumber.Required", "Organisationsnummer är obligatoriskt."));

        if (!Pattern.IsMatch(value))
            return Result.Failure<OrganizationNumber>(DomainError.Validation(
                "OrganizationNumber.Invalid", "Organisationsnummer måste vara exakt 10 siffror."));

        return Result.Success(new OrganizationNumber(value));
    }

    /// <summary>
    /// Reconstructs from an already-validated, persisted value (EF materialisation only). The DB
    /// column was validated by <see cref="Create"/> on write, so no re-validation — parity with
    /// the strongly-typed Id <c>HasConversion</c> idiom.
    /// </summary>
    public static OrganizationNumber FromTrusted(string value) => new(value);

    /// <summary>
    /// True when this 10-digit value is shaped like a Swedish personnummer (i.e. a potential
    /// enskild-firma org.nr that equals the owner's national identity number) and MUST be
    /// flagged/masked at any surfacing/log boundary (ADR 0087 D8(c)).
    ///
    /// <para>
    /// <b>Heuristic (conservative, non-primary, safe-default-sensitive):</b> a Swedish
    /// legal-entity organisationsnummer ALWAYS has its third digit ≥ 2 (Skatteverket: the
    /// "group number" for a legal person is 2–9). A personnummer YYMMDD-NNNN has its third
    /// digit = the tens digit of the birth month (0 for months 01–09, 1 for months 10–12),
    /// so 0 or 1. Hence a third digit &lt; 2 means the value cannot be a legal org.nr and is
    /// personnummer-shaped. Any value that is not exactly 10 digits also returns true (treat
    /// the unexpected as sensitive — this never runs on a validly-constructed instance, but the
    /// fail-safe default holds if the invariant is ever weakened). This is NOT a personnummer
    /// validity check (no Luhn/date validation) — it is a deliberately over-inclusive guard so
    /// the masking layer never under-flags. D8(c): on detection-uncertainty, treat-as-sensitive.
    /// </para>
    /// </summary>
    public bool IsPersonnummerShaped()
    {
        if (Value.Length != 10 || !Value.All(char.IsAsciiDigit))
            return true; // fail-safe: the unexpected is sensitive

        return Value[2] < '2';
    }

    public override string ToString() => Value;
}
