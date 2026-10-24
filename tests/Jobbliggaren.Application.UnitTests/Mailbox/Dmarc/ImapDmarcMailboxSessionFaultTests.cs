using System.Net;
using System.Net.Sockets;
using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Infrastructure.Mailbox.Dmarc;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Mailbox.Dmarc;

/// <summary>
/// #1981 PR 4a — the failure classes (CTO r2 §2a) and the sign-in bound (security-auditor B1). A session fault is a
/// value the run records; only the caller's own cancellation propagates.
/// </summary>
public class ImapDmarcMailboxSessionFaultTests
{
    [Fact]
    public async Task A_refused_login_sends_exactly_one_authentication_command_B1()
    {
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(), serverPassword: "another-test-value");

        var open = await harness.Mailbox.OpenAsync(TestContext.Current.CancellationToken);

        open.ShouldBe(new DmarcMailboxOpen.Failed(DmarcSessionFault.AuthRefused));
        harness.Server.Commands.Count(command => command.StartsWith("AUTHENTICATE", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("LOGIN", StringComparison.OrdinalIgnoreCase)).ShouldBe(1);
        harness.Server.Commands.ShouldContain("AUTHENTICATE PLAIN");
        harness.Server.CredentialsReceived.ShouldBe(1);
    }

    [Theory]
    [InlineData("IMAP4rev1 AUTH=LOGIN")]
    [InlineData("IMAP4rev1 AUTH=CRAM-MD5 AUTH=SCRAM-SHA-256")]
    [InlineData("IMAP4rev1")]
    public async Task A_server_that_does_not_advertise_plain_is_never_sent_the_credential_B1(string capabilities)
    {
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(), capabilities: capabilities);

        var open = await harness.Mailbox.OpenAsync(TestContext.Current.CancellationToken);

        open.ShouldBe(new DmarcMailboxOpen.Failed(DmarcSessionFault.Protocol));
        harness.Server.CredentialsReceived.ShouldBe(0);
        harness.Server.Commands.ShouldNotContain(command => command.StartsWith("AUTHENTICATE", StringComparison.OrdinalIgnoreCase)
            || command.StartsWith("LOGIN", StringComparison.OrdinalIgnoreCase));
        harness.Logger.Records.ShouldContain(record => record.EventId.Id == 3136);
    }

    [Fact]
    public async Task A_host_that_refuses_the_connection_is_unreachable()
    {
        var closedPort = ClosedLoopbackPort();
        var mailbox = new ImapDmarcMailbox(
            "test-value",
            new RecordingLogger<ImapDmarcMailbox>(),
            new ImapStreamConnector(async cancellationToken =>
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, closedPort), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }, tls: false),
            TimeSpan.FromSeconds(5));

        (await mailbox.OpenAsync(TestContext.Current.CancellationToken)).ShouldBe(new DmarcMailboxOpen.Failed(DmarcSessionFault.Unreachable));
    }

    [Fact]
    public async Task A_server_that_never_greets_is_a_timeout()
    {
        await using var harness = new SilentServer();
        var mailbox = new ImapDmarcMailbox(
            "test-value", new RecordingLogger<ImapDmarcMailbox>(), new ImapStreamConnector(harness.ConnectAsync, tls: false), TimeSpan.FromMilliseconds(300));

        (await mailbox.OpenAsync(TestContext.Current.CancellationToken)).ShouldBe(new DmarcMailboxOpen.Failed(DmarcSessionFault.Timeout));
    }

    [Fact]
    public async Task A_command_the_server_never_answers_is_a_timeout()
    {
        await using var harness = new MailboxHarness(ScriptedMailbox.WithUids([3, 6, 9]), commandTimeout: TimeSpan.FromMilliseconds(500));
        await using var session = await harness.OpenAsync();
        harness.Server.Intercept = command => command.StartsWith("FETCH", StringComparison.Ordinal) ? new Interception.Hang() : null;

        (await session.ListAfterAsync(0, 10, TestContext.Current.CancellationToken))
            .ShouldBe(new DmarcMailboxListing.Failed(DmarcSessionFault.Timeout));
    }

    [Fact]
    public async Task A_bye_in_the_middle_of_the_run_ends_the_session()
    {
        await using var harness = new MailboxHarness(ScriptedMailbox.WithUids([3, 6, 9]));
        await using var session = await harness.OpenAsync();
        harness.Server.Intercept = command => command.StartsWith("FETCH", StringComparison.Ordinal) ? new Interception.Bye() : null;

        (await session.ListAfterAsync(0, 10, TestContext.Current.CancellationToken))
            .ShouldBe(new DmarcMailboxListing.Failed(DmarcSessionFault.Protocol));
    }

    [Fact]
    public async Task A_connection_dropped_during_a_fetch_ends_the_session()
    {
        await using var harness = new MailboxHarness(ScriptedMailbox.Of(Messages.GoogleShaped(5, Messages.Bytes("zip"))));
        await using var session = await harness.OpenAsync();
        harness.Server.Intercept = command => command.Contains("[2.MIME]", StringComparison.Ordinal) ? new Interception.Drop() : null;

        (await session.FetchReportAsync(new DmarcMailboxEntry(5, MailboxHarness.Received, 100), TestContext.Current.CancellationToken))
            .ShouldBe(new DmarcMailboxFetch.Failed(DmarcSessionFault.Protocol));
    }

    [Fact]
    public async Task A_bad_to_examine_fails_the_open()
    {
        await using var harness = new MailboxHarness(ScriptedMailbox.Of());
        harness.Server.Intercept = command => command.StartsWith("EXAMINE", StringComparison.Ordinal) ? new Interception.TaggedBad() : null;

        (await harness.Mailbox.OpenAsync(TestContext.Current.CancellationToken)).ShouldBe(new DmarcMailboxOpen.Failed(DmarcSessionFault.Protocol));
    }

    [Fact]
    public async Task The_callers_cancellation_propagates()
    {
        await using var harness = new MailboxHarness(ScriptedMailbox.WithUids([3, 6, 9]));
        await using var session = await harness.OpenAsync();
        harness.Server.Intercept = command => command.StartsWith("FETCH", StringComparison.Ordinal) ? new Interception.Hang() : null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));

        await Should.ThrowAsync<OperationCanceledException>(async () => await session.ListAfterAsync(0, 10, cancellation.Token));
    }

    [Fact]
    public async Task A_cancellation_before_the_open_propagates()
    {
        await using var harness = new MailboxHarness(ScriptedMailbox.Of());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await harness.Mailbox.OpenAsync(cancellation.Token));
    }

    [Fact]
    public async Task Disposing_the_session_logs_out()
    {
        await using var harness = new MailboxHarness(ScriptedMailbox.Of());
        var session = await harness.OpenAsync();

        await session.DisposeAsync();

        harness.Server.Commands[^1].ShouldBe("LOGOUT");
    }

    [Fact]
    public async Task No_failure_log_line_carries_the_password_or_the_servers_text()
    {
        await using var refused = new MailboxHarness(ScriptedMailbox.Of(), serverPassword: "another-test-value");
        await refused.Mailbox.OpenAsync(TestContext.Current.CancellationToken);

        var record = refused.Logger.Records.ShouldHaveSingleItem();
        record.EventId.Id.ShouldBe(3135);
        var text = record.Message + string.Join(' ', record.Properties.Select(property => property.Value));
        text.ShouldNotContain(refused.Password);
        text.ShouldNotContain("AUTHENTICATIONFAILED", Case.Insensitive);
        text.ShouldContain(nameof(DmarcSessionFault.AuthRefused));
    }

    private static int ClosedLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>Accepts connections and never writes a byte.</summary>
    private sealed class SilentServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly List<Socket> _accepted = [];

        public SilentServer() => _listener.Start();

        public async ValueTask<Stream> ConnectAsync(CancellationToken cancellationToken)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)_listener.LocalEndpoint).Port), cancellationToken);
            _accepted.Add(await _listener.AcceptSocketAsync(cancellationToken));
            return new NetworkStream(socket, ownsSocket: true);
        }

        public ValueTask DisposeAsync()
        {
            foreach (var socket in _accepted)
                socket.Dispose();
            _listener.Stop();
            return ValueTask.CompletedTask;
        }
    }
}
