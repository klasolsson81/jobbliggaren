using System.Text.Json.Serialization;

namespace Jobbliggaren.Application.Admin.Email.Dmarc;

// #1981 PR 4a (ADR 0184) — the closed sets of DMARC report ingestion. Every member has an explicit number from 1,
// because the tables store the number and a CHECK rejects 0; a member is never renumbered.

/// <summary>
/// The DMARC-aligned result of one mechanism as the reporter evaluated it (<c>policy_evaluated/dkim</c> and
/// <c>/spf</c>). The schema allows only pass and fail; anything else a reporter writes is <see cref="Other"/>, which
/// is not a pass.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DmarcAlignment>))]
public enum DmarcAlignment
{
    Pass = 1,
    Fail = 2,
    Other = 3,
}

/// <summary>How the one report part of a message is packed.</summary>
public enum DmarcReportPartKind
{
    Xml = 1,
    Gzip = 2,
    Zip = 3,
}

/// <summary>Why a message held no part the decoder may read. Decided from bounded header fetches only.</summary>
public enum DmarcSkipReason
{
    /// <summary>The message, or its report part, is larger than the fetch cap.</summary>
    TooLarge = 1,

    /// <summary>No part has a report's media type or file extension.</summary>
    NoReportPart = 2,

    /// <summary>More than one part could be the report, so none is read.</summary>
    MultipleReportParts = 3,

    /// <summary>A forensic report (<c>multipart/report; report-type=feedback-report</c>). Never fetched further.</summary>
    Forensic = 4,

    /// <summary>The part's transfer encoding is not one the adapter decodes.</summary>
    UnsupportedEncoding = 5,

    /// <summary>A header block did not end inside its fetch cap, so it is never parsed truncated.</summary>
    HeaderTooLarge = 6,

    /// <summary>The mailbox answered a fetch for the message with no data: the message is gone (RFC 3501 §6.4.8).</summary>
    NotInMailbox = 7,
}

/// <summary>
/// A fault that ends the whole mailbox session. The message in hand goes back to <see cref="DmarcMessageOutcome.RetryPending"/>.
/// </summary>
public enum DmarcSessionFault
{
    Unreachable = 1,
    TlsFailed = 2,
    AuthRefused = 3,
    Timeout = 4,

    /// <summary>The server's answer broke the protocol, a tagged BAD included, or the connection became unusable.</summary>
    Protocol = 5,

    /// <summary>The sign-in was sent and no answer to it came: a timeout, a BYE, a reset or a protocol fault.</summary>
    AuthUnconfirmed = 6,
}

/// <summary>
/// What became of one message, kept in the ledger by mailbox epoch and position. The ledger never holds a sender,
/// a subject, a body or a filename.
/// </summary>
public enum DmarcMessageOutcome
{
    /// <summary>Claimed by a run that has not finished with it. Still here at the start of a run, it counts a crash.</summary>
    InProgress = 1,

    /// <summary>A session fault or a refusal of this message ended its turn; a later run retries it.</summary>
    RetryPending = 2,

    /// <summary>Two crashes, or the retry limit, ended it. The mailbox still holds it.</summary>
    Abandoned = 3,

    /// <summary>The report was stored.</summary>
    Stored = 10,

    /// <summary>The same report, byte for byte after decompression, was already stored.</summary>
    Duplicate = 11,

    /// <summary>Stored, but another stored report has the same reporter and report id with other content.</summary>
    StoredConflicting = 12,

    /// <summary>The day's storage cap was reached, so nothing was stored.</summary>
    VolumeCapped = 13,

    SkippedTooLarge = 20,
    SkippedNoReportPart = 21,
    SkippedMultipleReportParts = 22,
    SkippedForensic = 23,
    SkippedUnsupportedEncoding = 24,
    SkippedHeaderTooLarge = 25,
    SkippedNotInMailbox = 26,

    /// <summary>The part could not be read as a bounded report: archive, compression, XML or a decoder cap.</summary>
    RejectedMalformed = 30,

    /// <summary>The report is about another domain.</summary>
    RejectedPolicyDomain = 31,

    /// <summary>A field or a record broke one of the acceptance bounds, so the whole report was refused.</summary>
    RejectedContent = 32,

    /// <summary>The database refused a report the acceptance bounds let through. Declared unreachable.</summary>
    RejectedByStore = 33,
}

/// <summary>How the latest ingestion run ended. Kept in the one state row, never per message.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DmarcRunOutcome>))]
public enum DmarcRunOutcome
{
    Succeeded = 1,

    /// <summary>No mailbox password is configured, so nothing is read.</summary>
    NotConfigured = 2,

    /// <summary>A password pointer is set but the password is blank.</summary>
    Misconfigured = 3,

    Unreachable = 4,
    TlsFailed = 5,
    AuthRefused = 6,
    Timeout = 7,
    Protocol = 8,

    /// <summary>A first run started and has not finished. Written only when no earlier run left a state row.</summary>
    Incomplete = 9,

    /// <summary>The sign-in was sent and no answer to it came.</summary>
    AuthUnconfirmed = 10,
}

/// <summary>Why <see cref="DmarcAggregateReport.Accept"/> refused a candidate. Every reason refuses the whole report.</summary>
public enum DmarcRejection
{
    PolicyDomain = 1,
    FieldTooLong = 2,
    ForbiddenCharacter = 3,
    MalformedSource = 4,
    HeaderFromOutsideDomain = 5,
    CountOutOfRange = 6,
    ImplausibleRange = 7,
    NoRecords = 8,
    TooManyRecords = 9,
}

public static class DmarcOutcomes
{
    /// <summary>The ledger outcome a skip reason is recorded as. Total over the closed set.</summary>
    public static DmarcMessageOutcome OutcomeOf(DmarcSkipReason reason) => reason switch
    {
        DmarcSkipReason.TooLarge => DmarcMessageOutcome.SkippedTooLarge,
        DmarcSkipReason.NoReportPart => DmarcMessageOutcome.SkippedNoReportPart,
        DmarcSkipReason.MultipleReportParts => DmarcMessageOutcome.SkippedMultipleReportParts,
        DmarcSkipReason.Forensic => DmarcMessageOutcome.SkippedForensic,
        DmarcSkipReason.UnsupportedEncoding => DmarcMessageOutcome.SkippedUnsupportedEncoding,
        DmarcSkipReason.HeaderTooLarge => DmarcMessageOutcome.SkippedHeaderTooLarge,
        DmarcSkipReason.NotInMailbox => DmarcMessageOutcome.SkippedNotInMailbox,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "A skip reason this release does not know."),
    };

    /// <summary>The ledger outcome a rejection is recorded as: the policy domain apart, every bound is content.</summary>
    public static DmarcMessageOutcome OutcomeOf(DmarcRejection rejection) => rejection switch
    {
        DmarcRejection.PolicyDomain => DmarcMessageOutcome.RejectedPolicyDomain,
        _ when Enum.IsDefined(rejection) => DmarcMessageOutcome.RejectedContent,
        _ => throw new ArgumentOutOfRangeException(nameof(rejection), rejection, "A rejection this release does not know."),
    };

    /// <summary>The run outcome a session fault ends the run with.</summary>
    public static DmarcRunOutcome RunOutcomeOf(DmarcSessionFault fault) => fault switch
    {
        DmarcSessionFault.Unreachable => DmarcRunOutcome.Unreachable,
        DmarcSessionFault.TlsFailed => DmarcRunOutcome.TlsFailed,
        DmarcSessionFault.AuthRefused => DmarcRunOutcome.AuthRefused,
        DmarcSessionFault.Timeout => DmarcRunOutcome.Timeout,
        DmarcSessionFault.Protocol => DmarcRunOutcome.Protocol,
        DmarcSessionFault.AuthUnconfirmed => DmarcRunOutcome.AuthUnconfirmed,
        _ => throw new ArgumentOutOfRangeException(nameof(fault), fault, "A session fault this release does not know."),
    };

    /// <summary>
    /// Whether a run that ended so keeps the next runs from signing in for <see cref="DmarcIngestionLimits.SignInBackoff"/>:
    /// the password was refused, or it was sent and no answer came. Total over the closed set.
    /// </summary>
    public static bool BacksOffSignIn(DmarcRunOutcome outcome) => outcome switch
    {
        DmarcRunOutcome.AuthRefused or DmarcRunOutcome.AuthUnconfirmed => true,
        DmarcRunOutcome.Succeeded or DmarcRunOutcome.NotConfigured or DmarcRunOutcome.Misconfigured
            or DmarcRunOutcome.Unreachable or DmarcRunOutcome.TlsFailed or DmarcRunOutcome.Timeout
            or DmarcRunOutcome.Protocol or DmarcRunOutcome.Incomplete => false,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A run outcome this release does not know."),
    };
}
