using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// Fas 4b PR-9b (ADR 0100 §D3 read-path, DPIA #659 M-F2) — HTTP wiring + M-F2 posture for the
// owner-scoped original-file read on the STAGING key
// (GET /api/v1/resumes/parsed/{parsedId}/original). A captured ResumeFile is seeded through the
// REAL PR-9a seal write-path (POST /api/v1/resumes/import), so these tests prove the import → seal
// → decrypt-on-download round-trip end-to-end against real Postgres + the production
// field-encryption interceptors. Mirrors the sibling resume-endpoint tests exactly:
// ResumeRenderEndpointTests (Results.File byte-body + IDOR), GetResumeAtsTextEndpointTests
// (no-store header pins), GetParsedResumeEndpointTests (import + cross-user 404), and
// SessionStoreUnavailableTests (the capturing-logger derived host for the failed-access assertion).
//
// The CANONICAL key (GET /api/v1/resumes/{id}/original) is covered by
// DownloadResumeOriginalEndpointTests, which additionally has to promote the parse to a Resume.
[Collection("Api")]
public class DownloadResumeFileEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    // A non-trivial PDF body: the "%PDF-1.7" magic prefix (CvFileSignature resolves Pdf) followed by
    // high, non-digit bytes so the import personnummer body-scan is clean → the original IS captured
    // (PR-9a captures only body-scan-clean uploads). Byte-equality on this buffer is a meaningful
    // round-trip assertion (pin 1).
    private static readonly byte[] OriginalPdfBytes = BuildPdf();

    private static byte[] BuildPdf()
    {
        var bytes = new byte[48];
        // "%PDF-1.7"
        bytes[0] = 0x25; bytes[1] = 0x50; bytes[2] = 0x44; bytes[3] = 0x46;
        bytes[4] = 0x2D; bytes[5] = 0x31; bytes[6] = 0x2E; bytes[7] = 0x37;
        // High bytes (>= 0x80) — never ASCII digits, so no personnummer can form in the body.
        for (var i = 8; i < bytes.Length; i++)
            bytes[i] = (byte)(0x80 + (i * 7 % 0x40));

        return bytes;
    }

    // The staging arm is keyed on the ParsedResumeId the import response already returns, so these
    // tests no longer resolve a ResumeFileId out of the DbContext at all — the id under test is the
    // one the surface actually holds. The Guid overload serves the unknown-id and unauthenticated
    // probes, which have no import behind them.
    private static string DownloadUrl(string parsedResumeId) =>
        $"/api/v1/resumes/parsed/{parsedResumeId}/original";

    private static string DownloadUrl(Guid parsedResumeId) => DownloadUrl(parsedResumeId.ToString());

    private static async Task<HttpClient> NewAuthedClientAsync(
        WebApplicationFactory<Program> f, CancellationToken ct)
    {
        var client = f.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            client, email: $"download-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email: $"download-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private static MultipartFormDataContent FileForm(byte[] bytes, string fileName, string declaredContentType)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(declaredContentType);
        return new MultipartFormDataContent { { part, "file", fileName } };
    }

    private static async Task<string> ImportAsync(
        HttpClient client, byte[] bytes, string fileName, string declaredContentType, CancellationToken ct)
    {
        using var form = FileForm(bytes, fileName, declaredContentType);
        var import = await client.PostAsync("/api/v1/resumes/import", form, ct);
        import.IsSuccessStatusCode.ShouldBeTrue();
        return (await import.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("parsedResumeId").GetString()!;
    }

    private static void ShouldCarryNoStore(HttpResponseMessage response)
    {
        response.Headers.CacheControl.ShouldNotBeNull();
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        response.Headers.CacheControl.Private.ShouldBeTrue();
    }

    private static void ShouldCarryNoSniff(HttpResponseMessage response)
    {
        response.Headers.Contains("X-Content-Type-Options").ShouldBeTrue();
        response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
    }

    // 1 -----------------------------------------------------------------
    [Fact]
    public async Task Download_original_returns_200_and_original_bytes_for_owner()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var parsedId = await ImportAsync(_client, OriginalPdfBytes, "cv.pdf", "application/pdf", ct);

        var response = await _client.GetAsync(DownloadUrl(parsedId), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsByteArrayAsync(ct);
        // The decrypted download is byte-identical to the uploaded original (seal → open round-trip).
        body.ShouldBe(OriginalPdfBytes);
    }

    // 2 -----------------------------------------------------------------
    [Fact]
    public async Task Download_original_belonging_to_other_user_returns_404_and_owner_still_gets_200()
    {
        var ct = TestContext.Current.CancellationToken;

        var clientA = await NewAuthedClientAsync(_factory, ct);
        var parsedId = await ImportAsync(clientA, OriginalPdfBytes, "cv.pdf", "application/pdf", ct);

        // User B cannot read A's original (fail-closed IDOR, no enumeration oracle).
        var clientB = await NewAuthedClientAsync(_factory, ct);
        var getB = await clientB.GetAsync(DownloadUrl(parsedId), ct);
        getB.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A still reads their own on the same id — B's attempt had no side effect on the row.
        var getA = await clientA.GetAsync(DownloadUrl(parsedId), ct);
        getA.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // 3 -----------------------------------------------------------------
    [Fact]
    public async Task Download_original_cross_user_attempt_logs_failed_access_event_but_unknown_id_does_not()
    {
        var ct = TestContext.Current.CancellationToken;

        // User A owns a real captured original (imported through the real seal write-path).
        var clientA = await NewAuthedClientAsync(_factory, ct);
        var parsedId = await ImportAsync(clientA, OriginalPdfBytes, "cv.pdf", "application/pdf", ct);

        // User B runs on a derived host carrying an in-memory ILoggerProvider so the REAL
        // FailedAccessLogger output is captured (mirrors SessionStoreUnavailableTests' capturing host).
        await using var capturing = new CapturingLogApiFactory(_factory);
        var clientB = await NewAuthedClientAsync(capturing, ct);

        // An unknown id first: a plain 404 that must NOT log (no enumeration oracle).
        var unknownId = Guid.NewGuid();
        var unknown = await clientB.GetAsync(DownloadUrl(unknownId), ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A's real file id: a cross-user 404 that MUST log the failed-access attempt.
        var crossUser = await clientB.GetAsync(DownloadUrl(parsedId), ct);
        crossUser.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Exactly one failed-access event (EventId 4001) — the cross-user attempt, never the unknown id.
        var events = capturing.LogProvider.Logs.Where(l => l.EventId.Id == 4001).ToList();
        events.Count.ShouldBe(1);
        var record = events[0];
        record.Level.ShouldBe(LogLevel.Warning);
        record.Message.ShouldContain("event_name=failed_access_attempt");
        record.Message.ShouldContain("aggregate_type=ParsedResume");
        record.Message.ShouldContain("operation=DownloadParsedResumeOriginal");
        // The logged id is the one the CALLER supplied. That is the whole point of the probe: an
        // attacker can only vary the id in the URL, so an oracle guard aimed at any other key would
        // be guarding an id no request carries.
        record.Message.ShouldContain($"requested_aggregate_id={parsedId}");
        // The unknown-id probe left no trace — the two 404s are indistinguishable to the client but
        // only the cross-user hit (row exists for someone else) is logged.
        record.Message.ShouldNotContain(unknownId.ToString());
    }

    // 4 -----------------------------------------------------------------
    [Fact]
    public async Task Download_original_sets_no_store_on_200_and_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var parsedId = await ImportAsync(_client, OriginalPdfBytes, "cv.pdf", "application/pdf", ct);

        var ok = await _client.GetAsync(DownloadUrl(parsedId), ct);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldCarryNoStore(ok);

        var notFound = await _client.GetAsync(DownloadUrl(Guid.NewGuid()), ct);
        notFound.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        ShouldCarryNoStore(notFound);
    }

    // 5 -----------------------------------------------------------------
    [Fact]
    public async Task Download_original_sets_nosniff_on_200_and_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var parsedId = await ImportAsync(_client, OriginalPdfBytes, "cv.pdf", "application/pdf", ct);

        var ok = await _client.GetAsync(DownloadUrl(parsedId), ct);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldCarryNoSniff(ok);

        var notFound = await _client.GetAsync(DownloadUrl(Guid.NewGuid()), ct);
        notFound.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        ShouldCarryNoSniff(notFound);
    }

    // 6 -----------------------------------------------------------------
    [Fact]
    public async Task Download_original_content_type_is_server_derived_pdf_not_client_controlled()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        // Upload a PDF but DECLARE "application/octet-stream" — the import resolves the format from
        // the magic bytes and stores the canonical "application/pdf" (never the client's declared MIME).
        var parsedId = await ImportAsync(
            _client, OriginalPdfBytes, "cv.pdf", "application/octet-stream", ct);

        // The download request tries to influence the content-type via a hostile Accept + a bogus
        // query param — neither is honoured; the server-derived content-type stands.
        var request = new HttpRequestMessage(HttpMethod.Get, DownloadUrl(parsedId) + "?contentType=text%2Fhtml");
        request.Headers.Accept.Clear();
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/html");
        var response = await _client.SendAsync(request, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType.ShouldNotBeNull();
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
    }

    // 7 -----------------------------------------------------------------
    [Fact]
    public async Task Download_original_content_disposition_is_attachment_with_rfc5987_filename()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var parsedId = await ImportAsync(_client, OriginalPdfBytes, "cv.pdf", "application/pdf", ct);

        var response = await _client.GetAsync(DownloadUrl(parsedId), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var disposition = response.Content.Headers.ContentDisposition;
        disposition.ShouldNotBeNull();
        // Forced download (never inline) with both the quoted fallback and the RFC 5987 (filename*) form.
        disposition!.DispositionType.ShouldBe("attachment");
        disposition.FileName.ShouldNotBeNull();
        disposition.FileNameStar.ShouldNotBeNull();
    }

    // 8 -----------------------------------------------------------------
    [Fact]
    public async Task Download_original_filename_is_personnummer_redacted_in_header()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        // The filename embeds a REAL personnummer (811218-9876: valid date + Luhn). The body is
        // clean, so the original IS captured; the filename is masked at rest (M-F1) and re-masked
        // in the handler belt-and-braces, so the Content-Disposition can never carry the raw digits.
        var parsedId = await ImportAsync(
            _client, OriginalPdfBytes, "CV_811218-9876.pdf", "application/pdf", ct);

        var response = await _client.GetAsync(DownloadUrl(parsedId), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var disposition = response.Content.Headers.ContentDisposition;
        disposition.ShouldNotBeNull();
        var rawHeader = disposition!.ToString();
        // Raw personnummer digits never appear — not in the quoted form nor the percent-encoded form.
        rawHeader.ShouldNotContain("811218");
        rawHeader.ShouldNotContain("9876");
        // The masked form (every digit → '*', separator kept) is what the header carries instead.
        disposition.FileName.ShouldNotBeNull();
        disposition.FileName!.ShouldContain("******-****");
    }

    // 9 -----------------------------------------------------------------
    [Fact]
    public async Task Download_original_unauthenticated_returns_401_with_no_store_and_nosniff()
    {
        var ct = TestContext.Current.CancellationToken;
        // No session cookie/bearer. The path-scoped header middleware runs BEFORE authentication
        // (via Response.OnStarting), so the 401 challenge still carries the M-F2 headers.
        var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync(DownloadUrl(Guid.NewGuid()), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ShouldCarryNoStore(response);
        ShouldCarryNoSniff(response);
    }

    // 10 ----------------------------------------------------------------
    [Fact]
    public async Task Download_original_head_returns_405_with_no_store_and_nosniff()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // HEAD against the GET-only route → 405. The header middleware still stamps the M-F2 headers
        // (OnStarting fires even on the framework-generated method-not-allowed response).
        var request = new HttpRequestMessage(HttpMethod.Head, DownloadUrl(Guid.NewGuid()));
        var response = await _client.SendAsync(request, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        ShouldCarryNoStore(response);
        ShouldCarryNoSniff(response);
    }

    // 11 (bonus — the CryptographicException → 500 R-F6 arm) -------------
    [Fact]
    public async Task Download_original_decrypt_failure_returns_500_with_no_exception_detail()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var parsedId = await ImportAsync(_client, OriginalPdfBytes, "cv.pdf", "application/pdf", ct);

        // Tamper the stored ciphertext at rest: flip the last byte (the AES-GCM tag) so the opener's
        // Decrypt fails the tag check and throws CryptographicException. Reachable cleanly via SQL
        // against the shared Testcontainers DB — no test-only production seam.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE resume_files SET content = set_byte(content, length(content) - 1, "
                + "get_byte(content, length(content) - 1) # 255) WHERE parsed_resume_id = {0}",
                [Guid.Parse(parsedId)],
                ct);
        }

        var response = await _client.GetAsync(DownloadUrl(parsedId), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        var raw = await response.Content.ReadAsStringAsync(ct);
        var json = JsonDocument.Parse(raw);
        json.RootElement.GetProperty("error").GetString().ShouldBe("Ett internt fel uppstod.");
        // Zero exception detail leaks to the client body — no type name, DEK/crypto context or stack.
        raw.ShouldNotContain("Cryptographic");
        raw.ShouldNotContain("DEK");
        raw.ShouldNotContain("Form C");
        raw.ShouldNotContain("BinaryFieldOpener");
    }

    /// <summary>
    /// A derived host that reuses this factory's Testcontainers + service wiring (via the reflected
    /// parent <c>ConfigureWebHost</c>) and adds an in-memory <see cref="CapturingLoggerProvider"/> so a
    /// test can assert the REAL <c>FailedAccessLogger</c> event end-to-end. Mirrors
    /// <c>SessionStoreUnavailableTests.BrokenSessionStoreFactory</c> exactly.
    /// </summary>
    private sealed class CapturingLogApiFactory(ApiFactory parent) : WebApplicationFactory<Program>
    {
        public CapturingLoggerProvider LogProvider { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            parent.GetType()
                .GetMethod("ConfigureWebHost", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(parent, [builder]);

            builder.ConfigureServices(services =>
                services.AddSingleton<ILoggerProvider>(LogProvider));
        }
    }
}
