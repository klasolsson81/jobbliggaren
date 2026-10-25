using System.Text;
using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Infrastructure.Mailbox.Dmarc;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Mailbox.Dmarc;

/// <summary>
/// The adapter wired to a scripted server through its internal stream seam, with a recording logger. The password is
/// a test value generated per harness; it is what the canary tests look for in every log line.
/// </summary>
internal sealed class MailboxHarness : IAsyncDisposable
{
    public MailboxHarness(ScriptedMailbox mailbox, string? serverPassword = null, string? capabilities = null, TimeSpan? commandTimeout = null)
    {
        Password = "test-" + Guid.NewGuid().ToString("N");
        Server = new ScriptedImapServer(mailbox, serverPassword ?? Password)
        {
            Capabilities = capabilities ?? ScriptedImapServer.StratoCapabilities,
        };
        Logger = new RecordingLogger<ImapDmarcMailbox>();
        Mailbox = new ImapDmarcMailbox(
            Password,
            Logger,
            new ImapStreamConnector(Server.ConnectAsync, tls: false),
            commandTimeout ?? TimeSpan.FromSeconds(10));
    }

    public string Password { get; }

    public ScriptedImapServer Server { get; }

    public RecordingLogger<ImapDmarcMailbox> Logger { get; }

    public ImapDmarcMailbox Mailbox { get; }

    public async Task<IDmarcMailboxSession> OpenAsync()
    {
        var open = await Mailbox.OpenAsync(TestContext.Current.CancellationToken);
        return open.ShouldBeOfType<DmarcMailboxOpen.Opened>().Session;
    }

    /// <summary>Opens, fetches one message's report part, and closes, as one run does for one message.</summary>
    public async Task<DmarcMailboxFetch> FetchOnlyAsync(uint uid, long size = 4096)
    {
        await using var session = await OpenAsync();
        return await session.FetchReportAsync(new DmarcMailboxEntry(uid, Received, size), TestContext.Current.CancellationToken);
    }

    /// <summary>The commands after EXAMINE: what the run itself sent, without connecting, signing in or logging out.</summary>
    public IReadOnlyList<string> CommandsAfterExamine() =>
        [.. Server.Commands.SkipWhile(command => !command.StartsWith("EXAMINE", StringComparison.Ordinal)).Skip(1).Where(command => command != "LOGOUT")];

    public static readonly DateTimeOffset Received = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    public ValueTask DisposeAsync() => Server.DisposeAsync();
}

/// <summary>Message builders for the selection tests. Every value is synthetic; nothing is copied from a real report.</summary>
internal static class Messages
{
    public const string Canary = "canary-6d1c";

    public static string TopHeader(string contentType, string? extra = null) =>
        $"From: reporter-{Canary}@example.org\r\nSubject: Report domain: jobbliggaren.se {Canary}\r\n"
        + $"MIME-Version: 1.0\r\nContent-Type: {contentType}\r\n{extra}\r\n";

    public static string PartHeader(string contentType, string? encoding = null, string? disposition = null) =>
        $"Content-Type: {contentType}\r\n"
        + (encoding is null ? "" : $"Content-Transfer-Encoding: {encoding}\r\n")
        + (disposition is null ? "" : $"Content-Disposition: {disposition}\r\n")
        + "\r\n";

    public static byte[] Bytes(string text) => Encoding.ASCII.GetBytes(text);

    /// <summary>A Yahoo-shaped message: multipart/mixed, a text part, then the report as an attachment named for it.</summary>
    public static ScriptedMessage YahooShaped(uint uid, byte[] zip) =>
        ScriptedMessage.Multipart(
            uid,
            TopHeader("multipart/mixed; boundary=\"b1\""),
            (PartHeader("text/plain; charset=UTF-8"), Bytes("This is an aggregate report.\r\n")),
            (PartHeader(
                "application/zip; name=\"example.org!jobbliggaren.se!1760918400!1761004799.zip\"",
                "base64",
                $"attachment; filename=\"example.org!jobbliggaren.se!1760918400!1761004799-{Canary}.zip\""),
                Bytes(Convert.ToBase64String(zip, Base64FormattingOptions.InsertLineBreaks) + "\r\n")));

    /// <summary>
    /// A Google-shaped message, as the IETF dmarc-report archive showed it on 2026-10-24: single-part application/zip,
    /// base64, with no text body.
    /// </summary>
    public static ScriptedMessage GoogleShaped(uint uid, byte[] zip) =>
        ScriptedMessage.SinglePart(
            uid,
            TopHeader(
                "application/zip; name=\"example.org!jobbliggaren.se!1760918400!1761004799.zip\"",
                "Content-Transfer-Encoding: base64\r\n"
                + $"Content-Disposition: attachment; filename=\"example.org!jobbliggaren.se!1760918400!1761004799-{Canary}.zip\"\r\n"),
            Bytes(Convert.ToBase64String(zip, Base64FormattingOptions.InsertLineBreaks) + "\r\n"));
}
