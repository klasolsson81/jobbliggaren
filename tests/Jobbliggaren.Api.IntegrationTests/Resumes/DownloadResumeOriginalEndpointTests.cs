using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// The CANONICAL key for the original-file read path (GET /api/v1/resumes/{id}/original), the arm
// the CV hub's cards hold an id for. Sibling of DownloadResumeFileEndpointTests, which covers the
// STAGING key; the M-F2 header posture is shared and pinned there, so this file pins what is
// genuinely different about resolving through a Resume:
//
//   - the Resume.SourceParsedResumeId → ResumeFile.ParsedResumeId hop (ADR 0100 §D5) actually
//     returns the same bytes the import sealed,
//   - a resume with NO source parse is ordinary absence (404),
//   - a soft-deleted resume stops serving its original.
//
// The enumeration-probe contract for this key is asserted in DownloadResumeFileEndpointTests,
// which proves BOTH keys in ONE capturing host: every derived WebApplicationFactory builds its own
// internal EF service provider and the suite sits one host under EF's ceiling, so a second capture
// here turned the whole suite red in CI while filtered local runs stayed green. The ceiling itself
// is explained at ApiFactory.cs's ConfigureWarnings call.
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

    private static Task<string> ImportAsync(HttpClient client, CancellationToken ct) =>
        ImportNamedAsync(client, "cv.pdf", ct);

    private static async Task<string> ImportNamedAsync(
        HttpClient client, string fileName, CancellationToken ct)
    {
        var part = new ByteArrayContent(OriginalPdfBytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var form = new MultipartFormDataContent { { part, "file", fileName } };
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
        // client-declared value. The web client reads this header to pick the download's file
        // extension; it never decides from the stored filename.
        response.Content.Headers.ContentType.ShouldNotBeNull();
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
    }

    // 2b ----------------------------------------------------------------
    [Fact]
    public async Task Download_original_content_disposition_is_attachment_with_rfc5987_filename()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await ImportAndPromoteAsync(_client, ct);

        var response = await _client.GetAsync(DownloadUrl(resumeId), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var disposition = response.Content.Headers.ContentDisposition;
        disposition.ShouldNotBeNull();
        // Forced download (never inline) with both the quoted fallback and the RFC 5987 (filename*)
        // form. DPIA #659 M-F2 requires "attachment" verbatim and R-F6's residual rests on a stored
        // polyglot never being rendered inline from our origin — so this is pinned PER ARM, not
        // inherited from the staging sibling: the header is produced by each endpoint delegate
        // separately, and a shared middleware pin would not have caught one of them regressing.
        disposition!.DispositionType.ShouldBe("attachment");
        disposition.FileName.ShouldNotBeNull();
        disposition.FileNameStar.ShouldNotBeNull();
    }

    // 2c ----------------------------------------------------------------
    [Fact]
    public async Task Download_original_filename_is_personnummer_redacted_in_header()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        // The filename embeds a REAL personnummer (811218-9876: valid date + Luhn). The body is
        // clean, so the original IS captured; the filename is masked at rest (M-F1) and re-masked
        // in ResumeOriginalReader belt-and-braces, so the Content-Disposition can never carry the
        // raw digits on this arm either.
        var parsedId = await ImportNamedAsync(_client, "CV_811218-9876.pdf", ct);
        var promote = await _client.PostAsJsonAsync(
            $"/api/v1/resumes/parsed/{parsedId}/promote", PromoteBody(), ct);
        promote.StatusCode.ShouldBe(HttpStatusCode.Created);
        var resumeId = (await promote.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("id").GetString()!;

        var response = await _client.GetAsync(DownloadUrl(resumeId), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var disposition = response.Content.Headers.ContentDisposition;
        disposition.ShouldNotBeNull();
        var rawHeader = disposition!.ToString();
        rawHeader.ShouldNotContain("811218");
        rawHeader.ShouldNotContain("9876");
        disposition.FileName.ShouldNotBeNull();
        disposition.FileName!.ShouldContain("******-****");
    }

    // 3 -----------------------------------------------------------------
    [Fact]
    public async Task Download_original_for_a_resume_with_no_source_parse_returns_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        // Created directly: a real resume, owned by the caller, that never had an original.
        var resumeId = await CreateSourcelessResumeAsync(_client, ct);

        var response = await _client.GetAsync(DownloadUrl(resumeId), ct);

        // Ordinary absence, which the surface renders as an honest empty state. That this absence
        // emits NO failed-access event is asserted in DownloadResumeFileEndpointTests' single
        // capturing test, which covers both keys in one capture — the note there records why the
        // capture is not duplicated per arm (EF Core's service-provider cap).
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
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

    // 7b ----------------------------------------------------------------
    [Fact]
    public async Task Download_original_with_trailing_slash_still_carries_m_f2_headers()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await ImportAndPromoteAsync(_client, ct);

        // Endpoint routing trims a trailing slash, so this request still REACHES the endpoint and
        // still serves decrypted bytes. The header middleware matches on the path string, so it has
        // to trim too — without `TrimEnd('/')` the bytes would go out with neither `no-store` nor
        // `nosniff`. The 200 assertion is the positive control: on a 404 this test would pass
        // vacuously and measure nothing.
        var response = await _client.GetAsync($"{DownloadUrl(resumeId)}/", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldCarryNoStore(response);
        ShouldCarryNoSniff(response);
    }

    // 7c ----------------------------------------------------------------
    [Fact]
    public async Task Download_original_with_uppercase_segment_still_carries_m_f2_headers()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var resumeId = await ImportAndPromoteAsync(_client, ct);

        // Routing is case-insensitive, so this also reaches the endpoint. An `Ordinal` comparison in
        // the middleware would miss it and drop both M-F2 headers off a live PII response. Same
        // positive control: the 200 is asserted first.
        var response = await _client.GetAsync($"/api/v1/resumes/{resumeId}/ORIGINAL", ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldCarryNoStore(response);
        ShouldCarryNoSniff(response);
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
}
