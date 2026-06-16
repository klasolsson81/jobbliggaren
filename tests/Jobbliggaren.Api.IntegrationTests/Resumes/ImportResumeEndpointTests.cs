using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// Fas 4 STEG B / B1a — HTTP wiring for the F4-8 import/parse port. The deep parse +
// DEK-encryption path is already proven by the Worker integration tests
// (ParsedResumeEncryptionTests); these assertions cover the ENDPOINT surface: the auth
// gate, multipart parsing, the size validation under the per-request body cap, the
// magic-byte format gate, and the Result→HTTP mapping. A freshly-registered user has a
// JobSeeker (RegisterCommandHandler) and gets a DEK provisioned on first use
// (FieldEncryptionKeyPrefetchBehavior.GetOrCreateDataKeyAsync), so the happy path runs
// end-to-end against real Postgres.
[Collection("Api")]
public class ImportResumeEndpointTests(ApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    // "%PDF-1.7" — a real PDF magic prefix so CvFileSignature resolves Pdf. The 8-byte
    // stub has no usable text layer, so the fail-soft extractor yields a degraded parse;
    // the artifact still persists (first-class degraded parse, OQ5) → 201.
    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email: $"import-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private static MultipartFormDataContent FileForm(byte[] bytes, string fileName, string contentType)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { part, "file", fileName } };
    }

    [Fact]
    public async Task POST_import_without_auth_returns_401()
    {
        var ct = TestContext.Current.CancellationToken;
        using var form = FileForm(PdfBytes, "cv.pdf", "application/pdf");
        var response = await _client.PostAsync("/api/v1/resumes/import", form, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task POST_import_without_file_part_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        // A valid multipart form that simply omits the "file" part.
        using var form = new MultipartFormDataContent { { new StringContent("x"), "note" } };
        var response = await _client.PostAsync("/api/v1/resumes/import", form, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_import_malformed_multipart_returns_400_not_500()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        // multipart content-type but a body that contains no valid boundary parts.
        using var body = new StringContent("not-a-real-multipart-body");
        body.Headers.Remove("Content-Type");
        body.Headers.TryAddWithoutValidation("Content-Type", "multipart/form-data; boundary=zzz");
        var response = await _client.PostAsync("/api/v1/resumes/import", body, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_import_valid_pdf_returns_201_with_parsedResumeId_and_no_cv_content()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        using var form = FileForm(PdfBytes, "cv.pdf", "application/pdf");

        var response = await _client.PostAsync("/api/v1/resumes/import", form, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var id = json.GetProperty("parsedResumeId").GetString();
        id.ShouldNotBeNullOrEmpty();
        Guid.Parse(id!).ShouldNotBe(Guid.Empty);
        // The import response carries the parse summary, never CV-PII content.
        json.TryGetProperty("confidence", out _).ShouldBeTrue();
        json.TryGetProperty("rawText", out _).ShouldBeFalse();
        json.TryGetProperty("content", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task POST_import_unsupported_format_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        using var form = FileForm([0xDE, 0xAD, 0xBE, 0xEF], "cv.bin", "application/octet-stream");
        var response = await _client.PostAsync("/api/v1/resumes/import", form, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task POST_import_file_over_10mb_returns_400()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // One byte over the handler's 10 MiB FluentValidation floor, under the endpoint's
        // 11 MiB Kestrel body cap → the friendly size-validation 400 (not a hard 413).
        var oversize = new byte[10 * 1024 * 1024 + 1];
        oversize[0] = 0x25; // '%' — irrelevant; size validation fires before the format gate.
        using var form = FileForm(oversize, "big.pdf", "application/pdf");

        var response = await _client.PostAsync("/api/v1/resumes/import", form, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
