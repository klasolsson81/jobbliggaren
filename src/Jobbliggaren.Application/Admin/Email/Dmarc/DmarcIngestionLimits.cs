namespace Jobbliggaren.Application.Admin.Email.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0185) — every bound on DMARC report ingestion, named once. Anyone can mail the report address, so
/// each number bounds what one sender can make the Worker read, hold or store. The storage caps are sized against the
/// measured volume (8 reports in 5 days, 2026-10-24) and revisited against the first 30 days live.
/// </summary>
public static class DmarcIngestionLimits
{
    /// <summary>The domain whose reports are accepted. The report address is on it (RFC 9990 §4, same-domain case).</summary>
    public const string PolicyDomain = "jobbliggaren.se";

    /// <summary>New messages one run lists and claims, and the ceiling on new messages plus retries per run.</summary>
    public const int PerRunMessageCap = 100;

    /// <summary>A message larger than this (its RFC822.SIZE) is skipped without fetching any content.</summary>
    public const long MaxMessageBytes = 2L * 1024 * 1024;

    /// <summary>The top-level header block is fetched up to this many bytes and never parsed truncated.</summary>
    public const int HeaderFetchBytes = 8192;

    /// <summary>A body part's own header block is fetched up to this many bytes and never parsed truncated.</summary>
    public const int PartHeaderFetchBytes = 4096;

    /// <summary>The top-level parts whose headers are probed. Nested parts are never descended into.</summary>
    public const int MaxProbedParts = 4;

    /// <summary>The report part, still transfer-encoded, is fetched up to this many bytes plus one.</summary>
    public const int MaxReportPartBytes = 1024 * 1024;

    /// <summary>What the decoder may decompress from one report part; the one bound on the XML document.</summary>
    public const long MaxDecompressedXmlBytes = 8L * 1024 * 1024;

    /// <summary>
    /// The deepest element a report may hold, the root at depth 0. The report trees nest four levels; an unread
    /// extension may nest further, but not without bound, because the reader keeps a node per open element (8 MiB of
    /// open tags held about 500 MB, measured 2026-10-24).
    /// </summary>
    public const int MaxXmlElementDepth = 32;

    /// <summary>Records one report may carry.</summary>
    public const int MaxRecordsPerReport = 5_000;

    /// <summary>The largest message count one record may claim.</summary>
    public const int MaxRecordMessageCount = 10_000_000;

    /// <summary>The longest text field kept from a report (reporter, report id).</summary>
    public const int MaxFieldLength = 255;

    /// <summary>The longest header-from domain kept (RFC 1035).</summary>
    public const int MaxHeaderFromLength = 253;

    /// <summary>The longest period one report may cover.</summary>
    public static readonly TimeSpan MaxReportSpan = TimeSpan.FromDays(7);

    /// <summary>How far after its receipt a report's period may end (clock skew and the reporter's day boundary).</summary>
    public static readonly TimeSpan MaxEndAfterReceipt = TimeSpan.FromDays(1);

    /// <summary>Reports stored per UTC day of receipt. Past it a message is <see cref="DmarcMessageOutcome.VolumeCapped"/>.</summary>
    public const int MaxStoredReportsPerUtcDay = 100;

    /// <summary>Records stored per UTC day of receipt. One maximal report fills a day.</summary>
    public const int MaxStoredRecordsPerUtcDay = 5_000;

    /// <summary>Caught retries of one message before it is abandoned (168 hourly runs).</summary>
    public const int RetryLimit = 168;

    /// <summary>Runs that may end without finishing one message before it is abandoned.</summary>
    public const int MaxCrashAttempts = 2;

    /// <summary>One run's deadline, mailbox session and storage included.</summary>
    public static readonly TimeSpan RunDeadline = TimeSpan.FromMinutes(5);

    /// <summary>One mailbox command's timeout.</summary>
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long a second run waits for the first to finish; at least the run deadline.</summary>
    public const int ConcurrencyTimeoutSeconds = 360;

    /// <summary>The best-effort write that hands a claim back when the host shuts down.</summary>
    public static readonly TimeSpan ShutdownWriteTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// After a sign-in that was refused, or sent without an answer, no run connects again until this has passed since that
    /// attempt.
    /// </summary>
    public static readonly TimeSpan SignInBackoff = TimeSpan.FromHours(24);
}
