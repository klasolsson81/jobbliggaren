using System.Net.Sockets;
using Jobbliggaren.Application.Admin.Email.Dmarc;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;

namespace Jobbliggaren.Infrastructure.Mailbox.Dmarc;

/// <summary>
/// #1981 PR 4a (ADR 0183, CTO r2 §2a) — the exceptions a session can end in, each mapped to the fault the run records.
/// The set is closed: an exception not named here is a defect and propagates, and so does the caller's own
/// cancellation. A tagged NO reaches this only where the step has no per-message meaning (opening, listing); per
/// message it is a refusal, decided by the caller before this is asked.
/// </summary>
internal static class ImapFaults
{
    public static DmarcSessionFault? FaultOf(Exception exception, DmarcMailboxStep step, CancellationToken cancellationToken) =>
        exception switch
        {
            OperationCanceledException when cancellationToken.IsCancellationRequested => null,
            OperationCanceledException or TimeoutException => DmarcSessionFault.Timeout,
            SslHandshakeException or System.Security.Authentication.AuthenticationException => DmarcSessionFault.TlsFailed,
            MailKit.Security.AuthenticationException => DmarcSessionFault.AuthRefused,
            SocketException { SocketErrorCode: SocketError.TimedOut } => DmarcSessionFault.Timeout,
            SocketException when step == DmarcMailboxStep.Connect => DmarcSessionFault.Unreachable,
            IOException { InnerException: SocketException { SocketErrorCode: SocketError.TimedOut } } => DmarcSessionFault.Timeout,
            IOException when step == DmarcMailboxStep.Connect => DmarcSessionFault.Unreachable,
            SocketException or IOException => DmarcSessionFault.Protocol,
            ImapProtocolException or ImapCommandException or SaslException => DmarcSessionFault.Protocol,
            ServiceNotConnectedException or ServiceNotAuthenticatedException or FolderNotOpenException => DmarcSessionFault.Protocol,
            _ => null,
        };
}
