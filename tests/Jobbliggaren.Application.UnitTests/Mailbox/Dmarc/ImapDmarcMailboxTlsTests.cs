using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Jobbliggaren.Application.Admin.Email.Dmarc;
using Jobbliggaren.Infrastructure.Mailbox.Dmarc;
using Jobbliggaren.TestSupport;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Mailbox.Dmarc;

/// <summary>
/// #1981 PR 4a (security-auditor ruling 1) — the production TLS settings refuse a server whose certificate does not
/// chain to a trusted root, even one that names the right host. The handshake runs over the seam's stream with the
/// constant host name and every validation default MailKit has; nothing in the adapter can loosen them, since it sets
/// no callback (an architecture test bans the setter).
/// </summary>
public class ImapDmarcMailboxTlsTests
{
    [Fact]
    public async Task A_self_signed_certificate_for_the_right_host_is_refused_before_any_command()
    {
        using var certificate = SelfSignedFor(ImapDmarcMailbox.Host);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var commandsSeen = 0;
        var serving = ServeAsync(listener, certificate, () => Interlocked.Increment(ref commandsSeen));
        var mailbox = new ImapDmarcMailbox(
            "test-value",
            new RecordingLogger<ImapDmarcMailbox>(),
            new ImapStreamConnector(async cancellationToken =>
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }, tls: true),
            TimeSpan.FromSeconds(10));

        var open = await mailbox.OpenAsync(TestContext.Current.CancellationToken);

        open.ShouldBe(new DmarcMailboxOpen.Failed(DmarcSessionFault.TlsFailed));
        await serving.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        commandsSeen.ShouldBe(0);
    }

    private static async Task ServeAsync(TcpListener listener, X509Certificate2 certificate, Action onCommand)
    {
        using var socket = await listener.AcceptSocketAsync();
        await using var stream = new SslStream(new NetworkStream(socket, ownsSocket: false));
        try
        {
            await stream.AuthenticateAsServerAsync(certificate);
            await stream.WriteAsync("* OK [CAPABILITY IMAP4rev1 AUTH=PLAIN] ready\r\n"u8.ToArray());
            var buffer = new byte[1024];
            if (await stream.ReadAsync(buffer) > 0)
                onCommand();
        }
        catch (Exception exception) when (exception is IOException or System.Security.Authentication.AuthenticationException)
        {
            // The client refused the handshake, which is the outcome under test.
        }
    }

    private static X509Certificate2 SelfSignedFor(string host)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={host}", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(host);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));
        using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // SChannel cannot serve TLS from an ephemeral key; a round trip through PKCS#12 gives it a persisted one.
        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pkcs12), password: null);
    }
}
