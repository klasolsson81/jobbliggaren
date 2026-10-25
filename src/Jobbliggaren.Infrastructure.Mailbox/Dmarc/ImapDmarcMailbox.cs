using Jobbliggaren.Application.Admin.Email.Dmarc;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Logging;

namespace Jobbliggaren.Infrastructure.Mailbox.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0185) — <c>dmarc@jobbliggaren.se</c> at STRATO, read over implicit TLS and opened with EXAMINE, so
/// nothing in the mailbox can change: no flag, no move, no deletion. The host, port and account are constants, a
/// one-member allowlist by construction. Certificate and revocation validation are MailKit's defaults and nothing here
/// touches them; a missing root CA belongs in the image's trust store, never in a callback.
/// <para>
/// Sign-in is exactly one AUTHENTICATE PLAIN, through the <see cref="SaslMechanism"/> overload: the credential overload
/// would try every advertised mechanism and then LOGIN, several refused logins for one wrong password against a lockout
/// policy nobody has measured. STRATO advertised AUTH=PLAIN and AUTH=LOGIN before sign-in, read anonymously on
/// 2026-10-24; a server that stops advertising PLAIN is never sent the credential.
/// </para>
/// <para>
/// A singleton with no state of its own: every <see cref="OpenAsync"/> builds a new client, and the session that
/// owns it disposes it.
/// </para>
/// </summary>
internal sealed class ImapDmarcMailbox : IDmarcMailbox
{
    public const string Host = "imap.strato.de";

    public const int Port = 993;

    public const string Username = "dmarc@jobbliggaren.se";

    public const string Mechanism = "PLAIN";

    private readonly string _password;
    private readonly ILogger<ImapDmarcMailbox> _logger;
    private readonly ImapStreamConnector? _connector;
    private readonly TimeSpan _commandTimeout;

    public ImapDmarcMailbox(string password, ILogger<ImapDmarcMailbox> logger)
        : this(password, logger, connector: null, DmarcIngestionLimits.CommandTimeout)
    {
    }

    internal ImapDmarcMailbox(
        string password, ILogger<ImapDmarcMailbox> logger, ImapStreamConnector? connector, TimeSpan commandTimeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentNullException.ThrowIfNull(logger);
        _password = password;
        _logger = logger;
        _connector = connector;
        _commandTimeout = commandTimeout;
    }

    public async ValueTask<DmarcMailboxOpen> OpenAsync(CancellationToken cancellationToken)
    {
        ImapClient? client = new() { Timeout = (int)_commandTimeout.TotalMilliseconds };
        var step = DmarcMailboxStep.Connect;
        var logOut = false;
        try
        {
            await ImapDeadline.RunAsync(_commandTimeout, token => ConnectAsync(client, token), cancellationToken);

            step = DmarcMailboxStep.Authenticate;
            if (!client.AuthenticationMechanisms.Contains(Mechanism))
            {
                logOut = true;
                _logger.MechanismNotAdvertised();
                return new DmarcMailboxOpen.Failed(DmarcSessionFault.Protocol);
            }

            await ImapDeadline.RunAsync(
                _commandTimeout,
                token => client.AuthenticateAsync(new SaslMechanismPlain(Username, _password), token),
                cancellationToken);

            step = DmarcMailboxStep.Examine;
            var inbox = client.Inbox;
            await ImapDeadline.RunAsync(_commandTimeout, token => inbox.OpenAsync(FolderAccess.ReadOnly, token), cancellationToken);
            if (inbox.UidValidity == 0)
            {
                logOut = true;
                _logger.OpenFailed(DmarcSessionFault.Protocol, step);
                return new DmarcMailboxOpen.Failed(DmarcSessionFault.Protocol);
            }

            var session = new ImapDmarcMailboxSession(client, inbox, inbox.UidValidity, inbox.Count, _commandTimeout, _logger);
            client = null;
            return new DmarcMailboxOpen.Opened(session);
        }
        catch (Exception exception) when (ImapFaults.FaultOf(exception, step, cancellationToken) is { } fault)
        {
            _logger.OpenFailed(fault, step);
            return new DmarcMailboxOpen.Failed(fault);
        }
        finally
        {
            if (client is not null)
                await ImapDmarcMailboxSession.CloseAsync(client, _logger, logOut);
        }
    }

    private async Task ConnectAsync(ImapClient client, CancellationToken cancellationToken)
    {
        if (_connector is null)
        {
            await client.ConnectAsync(Host, Port, SecureSocketOptions.SslOnConnect, cancellationToken);
            return;
        }

        var stream = await _connector.OpenAsync(cancellationToken);
        var connected = false;
        try
        {
            await client.ConnectAsync(
                stream,
                Host,
                Port,
                _connector.Tls ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.None,
                cancellationToken);
            connected = true;
        }
        finally
        {
            if (!connected)
                await stream.DisposeAsync();
        }
    }
}

/// <summary>
/// The tests' way in: a stream to a scripted server instead of a socket to the constant host. With
/// <see cref="Tls"/> the handshake runs over that stream with the production settings and names the constant host, so
/// validation is tested as it runs; without it the scripted server speaks plaintext. Only the internal constructor
/// takes one, and nothing in production code builds one.
/// </summary>
internal sealed class ImapStreamConnector(Func<CancellationToken, ValueTask<Stream>> open, bool tls)
{
    public bool Tls { get; } = tls;

    public ValueTask<Stream> OpenAsync(CancellationToken cancellationToken) => open(cancellationToken);
}
