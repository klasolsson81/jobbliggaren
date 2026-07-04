using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;

namespace Jobbliggaren.Infrastructure.CompanyRegister.Scb;

/// <summary>
/// #560 (ADR 0091) — loads the SCB client certificate from the Windows certificate store by
/// thumbprint. This is a NEW pattern for the codebase (no cert-store loading existed before). SCB's
/// current API authenticates with a client certificate; loading it by thumbprint means the cert's
/// PASSWORD is never needed in config (the private key is unlocked by the OS keystore), so nothing
/// secret lives in <c>appsettings</c> — only the non-secret thumbprint (ADR 0091; the auth seam is
/// one place, ADR 0088 D3 — a future Sept-2026 API-key impl swaps in here).
///
/// <para>
/// Only invoked when <c>ScbRegister:Enabled=true</c> (the real population run on the dev machine
/// where Klas installed cert A01489). CI and cert-less dev never construct the real client, so this
/// never runs there. The loaded cert lives for the app lifetime attached to the SCB
/// <c>HttpClient</c>'s handler (not disposed here — parity the app-lifetime rate limiter).
/// </para>
/// </summary>
internal sealed class ScbClientCertificateProvider(IOptions<ScbRegisterOptions> options)
{
    /// <summary>
    /// Finds the client cert (with a private key) in <c>&lt;location&gt;\My</c> by its normalized
    /// thumbprint. Fails loud (never silently proceeds without client auth) if the thumbprint is
    /// unset, the cert is absent, or it has no private key.
    /// </summary>
    public X509Certificate2 Load()
    {
        var opts = options.Value;

        if (string.IsNullOrWhiteSpace(opts.CertThumbprint))
        {
            throw new InvalidOperationException(
                "ScbRegister:CertThumbprint saknas — krävs när ScbRegister:Enabled=true. Sätt den i " +
                "gitignored appsettings.Local.json eller via env-override (ScbRegister__CertThumbprint).");
        }

        var thumbprint = NormalizeThumbprint(opts.CertThumbprint);
        var location = Enum.TryParse<StoreLocation>(opts.CertStoreLocation, ignoreCase: true, out var loc)
            ? loc
            : StoreLocation.CurrentUser;

        using var store = new X509Store(StoreName.My, location);
        store.Open(OpenFlags.ReadOnly);

        // validOnly:false — SCB's client cert need not chain to a locally-trusted root for us to
        // present it; the server validates it. We still require a usable private key below.
        var matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"SCB-klientcertifikat hittades inte i {location}\\My (thumbprint slutar på …{Tail(thumbprint)}). " +
                "Installera certet (docs/scb .pfx) eller korrigera ScbRegister:CertThumbprint.");
        }

        var cert = matches.OfType<X509Certificate2>().FirstOrDefault(c => c.HasPrivateKey);
        if (cert is null)
        {
            throw new InvalidOperationException(
                "SCB-certifikatet hittades men saknar privat nyckel och kan inte användas för klient-autentisering. " +
                "Importera .pfx:en med privat nyckel (markera nyckeln som exportbar behövs ej).");
        }

        return cert;
    }

    // Cert-store UIs render thumbprints with spaces and sometimes a leading invisible mark; FindBy-
    // Thumbprint wants the bare uppercase hex. Never log the full value.
    private static string NormalizeThumbprint(string raw) =>
        new string(raw.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();

    private static string Tail(string thumbprint) =>
        thumbprint.Length <= 4 ? thumbprint : thumbprint[^4..];
}
