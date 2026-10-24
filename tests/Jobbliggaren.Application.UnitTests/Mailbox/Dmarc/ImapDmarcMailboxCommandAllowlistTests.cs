using System.Text;
using System.Text.RegularExpressions;
using Jobbliggaren.Application.Admin.Email.Dmarc;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Mailbox.Dmarc;

/// <summary>
/// #1981 PR 4a (security-auditor ruling 1; CTO r1 mechanism 6–7) — every command the adapter sends over a run that
/// meets every message shape, checked against a closed allowlist. The mailbox is opened with EXAMINE and read with
/// BODY.PEEK partial ranges only, so nothing in it can change; no structure, envelope or whole message is ever asked
/// for, and no message set is open-ended.
/// <para>
/// NAMESPACE and the two LISTs are MailKit's own: <c>AuthenticateAsync</c> always follows a successful sign-in with
/// them to find the inbox and the special-use folders (measured against this server, MailKit 4.18.2). They read folder
/// names, never a message, and the <c>*</c> in the second is a folder-name wildcard, not a message set.
/// </para>
/// </summary>
public partial class ImapDmarcMailboxCommandAllowlistTests
{
    [Fact]
    public async Task A_run_over_every_message_shape_sends_only_allowlisted_commands()
    {
        var report = Encoding.ASCII.GetBytes("<feedback/>");
        var refused = Messages.GoogleShaped(9, report);
        refused.Sections["2"] = new SectionAnswer.No();
        var messages = new[]
        {
            ScriptedMessage.SinglePart(3, Messages.TopHeader("application/gzip"), report),
            Messages.GoogleShaped(4, report),
            ScriptedMessage.Multipart(5, Messages.TopHeader("multipart/report; report-type=feedback-report; boundary=b")),
            ScriptedMessage.SinglePart(6, Messages.TopHeader("text/plain"), report),
            ScriptedMessage.Multipart(
                7,
                Messages.TopHeader("multipart/mixed; boundary=b"),
                (Messages.PartHeader("application/zip"), report),
                (Messages.PartHeader("application/gzip"), report)),
            ScriptedMessage.SinglePart(8, Messages.TopHeader("application/zip", "Content-Transfer-Encoding: x-token\r\n"), report),
            refused,
        };
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(messages));
        await using (var session = await harness.OpenAsync())
        {
            var listed = (await session.ListAfterAsync(0, DmarcIngestionLimits.PerRunMessageCap, TestContext.Current.CancellationToken))
                .ShouldBeOfType<DmarcMailboxListing.Listed>();
            foreach (var entry in listed.Entries)
                await session.FetchReportAsync(entry, TestContext.Current.CancellationToken);
        }

        var commands = harness.Server.Commands;
        commands.ShouldNotBeEmpty();
        commands.Where(command => !Allowed().IsMatch(command)).ShouldBeEmpty();
        commands.ShouldContain("EXAMINE INBOX");
        commands.ShouldContain(command => command.Contains("BODY.PEEK[2]<0.1048577>", StringComparison.Ordinal),
            "the run reached a part fetch, so the allowlist was crossed by every command kind");
    }

    [Theory]
    [InlineData("SELECT INBOX")]
    [InlineData("STORE 1 +FLAGS (\\Seen)")]
    [InlineData("UID STORE 4 +FLAGS (\\Seen)")]
    [InlineData("UID MOVE 4 Trash")]
    [InlineData("UID COPY 4 Archive")]
    [InlineData("EXPUNGE")]
    [InlineData("UID EXPUNGE 4")]
    [InlineData("APPEND INBOX {10}")]
    [InlineData("UID SEARCH UID 5:*")]
    [InlineData("SEARCH ALL")]
    [InlineData("SORT (DATE) UTF-8 ALL")]
    [InlineData("THREAD REFERENCES UTF-8 ALL")]
    [InlineData("FETCH 1:* (UID)")]
    [InlineData("UID FETCH 5:* (UID)")]
    [InlineData("FETCH 3 (BODYSTRUCTURE)")]
    [InlineData("FETCH 3 (ENVELOPE)")]
    [InlineData("UID FETCH 4 (BODY[HEADER]<0.8192>)")]
    [InlineData("UID FETCH 4 (BODY.PEEK[])")]
    [InlineData("UID FETCH 4 (BODY.PEEK[2])")]
    [InlineData("UID FETCH 4 (RFC822)")]
    [InlineData("UID FETCH 4:9 (BODY.PEEK[HEADER]<0.8192>)")]
    [InlineData("FETCH 3 (UID FLAGS)")]
    [InlineData("LOGIN dmarc@jobbliggaren.se secret")]
    [InlineData("AUTHENTICATE LOGIN")]
    [InlineData("NOOP")]
    [InlineData("IDLE")]
    [InlineData("CHECK")]
    public void The_allowlist_refuses_what_a_read_only_reader_must_never_send(string command)
    {
        Allowed().IsMatch(command).ShouldBeFalse();
    }

    /// <summary>
    /// The whole command language of a run. Message sets are explicit numbers; every content fetch is one UID and one
    /// BODY.PEEK section with a partial range.
    /// </summary>
    [GeneratedRegex(
        """^(?:CAPABILITY|ID .*|AUTHENTICATE PLAIN|NAMESPACE|LIST (?:\(SPECIAL-USE\) )?"" "(?:INBOX|\*)" RETURN \(SUBSCRIBED CHILDREN\)|EXAMINE INBOX|FETCH \d+ UID|FETCH \d+(?::\d+)? \(UID INTERNALDATE RFC822\.SIZE\)|UID FETCH \d+ \(BODY\.PEEK\[(?:HEADER|\d\.MIME|\d)\]<0\.\d+>\)|LOGOUT)$""",
        RegexOptions.CultureInvariant)]
    private static partial Regex Allowed();
}
