using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// The CANONICAL key for the original-file read path (GET /api/v1/resumes/{id}/original), the arm
// the CV hub's cards hold an id for. Sibling of DownloadResumeFileEndpointTests, which covers the
// STAGING key; the M-F2 header posture is shared and pinned there, so this file pins what is
// genuinely different about resolving through a Resume:
//
//   - the Resume.SourceParsedResumeId → ResumeFile.ParsedResumeId hop (ADR 0100 §D5) actually
//     returns the same bytes the import sealed,
//   - a resume with NO source parse is ordinary absence (404, and NOT a failed-access event),
//   - the enumeration probe fires on the id the caller supplied (the ResumeId), not on some
//     internal key no request carries,
//   - a soft-deleted resume stops serving its original.
//
// Everything is seeded through REAL production entry points: POST /import (the PR-9a seal
// write-path), POST /parsed/{id}/promote (which persists the provenance link), and POST /resumes
// (which is how a source-less resume comes to exist at all) — so no assertion here rests on a
// state production cannot produce.
[Collection("Api")]
public class DownloadResumeOriginalEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    // A 48-byte PDF: "%PDF-1.7" magic (CvFileSignature resolves Pdf) + high, non-digit bytes so the
    // import personnummer body-scan is clean → the original IS captured (PR-9a captures only clean
    // uploads). Identical construction to DownloadResumeFileEndpointTests.
    private static readonly byte[] OriginalPdfBytes = BuildPdf();

    private static byte[] BuildPdf()
    {
        var bytes = new byte[48];
        bytes[0] = 0x25; bytes[1] = 0x50; bytes[2] = 0x44; bytes[3] = 0x46;
        bytes[4] = 0x2D; bytes[5] = 0x31; bytes[6] = 0x2E; bytes[7] = 0x37;
        for (var i = 8; i < bytes.Length; i++)
            bytes[i] = (byte)(0x80 + (i * 7 % 0x40));

        return bytes;
    }

    private static string DownloadUrl(string resumeId) => $"/api/v1/resumes/{resumeId}/original";

    private static string DownloadUrl(Guid resumeId) => DownloadUrl(resumeId.ToString());

    private static async Task<HttpClient> NewAuthedClientAsync(
        WebApplicationFactory<Program> f, CancellationToken ct)
    {
        var client = f.CreateClient();
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            client, email: $"original-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
        return client;
    }

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email: $"original-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private static async Task<string> ImportAsync(HttpClient client, CancellationToken ct)
    {
        var part = new ByteArrayContent(OriginalPdfBytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var form = new MultipartFormDataContent { { part, "file", "cv.pdf" } };
        var import = await client.PostAsync("/api/v1/resumes/import", form, ct);
        import.IsSuccessStatusCode.ShouldBeTrue();
        return (await import.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("parsedResumeId").GetString()!;
    }

    private static object PromoteBody(string name = "Importerat CV") =>
        new
        {
            name,
            content = new
            {
                personalInfo = new { fullName = "Anna Andersson", email = "anna@example.se", phone = (string?)null, location = "Stockholm" },
                experiences = Array.Empty<object>(),
                educations = Array.Empty<object>(),
                skills = Array.Empty<object>(),
                summary = (string?)null,
            },
        };

    /// <summary>Import → promote, returning the canonical ResumeId that carries a stored original.</summary>
    private static async Task<string> ImportAndPromoteAsync(HttpClient client, CancellationToken ct)
    {
        var parsedId = await ImportAsync(client, ct);
        var promote = await client.PostAsJsonAsync(
            $"/api/v1/resumes/parsed/{parsedId}/promote", PromoteBody(), ct);
        promote.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await promote.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;
    }

    /// <summary>
    /// A resume created directly, with no import behind it — the production path by which a resume
    /// legitimately has no SourceParsedResumeId and therefore no original.
    /// </summary>
    private static async Task<string> CreateSourcelessResumeAsync(HttpClient client, CancellationToken ct)
    {
        var created = await client.PostAsJsonAsync(
            "/api/v1/resumes", new { name = "Skapat CV", fullName = "Anna Andersson" }, ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;
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
    public async Task Download_original_by_resume_id_returns_200_and_the_bytes_the_import_sealed()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await ImportAndPromoteAsync(_client, ct);

        var response = await _client.GetAsync(DownloadUrl(resumeId), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsByteArrayAsync(ct);
        // Byte-identical to the upload: the SourceParsedResumeId hop reaches the same sealed row the
        // staging key reaches, and the seal → open round-trip is lossless.
        body.ShouldBe(OriginalPdfBytes);
    }

    // 2 -----------------------------------------------------------------
    [Fact]
    public async Task Download_original_content_type_is_the_server_derived_pdf()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await ImportAndPromoteAsync(_client, ct);

        var response = await _client.GetAsync(DownloadUrl(resumeId), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        // The canonical MIME resolved from the file signature at import (M-F2), never a
        // client-declared value. The web client branches on exactly this header to decide whether
        // the original can be shown inline or must be offered as a download.
        response.Content.Headers.ContentType.ShouldNotBeNull();
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
    }

    // 3 -----------------------------------------------------------------
    [Fact]
    public async Task Download_original_for_a_resume_with_no_source_parse_is_404_and_not_an_access_event()
    {
        var ct = TestContext.Current.CancellationToken;

        await using var capturing = new CapturingLogApiFactory(_factory);
        var client = await NewAuthedClientAsync(capturing, ct);
        // Created directly: a real resume, owned by the caller, that never had an original.
        var resumeId = await CreateSourcelessResumeAsync(client, ct);

        var response = await client.GetAsync(DownloadUrl(resumeId), ct);

        // Ordinary absence, which the surface renders as an honest empty state.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // And NOT a failed-access attempt: the caller owns the resume, so logging one here would
        // fill the ops channel with every template-built CV the user opens.
        capturing.LogProvider.Logs.Where(l => l.EventId.Id == 4001).ShouldBeEmpty();
    }

    // 4 -----------------------------------------------------------------
    [Fact]
    public async Task Download_original_belonging_to_other_user_returns_404_and_owner_still_gets_200()
    {
        var ct = TestContext.Current.CancellationToken;

        var clientA = await NewAuthedClientAsync(_factory, ct);
        var resumeId = await ImportAndPromoteAsync(clientA, ct);

        var clientB = await NewAuthedClientAsync(_factory, ct);
        (await clientB.GetAsync(DownloadUrl(resumeId), ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A still reads their own on the same id — B's attempt had no side effect.
        (await clientA.GetAsync(DownloadUrl(resumeId), ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // 5 -----------------------------------------------------------------
    [Fact]
    public async Task Download_original_cross_user_attempt_logs_the_requested_resume_id_but_unknown_id_does_not()
    {
        var ct = TestContext.Current.CancellationToken;

        var clientA = await NewAuthedClientAsync(_factory, ct);
        var resumeId = await ImportAndPromoteAsync(clientA, ct);

        await using var capturing = new CapturingLogApiFactory(_factory);
        var clientB = await NewAuthedClientAsync(capturing, ct);

        // An unknown id first: a plain 404 that must NOT log (no enumeration oracle).
        var unknownId = Guid.NewGuid();
        (await clientB.GetAsync(DownloadUrl(unknownId), ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A's real resume id: a cross-user 404 that MUST log.
        (await clientB.GetAsync(DownloadUrl(resumeId), ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var events = capturing.LogProvider.Logs.Where(l => l.EventId.Id == 4001).ToList();
        events.Count.ShouldBe(1);
        var record = events[0];
        record.Level.ShouldBe(LogLevel.Warning);
        record.Message.ShouldContain("event_name=failed_access_attempt");
        record.Message.ShouldContain("aggregate_type=Resume");
        record.Message.ShouldContain("operation=DownloadResumeOriginal");
        // The probe is aimed at the id the CALLER supplied — the only id an attacker can vary.
        record.Message.ShouldContain($"requested_aggregate_id={resumeId}");
        record.Message.ShouldNotContain(unknownId.ToString());
    }

    // 6 -----------------------------------------------------------------
    [Fact]
    public async Task Download_original_after_the_resume_is_deleted_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await ImportAndPromoteAsync(_client, ct);

        (await _client.GetAsync(DownloadUrl(resumeId), ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await _client.DeleteAsync($"/api/v1/resumes/{resumeId}", ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The resume's global DeletedAt filter excludes the row, so the hop never starts. (The
        // coupled file is also hard-deleted by the cascade — that half is pinned by
        // DeleteResumeCascadesOriginalFileTests against the staging key.)
        (await _client.GetAsync(DownloadUrl(resumeId), ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // 7 -----------------------------------------------------------------
    [Fact]
    public async Task Download_original_carries_m_f2_headers_on_200_and_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await ImportAndPromoteAsync(_client, ct);

        // The header middleware is path-scoped, and this route's path shape differs from the staging
        // sibling's — so the pin is repeated here rather than inherited.
        var ok = await _client.GetAsync(DownloadUrl(resumeId), ct);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldCarryNoStore(ok);
        ShouldCarryNoSniff(ok);

        var notFound = await _client.GetAsync(DownloadUrl(Guid.NewGuid()), ct);
        notFound.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        ShouldCarryNoStore(notFound);
        ShouldCarryNoSniff(notFound);
    }

    // 8 -----------------------------------------------------------------
    [Fact]
    public async Task Download_original_unauthenticated_returns_401_with_m_f2_headers()
    {
        var ct = TestContext.Current.CancellationToken;
        // The path-scoped header middleware runs BEFORE authentication (via Response.OnStarting),
        // so the 401 challenge still carries the M-F2 headers on this path shape too.
        var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync(DownloadUrl(Guid.NewGuid()), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ShouldCarryNoStore(response);
        ShouldCarryNoSniff(response);
    }

    /// <summary>
    /// A derived host that reuses this factory's Testcontainers + service wiring (via the reflected
    /// parent <c>ConfigureWebHost</c>) and adds an in-memory <see cref="CapturingLoggerProvider"/> so a
    /// test can assert the REAL <c>FailedAccessLogger</c> event end-to-end. Mirrors
    /// <c>DownloadResumeFileEndpointTests.CapturingLogApiFactory</c> exactly.
    /// </summary>
    private sealed class CapturingLogApiFactory(ApiFactory parent) : WebApplicationFactory<Program>
    {
        public CapturingLoggerProvider LogProvider { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            typeof(ApiFactory)
                .GetMethod("ConfigureWebHost", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(parent, [builder]);

            builder.ConfigureLogging(logging => logging.AddProvider(LogProvider));
        }
    }
}
