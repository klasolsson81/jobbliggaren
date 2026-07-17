using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// Fas 4 STEG B / B1a + CV-pivot 5c — HTTP wiring for the F4-8 import port, now the in-place
// "spara direkt" flow (R4): /import runs import THEN always auto-promotes (two sends, two
// UnitOfWork — 5a CTO-bind §3) and returns the composed outcome. These assertions cover the
// ENDPOINT surface: the auth gate, multipart parsing (incl. the new personnummerAcknowledged +
// name form fields), the size/format gates, and the outcome→HTTP mapping. The Promoted (201)
// path needs a Confident parse the real extractor cannot yield from a stub, so it is proven at
// the unit level (AutoPromoteParsedResumeCommandHandlerTests.Handle_CleanConfidentParse_*); here
// a degraded stub deterministically LeftPending(ParseNotConfident) → 200. The consent re-POST
// wiring IS proven end-to-end below with a real personnummer-bearing DOCX: the finding surfaces
// (dialog trigger) and the acknowledge flag threads through to the file capture.
[Collection("Api")]
public class ImportResumeEndpointTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    // "%PDF-1.7" — a real PDF magic prefix so CvFileSignature resolves Pdf. The 8-byte
    // stub has no usable text layer, so the fail-soft extractor yields a degraded parse;
    // the artifact still persists (first-class degraded parse, OQ5) and, being not-Confident,
    // auto-promote leaves it PendingReview → 200 LeftPending(ParseNotConfident).
    private static readonly byte[] PdfBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x37];

    private const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    // A scanner-valid personnummer (parity AutoPromoteParsedResumeCommandHandlerTests) placed in
    // real DOCX body text so the authoritative server-side scan finds it — the only way to
    // exercise the flagged-capture path black-box (a stub has no text to scan).
    private const string ValidPersonnummer = "811218-9876";

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email: $"import-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private static MultipartFormDataContent FileForm(byte[] bytes, string fileName, string contentType)
        => FileForm(bytes, fileName, contentType, acknowledged: null, name: null);

    private static MultipartFormDataContent FileForm(
        byte[] bytes, string fileName, string contentType, bool? acknowledged, string? name)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var form = new MultipartFormDataContent { { part, "file", fileName } };
        if (acknowledged is not null)
            form.Add(new StringContent(acknowledged.Value ? "true" : "false"), "personnummerAcknowledged");
        if (name is not null)
            form.Add(new StringContent(name), "name");
        return form;
    }

    // A minimal, valid in-memory DOCX (OpenXml) — identical construction to
    // PdfPigOpenXmlCvTextExtractorTests.BuildDocx, so the real extractor yields the paragraphs
    // as raw text the personnummer scanner then runs over.
    private static byte[] BuildDocx(params string[] paragraphs)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(
            stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            var body = new Body();
            foreach (var text in paragraphs)
                body.AppendChild(new Paragraph(new Run(new Text(text))));
            mainPart.Document = new Document(body);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    // resume_files is write-once with no soft-delete filter, keyed by ParsedResumeId — a captured
    // original is directly visible. Projects only the id (never the sealed bytea → no DEK needed).
    private async Task<bool> OriginalFileCapturedAsync(string parsedResumeId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var parsedId = new ParsedResumeId(Guid.Parse(parsedResumeId));
        return await db.ResumeFiles.AsNoTracking().AnyAsync(f => f.ParsedResumeId == parsedId, ct);
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
    public async Task POST_import_degraded_pdf_returns_200_LeftPending_ParseNotConfident_no_cv_content()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        using var form = FileForm(PdfBytes, "cv.pdf", "application/pdf");

        var response = await _client.PostAsync("/api/v1/resumes/import", form, ct);

        // A degraded (not-Confident) parse is not clean → auto-promote leaves it pending → 200.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        json.GetProperty("outcome").GetString().ShouldBe("LeftPending");
        json.GetProperty("blockReason").GetString().ShouldBe("ParseNotConfident");
        json.TryGetProperty("resumeId", out var resumeId).ShouldBeTrue();
        resumeId.ValueKind.ShouldBe(JsonValueKind.Null);

        var id = json.GetProperty("parsedResumeId").GetString();
        id.ShouldNotBeNullOrEmpty();
        Guid.Parse(id!).ShouldNotBe(Guid.Empty);

        // The composed outcome carries the PII-free finding, never CV-PII content.
        json.GetProperty("personnummer").GetProperty("found").GetBoolean().ShouldBeFalse();
        json.TryGetProperty("rawText", out _).ShouldBeFalse();
        json.TryGetProperty("content", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task POST_import_accepts_name_and_acknowledge_form_fields()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        // A clean (no-pnr) stub with both new fields present — the endpoint must read them without
        // error. The stub is degraded, so the acknowledge is inert and the name is never applied
        // (auto-promote LeftPending precedes name use); this asserts only the parse succeeds.
        using var form = FileForm(
            PdfBytes, "cv.pdf", "application/pdf", acknowledged: true, name: "Anna Andersson");

        var response = await _client.PostAsync("/api/v1/resumes/import", form, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        json.GetProperty("outcome").GetString().ShouldBe("LeftPending");
        json.GetProperty("parsedResumeId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task POST_import_pnr_in_docx_without_acknowledge_surfaces_finding_and_captures_no_file()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var docx = BuildDocx("Anna Andersson", $"Personnummer: {ValidPersonnummer}");
        using var form = FileForm(docx, "cv.docx", DocxContentType);

        var response = await _client.PostAsync("/api/v1/resumes/import", form, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        // The finding surfaces (the FE's consent-dialog trigger) — PII-free: a bool + count, no value.
        var personnummer = json.GetProperty("personnummer");
        personnummer.GetProperty("found").GetBoolean().ShouldBeTrue();
        personnummer.GetProperty("count").GetInt32().ShouldBeGreaterThanOrEqualTo(1);

        // Auto-promote stays pending on the personnummer (never lifts — 5b B3/B4).
        json.GetProperty("outcome").GetString().ShouldBe("LeftPending");
        json.GetProperty("blockReason").GetString().ShouldBe("PersonnummerPresent");

        // Fail-closed: no acknowledge → the flagged original is NOT captured (5b Gate D).
        var parsedId = json.GetProperty("parsedResumeId").GetString()!;
        (await OriginalFileCapturedAsync(parsedId, ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task POST_import_pnr_in_docx_with_acknowledge_captures_the_original_file()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var docx = BuildDocx("Anna Andersson", $"Personnummer: {ValidPersonnummer}");
        using var form = FileForm(docx, "cv.docx", DocxContentType, acknowledged: true, name: null);

        var response = await _client.PostAsync("/api/v1/resumes/import", form, ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);

        // Consent stores the FILE, never the content: the parse still stays pending on the
        // personnummer (5b B3 — original-file-only), so routing is unchanged.
        json.GetProperty("personnummer").GetProperty("found").GetBoolean().ShouldBeTrue();
        json.GetProperty("outcome").GetString().ShouldBe("LeftPending");
        json.GetProperty("blockReason").GetString().ShouldBe("PersonnummerPresent");

        // The acknowledge threaded through to the capture: the sealed original now exists. This is
        // the endpoint-wiring guarantee (the aggregate biconditional + seal are proven in 5b's
        // handler + Testcontainers tests; here we prove the form field reaches the command).
        var parsedId = json.GetProperty("parsedResumeId").GetString()!;
        (await OriginalFileCapturedAsync(parsedId, ct)).ShouldBeTrue();
    }

    [Fact]
    public async Task POST_import_response_never_echoes_the_personnummer_value()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);
        var docx = BuildDocx("Anna Andersson", $"Personnummer: {ValidPersonnummer}");
        using var form = FileForm(docx, "cv.docx", DocxContentType);

        var response = await _client.PostAsync("/api/v1/resumes/import", form, ct);
        var raw = await response.Content.ReadAsStringAsync(ct);

        // Invariant 1 / 5b B6-B7: the finding is surfaced as count + kinds + a bool, never the
        // value — neither the hyphenated nor the digit-only form may appear on the wire.
        raw.ShouldNotContain(ValidPersonnummer);
        raw.ShouldNotContain(ValidPersonnummer.Replace("-", ""));
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
