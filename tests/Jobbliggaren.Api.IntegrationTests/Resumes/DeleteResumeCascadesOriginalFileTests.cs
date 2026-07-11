using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Jobbliggaren.Api.IntegrationTests.Helpers;
using Jobbliggaren.Api.IntegrationTests.Infrastructure;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Files;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Jobbliggaren.Api.IntegrationTests.Resumes;

// Fas 4b PR-9c (ADR 0100 §D5 / ADR 0103, epik #649/#778) — the resume-lifecycle link end-to-end
// against REAL Postgres. Import (capture a clean original through the real PR-9a seal write-path)
// → promote (the parsed→resume provenance is PERSISTED on Resume.SourceParsedResumeId) → delete
// the CV → the coupled resume_files row is HARD-deleted in the same UnitOfWork, and a subsequent
// download 404s. This is the authoritative atomicity + DEK-free oracle: no owner DEK is warmed
// anywhere in the flow, yet the erasure succeeds — because DeleteResumeCommand does not carry
// IRequiresFieldEncryptionKey and the cascade projects only the file id (never the sealed bytea).
// Mirrors DownloadResumeFileEndpointTests (import + service-scope id resolution) and
// PromoteParsedResumeEndpointTests (import → promote HTTP flow) exactly.
[Collection("Api")]
public class DeleteResumeCascadesOriginalFileTests(ApiFactory factory)
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

    private static string DownloadUrl(Guid fileId) => $"/api/v1/resumes/files/{fileId}/original";

    private async Task AuthenticateAsync(CancellationToken ct)
    {
        var sessionId = await AuthTestHelpers.RegisterAndGetSessionIdAsync(
            _client, email: $"delete-cascade-{Guid.NewGuid():N}@jobbliggaren.test", ct: ct);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionId);
    }

    private static async Task<string> ImportAsync(HttpClient client, CancellationToken ct)
    {
        var part = new ByteArrayContent(OriginalPdfBytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var form = new MultipartFormDataContent { { part, "file", "cv.pdf" } };
        var import = await client.PostAsync("/api/v1/resumes/import", form, ct);
        import.StatusCode.ShouldBe(HttpStatusCode.Created);
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

    // The import response carries the ParsedResumeId; the ResumeFileId is the coupling row's key
    // (ResumeFiles keyed by ParsedResumeId; owner-scoping lives in the handler, not a query filter,
    // so this scope read is safe). Projects ONLY the id — no sealed bytea materialised.
    private async Task<Guid> ResolveResumeFileIdAsync(string parsedResumeId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var parsedId = new ParsedResumeId(Guid.Parse(parsedResumeId));
        var fileId = await db.ResumeFiles
            .AsNoTracking()
            .Where(f => f.ParsedResumeId == parsedId)
            .Select(f => f.Id)
            .FirstOrDefaultAsync(ct);

        fileId.ShouldNotBe(default); // the clean import must have captured an original
        return fileId.Value;
    }

    // Reads the promoted Resume's persisted provenance link (F1=L-B). Projects the plain
    // SourceParsedResumeId column ONLY — never Includes the encrypted Master content, so no DEK.
    private async Task<Guid?> ResolveSourceParsedResumeIdAsync(string resumeId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rid = new ResumeId(Guid.Parse(resumeId));
        var source = await db.Resumes
            .AsNoTracking()
            .Where(r => r.Id == rid)
            .Select(r => r.SourceParsedResumeId)
            .FirstOrDefaultAsync(ct);
        return source?.Value;
    }

    private async Task<bool> ResumeFileRowExistsAsync(Guid fileId, CancellationToken ct)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rfid = new ResumeFileId(fileId);
        // resume_files is write-once: no soft-delete filter, so a surviving row is directly visible.
        return await db.ResumeFiles.AsNoTracking().AnyAsync(f => f.Id == rfid, ct);
    }

    [Fact]
    public async Task Delete_resume_hard_deletes_the_coupled_original_and_download_then_404s()
    {
        var ct = TestContext.Current.CancellationToken;
        await AuthenticateAsync(ct);

        // Import a clean original (captured through the real PR-9a seal write-path).
        var parsedId = await ImportAsync(_client, ct);
        var fileId = await ResolveResumeFileIdAsync(parsedId, ct);

        // Promote → canonical Resume. The parsed→resume link is persisted as the cascade key.
        var promote = await _client.PostAsJsonAsync(
            $"/api/v1/resumes/parsed/{parsedId}/promote", PromoteBody(), ct);
        promote.StatusCode.ShouldBe(HttpStatusCode.Created);
        var resumeId = (await promote.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        // The promoted Resume carries SourceParsedResumeId == the parsed id (F1=L-B persisted).
        (await ResolveSourceParsedResumeIdAsync(resumeId, ct)).ShouldBe(Guid.Parse(parsedId));

        // Sanity: the original is downloadable before the delete.
        (await _client.GetAsync(DownloadUrl(fileId), ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Delete the CV — the cascade hard-deletes the coupled original in the SAME UnitOfWork.
        // No owner DEK is warmed anywhere above; the erasure still succeeds (DEK-free read path).
        var delete = await _client.DeleteAsync($"/api/v1/resumes/{resumeId}", ct);
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The resume_files row is gone (immediate hard-delete) ...
        (await ResumeFileRowExistsAsync(fileId, ct)).ShouldBeFalse();

        // ... and the download now 404s (the row no longer exists for anyone).
        (await _client.GetAsync(DownloadUrl(fileId), ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
