namespace Jobbliggaren.Application.Admin.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0184) — the two classes of message outcome that store nothing because of what the message held, as
/// the page and the run summary count them.
/// </summary>
public static class DmarcOutcomeClasses
{
    /// <summary>The message held no part the decoder may read.</summary>
    public static bool IsSkipped(DmarcMessageOutcome outcome) => outcome
        is DmarcMessageOutcome.SkippedTooLarge
        or DmarcMessageOutcome.SkippedNoReportPart
        or DmarcMessageOutcome.SkippedMultipleReportParts
        or DmarcMessageOutcome.SkippedForensic
        or DmarcMessageOutcome.SkippedUnsupportedEncoding
        or DmarcMessageOutcome.SkippedHeaderTooLarge;

    /// <summary>The message's report part was read, and the report refused.</summary>
    public static bool IsRejected(DmarcMessageOutcome outcome) => outcome
        is DmarcMessageOutcome.RejectedMalformed
        or DmarcMessageOutcome.RejectedPolicyDomain
        or DmarcMessageOutcome.RejectedContent
        or DmarcMessageOutcome.RejectedByStore;
}
