using Jobbliggaren.Application.CompanyRegister.Abstractions;
using Jobbliggaren.Domain.CompanyWatches;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>Result of filtering one fetched SCB batch into persistable legal-entity rows.</summary>
/// <param name="Entries">The rows that passed both guards, mapped to the read-model.</param>
/// <param name="ExcludedPersonnummerShaped">Rows dropped by the personnummer-shape guard — the audited
/// proof the legal-entities-only invariant held (expected 0 given the SCB Juridisk-form filter, but
/// always enforced).</param>
/// <param name="ExcludedInvalid">Rows dropped because the org.nr failed 10-digit validation.</param>
internal readonly record struct ScbFilterResult(
    IReadOnlyList<ScbCompanyRegisterEntry> Entries,
    int ExcludedPersonnummerShaped,
    int ExcludedInvalid);

/// <summary>
/// #560 (ADR 0091) — the register's legal-entities-only ingest guard, extracted as a PURE function so
/// the GDPR-critical exclusion is unit-testable without a DB, HTTP, or DI (it is the security-auditor
/// veto surface — a first-class, tested boundary, CLAUDE.md §5 highest-priority). Applies, per fetched
/// row: 10-digit org.nr validation, then the <see cref="OrganizationNumber.IsPersonnummerShaped"/>
/// exclusion (defense-in-depth behind the SCB Juridisk-form ≠ 10 query filter), then maps survivors to
/// <see cref="ScbCompanyRegisterEntry"/>. The register therefore NEVER persists a personnummer-shaped
/// org.nr, even if the SCB filter were misconfigured or SCB returned an unexpected row.
/// </summary>
internal static class ScbLegalEntityFilter
{
    public static ScbFilterResult Apply(IReadOnlyList<ScbCompanyRecord> batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var entries = new List<ScbCompanyRegisterEntry>(batch.Count);
        var excludedPersonnummerShaped = 0;
        var excludedInvalid = 0;

        foreach (var record in batch)
        {
            var orgNr = OrganizationNumber.Create(record.OrganizationNumber);
            if (orgNr.IsFailure)
            {
                excludedInvalid++;
                continue;
            }

            if (orgNr.Value.IsPersonnummerShaped())
            {
                excludedPersonnummerShaped++;
                continue;
            }

            entries.Add(MapEntry(record));
        }

        return new ScbFilterResult(entries, excludedPersonnummerShaped, excludedInvalid);
    }

    private static ScbCompanyRegisterEntry MapEntry(ScbCompanyRecord record) => new()
    {
        OrganizationNumber = record.OrganizationNumber,
        Name = record.Name,
        SeatMunicipalityCode = record.SeatMunicipalityCode,
        SeatMunicipalityName = record.SeatMunicipalityName,
        SniCodes = [.. record.SniCodes],
        HasAdvertisingBlock = record.HasAdvertisingBlock,
        ScbStatusRaw = string.IsNullOrWhiteSpace(record.RawStatusCode) ? null : record.RawStatusCode,
        Status = MapStatus(record.RawStatusCode),
    };

    // SCB Företagsstatus: "1" = active (per the register's criteria); "0" (never active) / "9" (no
    // longer active) / anything unexpected → Deregistered. Full fidelity is preserved in
    // scb_status_raw (senior-cto-advisor Fork 4 — raw + derived).
    internal static CompanyRegisterStatus MapStatus(string? rawStatus) =>
        string.Equals(rawStatus, "1", StringComparison.Ordinal)
            ? CompanyRegisterStatus.Active
            : CompanyRegisterStatus.Deregistered;
}
