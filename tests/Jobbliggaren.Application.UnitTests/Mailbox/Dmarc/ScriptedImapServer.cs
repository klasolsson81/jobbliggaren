using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace Jobbliggaren.Application.UnitTests.Mailbox.Dmarc;

/// <summary>
/// A scripted IMAP4rev1 server for the DMARC mailbox adapter, in plaintext over a loopback socket (the adapter's
/// internal stream seam). It answers from a small mailbox model and records every command a client sends, tag
/// stripped, so a test can pin exactly what the adapter asked for. A command it does not know is recorded and answered
/// with a tagged BAD, which keeps it visible to the allowlist test rather than hidden behind a fake success.
/// <para>
/// Its greeting advertises the capabilities STRATO's server advertised before sign-in, read anonymously on 2026-10-24.
/// </para>
/// </summary>
internal sealed partial class ScriptedImapServer : IAsyncDisposable
{
    public const string StratoCapabilities =
        "IMAP4 IMAP4rev1 APPENDLIMIT=104857600 AUTH=PLAIN AUTH=LOGIN ENABLE ID IDLE MOVE LIST-EXTENDED NAMESPACE QUOTA SORT SPECIAL-USE UIDPLUS";

    private readonly TcpListener _listener;
    private readonly Task _accepting;
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<string> _commands = [];
    private readonly List<Task> _sessions = [];
    private readonly Lock _gate = new();
    private int _credentialsReceived;

    public ScriptedImapServer(ScriptedMailbox mailbox, string password)
    {
        Mailbox = mailbox;
        Password = password;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _accepting = AcceptAsync();
    }

    public ScriptedMailbox Mailbox { get; }

    public string Password { get; }

    public string Capabilities { get; init; } = StratoCapabilities;

    /// <summary>Called with each command, tag stripped, before the default answer; a non-null result replaces it.</summary>
    public Func<string, Interception?> Intercept { get; set; } = _ => null;

    /// <summary>What the server does once an AUTHENTICATE credential has arrived: a hang, a BYE, a drop or a reset.</summary>
    public Interception? AfterCredential { get; set; }

    public IReadOnlyList<string> Commands
    {
        get
        {
            lock (_gate)
                return [.. _commands];
        }
    }

    /// <summary>How many times a client sent a credential, in an AUTHENTICATE continuation or a LOGIN.</summary>
    public int CredentialsReceived => Volatile.Read(ref _credentialsReceived);

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Opens one plaintext connection to this server.</summary>
    public async ValueTask<Stream> ConnectAsync(CancellationToken cancellationToken)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, Port), cancellationToken);
        return new NetworkStream(socket, ownsSocket: true);
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _listener.Stop();
        Task[] sessions;
        lock (_gate)
            sessions = [.. _sessions];
        await Task.WhenAll([_accepting, .. sessions]).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _stopping.Dispose();
    }

    private async Task AcceptAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = await _listener.AcceptSocketAsync(_stopping.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            lock (_gate)
                _sessions.Add(ServeAsync(socket));
        }
    }

    private async Task ServeAsync(Socket socket)
    {
        await using var stream = new NetworkStream(socket, ownsSocket: true);
        var reader = new LineReader(stream);
        try
        {
            await WriteAsync(stream, $"* OK [CAPABILITY {Capabilities}] IMAP server ready\r\n");
            while (await reader.ReadLineAsync(_stopping.Token) is { } line)
            {
                var space = line.IndexOf(' ', StringComparison.Ordinal);
                if (space <= 0)
                    continue;

                var tag = line[..space];
                var command = line[(space + 1)..];
                lock (_gate)
                    _commands.Add(command);

                if (!await AnswerAsync(stream, reader, tag, command))
                    return;
            }
        }
        catch (Exception exception) when (exception is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The client went away or the test ended; a scripted server has nothing to report.
        }
    }

    /// <summary>Answers one command; <see langword="false"/> closes the connection.</summary>
    private async Task<bool> AnswerAsync(NetworkStream stream, LineReader reader, string tag, string command)
    {
        switch (Intercept(command))
        {
            case Interception.TaggedNo:
                await WriteAsync(stream, $"{tag} NO Scripted refusal\r\n");
                return true;
            case Interception.TaggedBad:
                await WriteAsync(stream, $"{tag} BAD Scripted protocol error\r\n");
                return true;
            case Interception.Bye:
                await WriteAsync(stream, "* BYE Scripted shutdown\r\n");
                return false;
            case Interception.Drop:
                return false;
            case Interception.Reset:
                Reset(stream);
                return false;
            case Interception.Hang:
                await Task.Delay(Timeout.Infinite, _stopping.Token);
                return false;
            case Interception.Prepend { UntaggedLines: var lines }:
                await WriteAsync(stream, lines);
                break;
            case Interception.Raw { Answer: var answer }:
                await WriteAsync(stream, answer(tag));
                return true;
        }

        var verb = command.Split(' ', 2)[0].ToUpperInvariant();
        switch (verb)
        {
            case "CAPABILITY":
                await WriteAsync(stream, $"* CAPABILITY {Capabilities}\r\n{tag} OK CAPABILITY completed\r\n");
                return true;
            case "AUTHENTICATE":
                return await AuthenticateAsync(stream, reader, tag);
            case "LOGIN":
                Interlocked.Increment(ref _credentialsReceived);
                await WriteAsync(stream, $"{tag} NO [AUTHENTICATIONFAILED] Authentication failed\r\n");
                return true;
            case "NAMESPACE":
                await WriteAsync(stream, $"* NAMESPACE ((\"\" \"/\")) NIL NIL\r\n{tag} OK NAMESPACE completed\r\n");
                return true;
            case "LIST":
                await WriteAsync(stream, $"* LIST (\\HasNoChildren) \"/\" INBOX\r\n{tag} OK LIST completed\r\n");
                return true;
            case "EXAMINE" or "SELECT":
                await WriteAsync(
                    stream,
                    "* FLAGS (\\Answered \\Flagged \\Deleted \\Seen \\Draft)\r\n"
                    + $"* {Mailbox.Exists} EXISTS\r\n"
                    + "* 0 RECENT\r\n"
                    + $"* OK [UIDVALIDITY {Mailbox.UidValidity}] UIDs valid\r\n"
                    + $"* OK [UIDNEXT {Mailbox.UidNext}] Predicted next UID\r\n"
                    + "* OK [PERMANENTFLAGS ()] No permanent flags permitted\r\n"
                    + $"{tag} OK [{(verb == "EXAMINE" ? "READ-ONLY" : "READ-WRITE")}] {verb} completed\r\n");
                return true;
            case "FETCH":
                await FetchAsync(stream, tag, command["FETCH ".Length..], byUid: false);
                return true;
            case "UID" when command.StartsWith("UID FETCH ", StringComparison.OrdinalIgnoreCase):
                await FetchAsync(stream, tag, command["UID FETCH ".Length..], byUid: true);
                return true;
            case "NOOP" or "ID" or "ENABLE":
                await WriteAsync(stream, $"{tag} OK {verb} completed\r\n");
                return true;
            case "LOGOUT":
                await WriteAsync(stream, $"* BYE Logging out\r\n{tag} OK LOGOUT completed\r\n");
                return false;
            default:
                await WriteAsync(stream, $"{tag} BAD Command unrecognized\r\n");
                return true;
        }
    }

    private async Task<bool> AuthenticateAsync(NetworkStream stream, LineReader reader, string tag)
    {
        await WriteAsync(stream, "+ \r\n");
        var response = await reader.ReadLineAsync(_stopping.Token);
        if (response is null)
            return false;
        if (response == "*")
        {
            await WriteAsync(stream, $"{tag} BAD Authentication cancelled\r\n");
            return true;
        }

        Interlocked.Increment(ref _credentialsReceived);
        switch (AfterCredential)
        {
            case Interception.Hang:
                await Task.Delay(Timeout.Infinite, _stopping.Token);
                return false;
            case Interception.Bye:
                await WriteAsync(stream, "* BYE Scripted shutdown\r\n");
                return false;
            case Interception.Drop:
                return false;
            case Interception.Reset:
                Reset(stream);
                return false;
        }

        var parts = Encoding.UTF8.GetString(Convert.FromBase64String(response)).Split('\0');
        var accepted = parts is [_, "dmarc@jobbliggaren.se", var password] && password == Password;
        await WriteAsync(
            stream,
            accepted
                ? $"{tag} OK [CAPABILITY {Capabilities}] Logged in\r\n"
                : $"{tag} NO [AUTHENTICATIONFAILED] Authentication failed\r\n");
        return true;
    }

    private async Task FetchAsync(NetworkStream stream, string tag, string arguments, bool byUid)
    {
        var match = FetchArguments().Match(arguments);
        if (!match.Success)
        {
            await WriteAsync(stream, $"{tag} BAD Malformed FETCH\r\n");
            return;
        }

        var items = match.Groups["items"].Value;
        var section = BodySection().Match(items);
        var answer = new List<byte>();
        foreach (var number in ParseSet(match.Groups["set"].Value))
        {
            var sequence = byUid ? Mailbox.SequenceOf(number) : (int)number;
            if (sequence < 1 || sequence > Mailbox.Messages.Count)
                continue;

            var message = Mailbox.Messages[sequence - 1];
            var fields = new List<string>();
            if (byUid || items.Contains("UID", StringComparison.OrdinalIgnoreCase))
                fields.Add($"UID {message.Uid}");
            if (items.Contains("RFC822.SIZE", StringComparison.OrdinalIgnoreCase))
                fields.Add($"RFC822.SIZE {message.Size}");
            if (items.Contains("INTERNALDATE", StringComparison.OrdinalIgnoreCase))
                fields.Add($"INTERNALDATE \"{message.InternalDate.ToString("dd-MMM-yyyy HH:mm:ss +0000", CultureInfo.InvariantCulture)}\"");

            if (!section.Success)
            {
                answer.AddRange(Encoding.ASCII.GetBytes($"* {sequence} FETCH ({string.Join(' ', fields)})\r\n"));
                continue;
            }

            var name = section.Groups["section"].Value;
            var origin = section.Groups["origin"].Success ? int.Parse(section.Groups["origin"].Value, CultureInfo.InvariantCulture) : 0;
            var count = section.Groups["count"].Success ? int.Parse(section.Groups["count"].Value, CultureInfo.InvariantCulture) : int.MaxValue;
            var label = section.Groups["origin"].Success ? $"BODY[{name}]<{origin}>" : $"BODY[{name}]";
            switch (message.SectionAnswer(name))
            {
                case SectionAnswer.No:
                    await WriteAsync(stream, $"{tag} NO Scripted refusal of {name}\r\n");
                    return;
                case SectionAnswer.Bad:
                    await WriteAsync(stream, $"{tag} BAD Scripted protocol error on {name}\r\n");
                    return;
                case SectionAnswer.Nil:
                    answer.AddRange(Encoding.ASCII.GetBytes($"* {sequence} FETCH ({string.Join(' ', fields)} {label} NIL)\r\n"));
                    break;
                case SectionAnswer.Empty:
                    answer.AddRange(Encoding.ASCII.GetBytes($"* {sequence} FETCH ({string.Join(' ', fields)} {label} \"\")\r\n"));
                    break;
                case SectionAnswer.Bytes { Content: var content }:
                    var slice = content.Skip(origin).Take(count).ToArray();
                    answer.AddRange(Encoding.ASCII.GetBytes($"* {sequence} FETCH ({string.Join(' ', fields)} {label} {{{slice.Length}}}\r\n"));
                    answer.AddRange(slice);
                    answer.AddRange(")\r\n"u8.ToArray());
                    break;
            }
        }

        answer.AddRange(Encoding.ASCII.GetBytes($"{tag} OK FETCH completed\r\n"));
        await stream.WriteAsync(answer.ToArray(), _stopping.Token);
    }

    /// <summary>A sequence or UID set as a client wrote it. <c>*</c> is read as the largest value present.</summary>
    private IEnumerable<long> ParseSet(string set)
    {
        foreach (var range in set.Split(','))
        {
            var bounds = range.Split(':');
            var low = Bound(bounds[0]);
            var high = bounds.Length == 2 ? Bound(bounds[1]) : low;
            for (var value = Math.Min(low, high); value <= Math.Max(low, high); value++)
                yield return value;
        }

        long Bound(string text) => text == "*" ? Mailbox.Messages.Count : long.Parse(text, CultureInfo.InvariantCulture);
    }

    /// <summary>A zero linger, so closing the socket sends a TCP reset rather than a FIN.</summary>
    private static void Reset(NetworkStream stream) => stream.Socket.LingerState = new LingerOption(enable: true, seconds: 0);

    private Task WriteAsync(NetworkStream stream, string text) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes(text), _stopping.Token).AsTask();

    [GeneratedRegex(@"^(?<set>\S+) (?:\((?<items>.*)\)|(?<items>\S+))$", RegexOptions.CultureInvariant)]
    private static partial Regex FetchArguments();

    [GeneratedRegex(@"BODY\.PEEK\[(?<section>[^\]]*)\](?:<(?<origin>\d+)\.(?<count>\d+)>)?", RegexOptions.CultureInvariant)]
    private static partial Regex BodySection();

    /// <summary>Reads CRLF-terminated lines; a test server needs nothing cleverer.</summary>
    private sealed class LineReader(Stream stream)
    {
        private readonly byte[] _buffer = new byte[64 * 1024];
        private int _start;
        private int _end;

        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            var line = new List<byte>();
            while (true)
            {
                if (_start == _end)
                {
                    _start = 0;
                    _end = await stream.ReadAsync(_buffer, cancellationToken);
                    if (_end == 0)
                        return null;
                }

                var value = _buffer[_start++];
                if (value == (byte)'\n')
                    return Encoding.ASCII.GetString([.. line]).TrimEnd('\r');
                line.Add(value);
            }
        }
    }
}

/// <summary>A scripted answer that replaces the server's default one for a single command.</summary>
internal abstract record Interception
{
    public sealed record TaggedNo : Interception;

    public sealed record TaggedBad : Interception;

    /// <summary>An untagged BYE, then the connection closes.</summary>
    public sealed record Bye : Interception;

    /// <summary>The connection closes without a word.</summary>
    public sealed record Drop : Interception;

    /// <summary>The connection is reset (a TCP RST), not closed.</summary>
    public sealed record Reset : Interception;

    /// <summary>No answer at all, for as long as the test runs.</summary>
    public sealed record Hang : Interception;

    /// <summary>Untagged lines sent first, then the default answer.</summary>
    public sealed record Prepend(string UntaggedLines) : Interception;

    /// <summary>The whole answer, tagged line included, written as given for the command's tag.</summary>
    public sealed record Raw(Func<string, string> Answer) : Interception;
}

/// <summary>The inbox a scripted server serves: messages in ascending UID order, as RFC 3501 §2.3.1.2 orders them.</summary>
internal sealed class ScriptedMailbox(uint uidValidity, IReadOnlyList<ScriptedMessage> messages)
{
    public uint UidValidity { get; } = uidValidity;

    public IReadOnlyList<ScriptedMessage> Messages { get; } = messages;

    public int Exists => Messages.Count;

    public uint UidNext => Messages.Count == 0 ? 1 : Messages[^1].Uid + 1;

    public int SequenceOf(long uid)
    {
        for (var index = 0; index < Messages.Count; index++)
        {
            if (Messages[index].Uid == uid)
                return index + 1;
        }

        return 0;
    }

    public static ScriptedMailbox Of(params ScriptedMessage[] messages) => new(uidValidity: 7, messages);

    /// <summary>One message per UID, each holding nothing but metadata.</summary>
    public static ScriptedMailbox WithUids(IEnumerable<uint> uids) =>
        new(uidValidity: 7, [.. uids.Select(uid => new ScriptedMessage(uid))]);
}

/// <summary>
/// One message as a server would expose it to section fetches. Sections a test does not declare answer NIL, the way
/// a server answers for a part that does not exist.
/// </summary>
internal sealed class ScriptedMessage(uint uid)
{
    public uint Uid { get; } = uid;

    public DateTimeOffset InternalDate { get; init; } = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    public uint Size { get; init; } = 4096;

    public Dictionary<string, SectionAnswer> Sections { get; } = new(StringComparer.OrdinalIgnoreCase);

    public SectionAnswer SectionAnswer(string section) =>
        Sections.TryGetValue(section, out var answer) ? answer : new SectionAnswer.Nil();

    /// <summary>A single-part message: the top-level header and part 1, its body.</summary>
    public static ScriptedMessage SinglePart(uint uid, string header, byte[] body)
    {
        var message = new ScriptedMessage(uid);
        message.Sections["HEADER"] = new SectionAnswer.Bytes(Encoding.ASCII.GetBytes(header));
        message.Sections["1"] = new SectionAnswer.Bytes(body);
        return message;
    }

    /// <summary>A multipart message: the top-level header and, per part, its MIME header and its body.</summary>
    public static ScriptedMessage Multipart(uint uid, string header, params (string MimeHeader, byte[] Body)[] parts)
    {
        var message = new ScriptedMessage(uid);
        message.Sections["HEADER"] = new SectionAnswer.Bytes(Encoding.ASCII.GetBytes(header));
        for (var index = 0; index < parts.Length; index++)
        {
            message.Sections[$"{index + 1}.MIME"] = new SectionAnswer.Bytes(Encoding.ASCII.GetBytes(parts[index].MimeHeader));
            message.Sections[$"{index + 1}"] = new SectionAnswer.Bytes(parts[index].Body);
        }

        return message;
    }
}

internal abstract record SectionAnswer
{
    public sealed record Bytes(byte[] Content) : SectionAnswer;

    public sealed record Nil : SectionAnswer;

    public sealed record Empty : SectionAnswer;

    public sealed record No : SectionAnswer;

    public sealed record Bad : SectionAnswer;
}
