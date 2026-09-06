using Jobbliggaren.Domain.CompanyWatches;

namespace Jobbliggaren.Infrastructure.CompanyRegister;

/// <summary>Result of filtering one criterion's candidate org.nr into persistable member values.</summary>
/// <param name="OrganizationNumbers">The values that passed both guards, ready to write.</param>
/// <param name="ExcludedPersonnummerShaped">Candidates dropped by the personnummer-shape guard — the
/// audited proof that the member table's pnr-freedom is its OWN invariant.</param>
/// <param name="ExcludedInvalid">Candidates dropped because the org.nr failed 10-digit validation.</param>
internal readonly record struct CompanyWatchCriterionMemberFilterResult(
    IReadOnlyList<string> OrganizationNumbers,
    int ExcludedPersonnummerShaped,
    int ExcludedInvalid)
{
    /// <summary>
    /// REDACTED (#883, OrgNrRecordLoggingGuardTests). The compiler-generated record
    /// <c>ToString()</c> prints every member, and this one carries a LIST of raw org.nr — so a plain
    /// <c>{X}</c> MEL placeholder would write the whole matched company set into the log, and a sole
    /// trader's org.nr IS a personnummer in plaintext (ADR 0087 D8(c); CLAUDE.md §5, highest
    /// priority). The counts are kept: they are the diagnostic value here, and they are not personal
    /// data.
    /// </summary>
    public override string ToString() =>
        $"CompanyWatchCriterionMemberFilterResult({OrganizationNumbers.Count} org.nr redacted, "
        + $"{ExcludedPersonnummerShaped} pnr-shaped excluded, {ExcludedInvalid} invalid)";
}

/// <summary>
/// #1681 (ADR 0139; security-auditor Major 4, 2026-09-06) — the member table's OWN
/// legal-entities-only guard, at the materialisation's OWN write boundary. A PURE function, so the
/// GDPR-critical exclusion is unit-testable without a DB, HTTP or DI — deliberately the same shape as
/// <see cref="ScbLegalEntityFilter"/>, which is the ingest-side guard this one refuses to inherit
/// from.
///
/// <para>
/// <b>Why a second copy of a guarantee the register already makes.</b> The register IS
/// legal-entities-only (ADR 0091): SCB is queried with Juridisk form != 10 and
/// <see cref="ScbLegalEntityFilter"/> drops any pnr-shaped org.nr before persistence, pinned at the
/// exact third-digit boundary. So in a correct system this filter drops nothing, forever. It is here
/// anyway because resting one subsystem's at-rest PII guarantee on ANOTHER subsystem's ingest
/// invariant is precisely what this repo declined to do for <c>CompanyLookupDto</c> (#454), and
/// because a member table would otherwise be the first at-rest store in the house whose pnr-freedom
/// is nobody's own property. security-auditor: "then pnr-freedom is the table's own invariant, and ADR
/// 0090 D5 reaffirmed — the form that does not replicate a personnummer-shaped value into a second
/// at-rest location is the correct one (Art. 5(1)(c)) — is satisfied because the value class never
/// enters."
/// </para>
///
/// <para>
/// <b>Drop and COUNT, never drop silently.</b> The count rides out on
/// <c>CompanyWatchCriterionMaterialisation.ExcludedPersonnummerShaped</c> and on the run result. A
/// guard whose firings are not counted cannot be distinguished from a guard that never ran — which is
/// the vacuous-guarantee shape (#805-3, #842), and it would be an especially bad one here because the
/// expected count is zero: without the counter, "0 dropped" and "never executed" produce identical
/// evidence.
/// </para>
///
/// <para>
/// <b>Not tokenisation, and that was a decision rather than an omission</b> (security-auditor's own
/// answer to question 2). HMAC is what <c>CompanyWatch.organization_number</c> does because that value
/// is USER-SUPPLIED; these are REGISTER-DERIVED. Tokenising here would also break the read path, since
/// the join target <c>job_ads.organization_number</c> is plaintext (ADR 0087 D8(a)) — and the
/// SQL-prefilter-then-HMAC-in-memory hybrid that rescues the <c>CompanyWatch</c> arm works on a
/// handful of user rows, not on thousands of members times N criteria. Excluding the value class
/// outright is both cheaper and stronger than encoding it.
/// </para>
/// </summary>
internal static class CompanyWatchCriterionMemberFilter
{
    public static CompanyWatchCriterionMemberFilterResult Apply(IReadOnlyList<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var accepted = new List<string>(candidates.Count);
        var excludedPersonnummerShaped = 0;
        var excludedInvalid = 0;

        foreach (var candidate in candidates)
        {
            // Create enforces the 10-ASCII-digit format ([0-9], never \d — #865: a fullwidth or
            // Arabic-Indic "org.nr" would pass a \d guard, be stored, and then never equality-match
            // the ASCII job_ads.organization_number, i.e. the member would silently match NOTHING
            // forever, the cardinal sin).
            var orgNr = OrganizationNumber.Create(candidate);
            if (orgNr.IsFailure)
            {
                excludedInvalid++;
                continue;
            }

            // The SSOT discriminator (ADR 0087 D8(c), ADR 0090 D5 security-auditor B2): the same
            // predicate that decides masking at every surfacing boundary decides admission here, so
            // the set excluded at rest is exactly the set that would have been masked on display.
            // Deliberately over-inclusive — anything not exactly 10 digits is treated as sensitive.
            if (orgNr.Value.IsPersonnummerShaped())
            {
                excludedPersonnummerShaped++;
                continue;
            }

            accepted.Add(orgNr.Value.Value);
        }

        return new CompanyWatchCriterionMemberFilterResult(
            accepted, excludedPersonnummerShaped, excludedInvalid);
    }
}
