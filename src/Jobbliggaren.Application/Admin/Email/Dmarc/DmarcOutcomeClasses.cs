namespace Jobbliggaren.Application.Admin.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0184) — the one partition of message outcomes into the classes the page, the run summary and the
/// store count by. Total over the closed set: an outcome this release does not know throws.
/// </summary>
public enum DmarcOutcomeClass
{
    InProgress,
    RetryPending,
    Stored,

    /// <summary>The message held no part the decoder may read.</summary>
    Skipped,

    /// <summary>The message's report part was read, and the report refused.</summary>
    Rejected,

    VolumeCapped,
    Abandoned,
}

public static class DmarcOutcomeClasses
{
    public static DmarcOutcomeClass ClassOf(DmarcMessageOutcome outcome) => outcome switch
    {
        DmarcMessageOutcome.InProgress => DmarcOutcomeClass.InProgress,
        DmarcMessageOutcome.RetryPending => DmarcOutcomeClass.RetryPending,
        DmarcMessageOutcome.Stored or DmarcMessageOutcome.Duplicate or DmarcMessageOutcome.StoredConflicting =>
            DmarcOutcomeClass.Stored,
        DmarcMessageOutcome.SkippedTooLarge
            or DmarcMessageOutcome.SkippedNoReportPart
            or DmarcMessageOutcome.SkippedMultipleReportParts
            or DmarcMessageOutcome.SkippedForensic
            or DmarcMessageOutcome.SkippedUnsupportedEncoding
            or DmarcMessageOutcome.SkippedHeaderTooLarge
            or DmarcMessageOutcome.SkippedNotInMailbox => DmarcOutcomeClass.Skipped,
        DmarcMessageOutcome.RejectedMalformed
            or DmarcMessageOutcome.RejectedPolicyDomain
            or DmarcMessageOutcome.RejectedContent
            or DmarcMessageOutcome.RejectedByStore => DmarcOutcomeClass.Rejected,
        DmarcMessageOutcome.VolumeCapped => DmarcOutcomeClass.VolumeCapped,
        DmarcMessageOutcome.Abandoned => DmarcOutcomeClass.Abandoned,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A message outcome this release does not know."),
    };

    public static bool IsSkipped(DmarcMessageOutcome outcome) => ClassOf(outcome) == DmarcOutcomeClass.Skipped;

    public static bool IsRejected(DmarcMessageOutcome outcome) => ClassOf(outcome) == DmarcOutcomeClass.Rejected;
}
