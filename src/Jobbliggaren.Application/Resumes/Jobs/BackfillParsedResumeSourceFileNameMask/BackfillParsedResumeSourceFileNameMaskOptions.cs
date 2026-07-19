using System.ComponentModel.DataAnnotations;

namespace Jobbliggaren.Application.Resumes.Jobs.BackfillParsedResumeSourceFileNameMask;

/// <summary>
/// Tunables for the one-off #664 backfill that re-masks pre-#465 personnummer left in the UNENCRYPTED
/// <c>parsed_resumes.source_file_name</c> column (#479 Low, GDPR Art. 5(1)(c)/25). Mirrors
/// <c>BackfillCompanyWatchOrgNrTokenOptions</c>: a LOCAL pass (no external call, no per-row secret), so
/// the default throttle is zero and the cap exists only as an operator brake. The changed set is tiny
/// (only rows whose filename holds a REAL personnummer) — empty in prod forever (#465 is already in
/// main and no prod host exists; the value is dev/staging hygiene + an auditable witness the exposure
/// is closed).
/// </summary>
public sealed class BackfillParsedResumeSourceFileNameMaskOptions
{
    public const string SectionName = "BackfillParsedResumeSourceFileNameMask";

    /// <summary>Optional per-update delay. Local re-projection → default 0.</summary>
    [Range(0, 10_000)]
    public int PerItemDelayMs { get; init; }

    /// <summary>Upper bound on rows SCANNED per run (an operator brake — the parsed-resume set is
    /// bounded by uploads). A re-run is idempotent (an already-masked filename is a no-op).</summary>
    [Range(1, 1_000_000)]
    public int MaxItemsPerRun { get; init; } = 500_000;

    [Range(1, 100_000)]
    public int ProgressLogEvery { get; init; } = 1_000;
}
