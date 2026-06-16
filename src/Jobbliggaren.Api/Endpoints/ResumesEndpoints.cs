using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.JobSeekers.Commands.SetPrimaryResume;
using Jobbliggaren.Application.Resumes.Commands.CreateResume;
using Jobbliggaren.Application.Resumes.Commands.DeleteResume;
using Jobbliggaren.Application.Resumes.Commands.DeleteResumeVersion;
using Jobbliggaren.Application.Resumes.Commands.ImportResume;
using Jobbliggaren.Application.Resumes.Commands.RenameResume;
using Jobbliggaren.Application.Resumes.Commands.SetResumeLanguage;
using Jobbliggaren.Application.Resumes.Commands.UpdateMasterContent;
using Jobbliggaren.Application.Resumes.Queries;
using Jobbliggaren.Application.Resumes.Queries.GetParsedResume;
using Jobbliggaren.Application.Resumes.Queries.GetResumeById;
using Jobbliggaren.Application.Resumes.Queries.GetResumes;
using Mediator;
using Microsoft.AspNetCore.Http.Features;

namespace Jobbliggaren.Api.Endpoints;

public static class ResumesEndpoints
{
    // Per-request upload body cap (F4-8 / STEG B), derived as exactly one notch above
    // the handler's FluentValidation floor (ImportResumeCommandValidator.MaxFileBytes,
    // 10 MiB) so the "+1 MiB" relation is a compiled invariant, not a comment. Layering:
    // a 10–11 MiB file passes the Kestrel body cap and reaches the friendly Swedish
    // FluentValidation 400; a file > 11 MiB is hard-rejected by Kestrel as a 413
    // (BadHttpRequestException — NOT the friendly 400 filter). The 413 path is Kestrel
    // host behaviour, verified separately (the in-memory TestServer harness cannot reach
    // it). Defense-in-depth, not the primary gate — the magic-byte format check lives in
    // the handler (CvFileSignature).
    private const long MaxUploadBytes = ImportResumeCommandValidator.MaxFileBytes + (1L * 1024 * 1024);

    public static void MapResumesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/resumes").WithTags("Resumes");

        group.MapGet("/", async (
            IMediator mediator,
            int page = 1,
            int pageSize = 20,
            CancellationToken ct = default) =>
        {
            var result = await mediator.Send(new GetResumesQuery(page, pageSize), ct);
            return Results.Ok(result);
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        group.MapGet("/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetResumeByIdQuery(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization();

        // CV import + parse (F4-8). multipart/form-data with a single "file" part
        // (PDF/DOCX). The bytes are read into the command HERE — IFormFile never crosses
        // into Application (Clean Architecture). The body-size feature is tightened per
        // request so an oversize upload is rejected by Kestrel before it is buffered; the
        // handler then runs the authoritative magic-byte format gate + the personnummer
        // guard on the raw text before persist (Invariant 1) and encrypts the CV-PII
        // shadows (Invariant 3). The response carries no CV-PII (id + parse summary only).
        group.MapPost("/import", async (
            HttpRequest request, IMediator mediator, CancellationToken ct) =>
        {
            var bodySize = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodySize is { IsReadOnly: false })
                bodySize.MaxRequestBodySize = MaxUploadBytes;

            if (!request.HasFormContentType)
                return Results.Problem(
                    detail: "Ladda upp filen som multipart/form-data.",
                    title: "Resume.InvalidUpload", statusCode: 400);

            // Bound the multipart parser explicitly: manual ReadFormAsync uses the default
            // FormOptions (MultipartBodyLengthLimit 128 MB, ValueCountLimit 1024), which the
            // per-request body cap above does NOT constrain. A single "file" part plus a
            // little headroom is all this endpoint accepts — closes the part-flooding surface.
            request.HttpContext.Features.Set<IFormFeature>(new FormFeature(request, new FormOptions
            {
                MultipartBodyLengthLimit = MaxUploadBytes,
                ValueCountLimit = 8,
            }));

            IFormCollection form;
            try
            {
                form = await request.ReadFormAsync(ct);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                // A malformed/truncated multipart body is a client error, not a 500.
                return Results.Problem(
                    detail: "Det gick inte att läsa filuppladdningen. Försök igen.",
                    title: "Resume.InvalidUpload", statusCode: 400);
            }

            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
                return Results.Problem(
                    detail: "Ingen fil bifogades. Välj en PDF- eller Word-fil (DOCX).",
                    title: "Resume.NoFile", statusCode: 400);

            using var buffer = new MemoryStream(
                file.Length <= MaxUploadBytes ? (int)file.Length : 0);
            await file.CopyToAsync(buffer, ct);

            var command = new ImportResumeCommand(file.FileName, file.ContentType, buffer.ToArray());
            var result = await mediator.Send(command, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/resumes/parsed/{result.Value.ParsedResumeId}", result.Value)
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.ResumeImportPolicy);

        // The PendingReview parsed-CV staging artifact (F4-8), owner-scoped — drives the
        // review + gap-fill UI. Returns the owner's decrypted, loosely-parsed content
        // (IRequiresFieldEncryptionKey); cross-user/unknown → 404 (no enumeration oracle);
        // a promoted/discarded artifact is invisible (global DeletedAt filter) → 404.
        group.MapGet("/parsed/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetParsedResumeQuery(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        group.MapPost("/", async (
            CreateResumeBody body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new CreateResumeCommand(body.Name, body.FullName), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/resumes/{result.Value}", new { id = result.Value })
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization();

        group.MapPatch("/{id:guid}", async (
            Guid id, RenameResumeBody body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new RenameResumeCommand(id, body.Name), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization();

        group.MapPut("/{id:guid}/master", async (
            Guid id, ResumeContentDto content, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new UpdateMasterContentCommand(id, content), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization();

        group.MapPut("/{id:guid}/language", async (
            Guid id, SetLanguageBody body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new SetResumeLanguageCommand(id, body.Language), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization();

        group.MapPut("/{id:guid}/set-as-primary", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new SetPrimaryResumeCommand(id), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization();

        group.MapDelete("/{id:guid}", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new DeleteResumeCommand(id), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization();

        group.MapDelete("/{id:guid}/versions/{versionId:guid}", async (
            Guid id, Guid versionId, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new DeleteResumeVersionCommand(id, versionId), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : Results.Problem(detail: result.Error.Message, title: result.Error.Code, statusCode: 400);
        }).RequireAuthorization();
    }

    private sealed record CreateResumeBody(string Name, string FullName);
    private sealed record RenameResumeBody(string Name);
    private sealed record SetLanguageBody(string Language);
}
