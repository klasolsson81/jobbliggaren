using Jobbliggaren.Application.Admin.Email.Dmarc;
using MailKit;
using MailKit.Net.Imap;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Infrastructure.Mailbox.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0185) — one run's read-only view of the inbox. Every command it sends is bounded before it is sent:
/// the listing by the message count EXAMINE reported, captured once, and every content fetch by a partial range. Each
/// command also has its own deadline. Once a session fault ends it, every later call answers with that fault and sends
/// nothing, and disposing it closes the connection without a LOGOUT the server may never answer.
/// </summary>
internal sealed class ImapDmarcMailboxSession : IDmarcMailboxSession
{
    private static readonly FetchRequest UidOnly = new(MessageSummaryItems.UniqueId);

    private static readonly FetchRequest Metadata =
        new(MessageSummaryItems.UniqueId | MessageSummaryItems.Size | MessageSummaryItems.InternalDate);

    private static readonly TimeSpan LogoutTimeout = TimeSpan.FromSeconds(5);

    private readonly ImapClient _client;
    private readonly IMailFolder _inbox;
    private readonly uint _uidValidity;
    private readonly int _exists;
    private readonly TimeSpan _commandTimeout;
    private readonly ILogger _logger;
    private DmarcSessionFault? _fault;
    private bool _cancelled;
    private DmarcMailboxStep _step;

    public ImapDmarcMailboxSession(
        ImapClient client, IMailFolder inbox, uint uidValidity, int exists, TimeSpan commandTimeout, ILogger logger)
    {
        _client = client;
        _inbox = inbox;
        _uidValidity = uidValidity;
        _exists = exists;
        _commandTimeout = commandTimeout;
        _logger = logger;
    }

    public long Epoch => _uidValidity;

    public async ValueTask<DmarcMailboxListing> ListAfterAsync(long position, int max, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(max);
        if (_fault is { } earlier)
            return new DmarcMailboxListing.Failed(earlier);

        try
        {
            return await ListAsync(position, max, cancellationToken) is { } entries
                ? new DmarcMailboxListing.Listed(entries)
                : new DmarcMailboxListing.Failed(End(DmarcSessionFault.Protocol, DmarcMailboxStep.List));
        }
        catch (Exception exception) when (ImapFaults.FaultOf(exception, DmarcMailboxStep.List, cancellationToken) is { } fault)
        {
            return new DmarcMailboxListing.Failed(End(fault, DmarcMailboxStep.List));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _cancelled = true;
            throw;
        }
    }

    public async ValueTask<DmarcMailboxFetch> FetchReportAsync(DmarcMailboxEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(entry.Position, uint.MaxValue);
        if (_fault is { } earlier)
            return new DmarcMailboxFetch.Failed(earlier);

        _step = DmarcMailboxStep.FetchHeader;
        try
        {
            return await FetchAsync(new UniqueId(_uidValidity, (uint)entry.Position), cancellationToken);
        }
        catch (Exception exception) when (ImapFaults.FaultOf(exception, _step, cancellationToken) is { } fault)
        {
            return new DmarcMailboxFetch.Failed(End(fault, _step));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _cancelled = true;
            throw;
        }
    }

    public async ValueTask DisposeAsync() => await CloseAsync(_client, _logger, logOut: _fault is null && !_cancelled);

    /// <summary>Sends LOGOUT when asked and still connected, then releases the client whatever happened.</summary>
    internal static async Task CloseAsync(ImapClient client, ILogger logger, bool logOut)
    {
        try
        {
            if (logOut && client.IsConnected)
            {
                using var timeout = new CancellationTokenSource(LogoutTimeout);
                await client.DisconnectAsync(quit: true, timeout.Token);
            }
        }
        catch (Exception exception) when (ImapFaults.FaultOf(exception, DmarcMailboxStep.Connect, CancellationToken.None) is not null)
        {
            logger.LogoutIncomplete(exception.GetType().Name);
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <summary>
    /// CTO r2 §1 (security-auditor D1–D6): the first sequence number whose UID is above <paramref name="position"/>,
    /// found by bisection over <c>FETCH k (UID)</c>, then one window of at most <paramref name="max"/> messages. EXISTS
    /// is the count EXAMINE reported, read once: an untagged EXISTS later moves MailKit's count, never this bound.
    /// Only non-UID FETCH is sent from the first probe to the window, so RFC 3501 §7.4.1 keeps every sequence number
    /// in place meanwhile, and UIDs ascend with sequence numbers (§2.3.1.2). <see langword="null"/> means the server's
    /// answers broke that ordering or left something out, which is never read as "nothing new".
    /// </summary>
    private async Task<IReadOnlyList<DmarcMailboxEntry>?> ListAsync(long position, int max, CancellationToken cancellationToken)
    {
        if (_exists == 0)
            return [];

        if (await UidAtAsync(_exists, cancellationToken) is not { } lastUid)
            return null;
        if (lastUid <= position)
            return [];

        var low = 1;
        var high = _exists;
        uint lowerUid = 0;
        var upperUid = lastUid;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (await UidAtAsync(middle, cancellationToken) is not { } uid || uid <= lowerUid || uid >= upperUid)
                return null;

            if (uid > position)
            {
                high = middle;
                upperUid = uid;
            }
            else
            {
                low = middle + 1;
                lowerUid = uid;
            }
        }

        var first = low;
        var last = (int)Math.Min((long)first + max - 1, _exists);
        var window = await ImapDeadline.RunAsync(
            _commandTimeout, token => _inbox.FetchAsync(first - 1, last - 1, Metadata, token), cancellationToken);
        return WindowEntries(window, first, last, upperUid, position);
    }

    private async Task<uint?> UidAtAsync(int sequenceNumber, CancellationToken cancellationToken)
    {
        var index = sequenceNumber - 1;
        var answer = await ImapDeadline.RunAsync(
            _commandTimeout, token => _inbox.FetchAsync(index, index, UidOnly, token), cancellationToken);
        return answer is [{ UniqueId.IsValid: true } summary] && summary.Index == index ? summary.UniqueId.Id : null;
    }

    private static List<DmarcMailboxEntry>? WindowEntries(
        IList<IMessageSummary> window, int first, int last, uint firstUid, long position)
    {
        var ordered = window.OrderBy(summary => summary.Index).ToList();
        if (ordered.Count != last - first + 1)
            return null;

        var entries = new List<DmarcMailboxEntry>(ordered.Count);
        uint previous = 0;
        for (var offset = 0; offset < ordered.Count; offset++)
        {
            var summary = ordered[offset];
            if (summary.Index != first - 1 + offset
                || !summary.UniqueId.IsValid
                || summary.UniqueId.Id <= previous
                || summary.Size is not { } size
                || summary.InternalDate is not { } receivedAt)
                return null;
            if (offset == 0 && summary.UniqueId.Id != firstUid)
                return null;

            previous = summary.UniqueId.Id;
            if (summary.UniqueId.Id > position)
                entries.Add(new DmarcMailboxEntry(summary.UniqueId.Id, receivedAt, size));
        }

        return entries;
    }

    /// <summary>
    /// CTO r1 mechanism steps 3–5: the top-level header, then for a multipart the part headers at depth one, then the
    /// one candidate part, each fetched as a partial range. A tagged NO to the header, to part 1 or to the part itself
    /// is a refusal of this message; a BAD ends the session. For parts after the first, NIL, empty, NO and BAD all mean
    /// "there is no such part": the command differs from part 1's only in its number.
    /// </summary>
    private async Task<DmarcMailboxFetch> FetchAsync(UniqueId uid, CancellationToken cancellationToken)
    {
        var header = await ReadSectionAsync(uid, "HEADER", DmarcIngestionLimits.HeaderFetchBytes, cancellationToken);
        if (header is not SectionRead.Bytes { Content: var headerBytes })
            return Unread(header);

        var topBlock = MimeHeaderBlock.Read(headerBytes);
        if (topBlock is not MimeHeaderRead.Complete { Fields: var topFields })
            return Skip(topBlock);

        var top = ReportPartRules.Parse(topFields);
        if (ReportPartRules.IsForensic(top))
            return new DmarcMailboxFetch.Skipped(DmarcSkipReason.Forensic);

        if (!ReportPartRules.IsMultipart(top))
        {
            return ReportPartRules.KindOf(top, insideDigest: false) is { } kind
                ? await FetchPartAsync(uid, part: 1, top.Encoding, kind, cancellationToken)
                : new DmarcMailboxFetch.Skipped(DmarcSkipReason.NoReportPart);
        }

        _step = DmarcMailboxStep.ProbePart;
        var digest = ReportPartRules.IsDigest(top);
        var candidates = new List<(int Part, DmarcReportPartKind Kind, TransferEncoding Encoding)>();
        for (var part = 1; part <= DmarcIngestionLimits.MaxProbedParts; part++)
        {
            var read = await ReadSectionAsync(uid, $"{part}.MIME", DmarcIngestionLimits.PartHeaderFetchBytes, cancellationToken);
            if (read is not SectionRead.Bytes { Content: { Length: > 0 } partHeader })
            {
                if (part == 1 && read is SectionRead.No or SectionRead.Bad or SectionRead.Absent)
                    return Unread(read);
                break;
            }

            var block = MimeHeaderBlock.Read(partHeader);
            if (block is not MimeHeaderRead.Complete { Fields: var fields })
                return Skip(block);

            var parsed = ReportPartRules.Parse(fields);
            if (ReportPartRules.KindOf(parsed, digest) is { } kind)
                candidates.Add((part, kind, parsed.Encoding));
        }

        return candidates switch
        {
            [] => new DmarcMailboxFetch.Skipped(DmarcSkipReason.NoReportPart),
            [var only] => await FetchPartAsync(uid, only.Part, only.Encoding, only.Kind, cancellationToken),
            _ => new DmarcMailboxFetch.Skipped(DmarcSkipReason.MultipleReportParts),
        };
    }

    private async Task<DmarcMailboxFetch> FetchPartAsync(
        UniqueId uid,
        int part,
        TransferEncoding encoding,
        DmarcReportPartKind kind,
        CancellationToken cancellationToken)
    {
        if (encoding == TransferEncoding.Unsupported)
            return new DmarcMailboxFetch.Skipped(DmarcSkipReason.UnsupportedEncoding);

        _step = DmarcMailboxStep.FetchPart;
        var read = await ReadSectionAsync(
            uid, part.ToString(System.Globalization.CultureInfo.InvariantCulture), DmarcIngestionLimits.MaxReportPartBytes + 1, cancellationToken);
        if (read is not SectionRead.Bytes { Content: var content })
            return Unread(read);

        return content.Length > DmarcIngestionLimits.MaxReportPartBytes
            ? new DmarcMailboxFetch.Skipped(DmarcSkipReason.TooLarge)
            : new DmarcMailboxFetch.Fetched(ReportPartRules.Decode(content, encoding), kind);
    }

    /// <summary>
    /// One partial fetch, and never more than <paramref name="count"/> bytes kept from it: RFC 3501 §6.4.5 has the
    /// server truncate to the range, and the copy enforces the same bound on our side.
    /// </summary>
    private async Task<SectionRead> ReadSectionAsync(UniqueId uid, string section, int count, CancellationToken cancellationToken)
    {
        Stream stream;
        try
        {
            stream = await ImapDeadline.RunAsync(
                _commandTimeout, token => _inbox.GetStreamAsync(uid, section, 0, count, token), cancellationToken);
        }
        catch (MessageNotFoundException)
        {
            return new SectionRead.Absent();
        }
        catch (ImapCommandException exception) when (exception.Response == ImapCommandResponse.No)
        {
            return new SectionRead.No();
        }
        catch (ImapCommandException exception) when (exception.Response == ImapCommandResponse.Bad)
        {
            return new SectionRead.Bad();
        }

        await using (stream)
        {
            var buffer = new byte[count];
            var length = 0;
            while (length < count && await stream.ReadAsync(buffer.AsMemory(length), cancellationToken) is var read and > 0)
                length += read;
            return new SectionRead.Bytes(buffer[..length]);
        }
    }

    /// <summary>A header block that is not read: unterminated inside its cap, or with a field it reads twice.</summary>
    private static DmarcMailboxFetch.Skipped Skip(MimeHeaderRead block) =>
        new(block is MimeHeaderRead.Ambiguous ? DmarcSkipReason.NoReportPart : DmarcSkipReason.HeaderTooLarge);

    /// <summary>
    /// A section that arrived as anything but bytes, where the part is known to exist. An OK with no data for the UID
    /// means the message is gone, and a UID is never reused (RFC 3501 §2.3.1.1, §6.4.8), so that one is terminal.
    /// </summary>
    private DmarcMailboxFetch Unread(SectionRead read)
    {
        if (read is SectionRead.Absent)
            return new DmarcMailboxFetch.Skipped(DmarcSkipReason.NotInMailbox);
        if (read is SectionRead.Bad)
            return new DmarcMailboxFetch.Failed(End(DmarcSessionFault.Protocol, _step));

        _logger.Refused(_step);
        return new DmarcMailboxFetch.Refused();
    }

    private DmarcSessionFault End(DmarcSessionFault fault, DmarcMailboxStep step)
    {
        _fault = fault;
        _logger.SessionFault(fault, step);
        return fault;
    }

    private abstract record SectionRead
    {
        public sealed record Bytes(byte[] Content) : SectionRead;

        /// <summary>OK with no data for the section (NIL, or the message is gone).</summary>
        public sealed record Absent : SectionRead;

        public sealed record No : SectionRead;

        public sealed record Bad : SectionRead;
    }
}
