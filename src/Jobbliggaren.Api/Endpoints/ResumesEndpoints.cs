using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.JobSeekers.Commands.SetPrimaryResume;
using Jobbliggaren.Application.Resumes.Commands.ApplyCvImprovements;
using Jobbliggaren.Application.Resumes.Commands.CreateResume;
using Jobbliggaren.Application.Resumes.Commands.DeleteResume;
using Jobbliggaren.Application.Resumes.Commands.DeleteResumeVersion;
using Jobbliggaren.Application.Resumes.Commands.ImportResume;
using Jobbliggaren.Application.Resumes.Commands.PromoteParsedResume;
using Jobbliggaren.Application.Resumes.Commands.RenameResume;
using Jobbliggaren.Application.Resumes.Commands.SetFindingStatus;
using Jobbliggaren.Application.Resumes.Commands.SetResumeLanguage;
using Jobbliggaren.Application.Resumes.Commands.UpdateMasterContent;
using Jobbliggaren.Application.Resumes.Improvement.Queries.PreviewCvImprovement;
using Jobbliggaren.Application.Resumes.Improvement.Queries.SuggestCvImprovements;
using Jobbliggaren.Application.Resumes.Queries;
using Jobbliggaren.Application.Resumes.Queries.GetLatestPendingParsedResume;
using Jobbliggaren.Application.Resumes.Queries.GetParsedResume;
using Jobbliggaren.Application.Resumes.Queries.GetParsedResumeOccupations;
using Jobbliggaren.Application.Resumes.Queries.GetParsedResumeSkills;
using Jobbliggaren.Application.Resumes.Queries.GetResumeById;
using Jobbliggaren.Application.Resumes.Queries.GetResumes;
using Jobbliggaren.Application.Resumes.Rendering.Queries.RenderCv;
using Jobbliggaren.Application.Resumes.Rendering.Queries.RenderResume;
using Jobbliggaren.Application.Resumes.Review.Queries.ReviewParsedResume;
using Jobbliggaren.Application.Resumes.Review.Queries.ReviewResume;
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
                : result.Error.ToProblemResult();
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

        // The owner's non-PII SSYK occupation proposals for a PendingReview parsed CV (F4-8),
        // derived deterministically at import and stored as plain jsonb. Drives the match-setup
        // wizard's CV-suggest for a freshly-uploaded-but-not-yet-promoted CV. Projects the jsonb
        // column ONLY — never reads/decrypts the CV-PII (PII-minimisation, CTO Variant B
        // 2026-06-21). Cross-user/unknown/promoted → 404 (global DeletedAt filter + fail-closed
        // handler, no enumeration oracle).
        group.MapGet("/parsed/{id:guid}/occupations", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetParsedResumeOccupationsQuery(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        // The owner's non-PII JobTech skill proposals for a PendingReview parsed CV
        // (ADR 0079 STEG 3), resolved deterministically at import and stored as plain
        // jsonb. Drives the match-setup skill section's CV-suggest for a freshly-uploaded-
        // but-not-yet-promoted CV. Projects the jsonb column ONLY — never reads/decrypts
        // the CV-PII (PII-minimisation, mirrors the occupations projection). Cross-user/
        // unknown/promoted → 404 (global DeletedAt filter + fail-closed handler, no
        // enumeration oracle).
        group.MapGet("/parsed/{id:guid}/skills", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetParsedResumeSkillsQuery(id), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        // The CURRENT user's most-recent PendingReview parsed-CV summary (id + file name + upload
        // time), or 200 with the JSON literal `null` when the user has no pending CV (a normal,
        // non-error state — not 404). Drives the /cv "complete your CV" card after the welcome flow
        // reads a CV without promoting it (onboarding decouple, ADR 0079-amendment). Owner-scoped by
        // construction (no client id → no IDOR surface); projects plaintext metadata only — never
        // reads/decrypts the CV-PII (PII-minimisation, mirrors the occupations/skills projections).
        // The null case writes the literal `null` token explicitly: minimal-API's
        // WriteResultAsJsonAsync short-circuits a null value to an EMPTY body, which the FE BFF's
        // JSON parse (nullable schema) cannot read — so Results.Content emits the `null` it expects.
        group.MapGet("/parsed/latest-pending", async (IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetLatestPendingParsedResumeQuery(), ct);
            return result is null
                ? Results.Content("null", "application/json")
                : Results.Json(result);
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        // Deterministic CV review (F4-9): PASS/WARN/FAIL + cited evidence, owner-scoped. The
        // ?profile= must be Ats|Visual (the validator fails loud → 400). The transmitted
        // CvReviewDto carries verdicts whose evidence is ALREADY pnr-redacted at the engine
        // choke point (CvReviewEngine.ReviewAsync) — this endpoint is the first surface to
        // transmit it; the mapper introduces no un-redacted field.
        group.MapGet("/parsed/{id:guid}/review", async (
            Guid id, string? profile, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new ReviewParsedResumeQuery(id, profile ?? string.Empty), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        // Deterministic propose-and-approve CV improvements (F4-10): the diffs are never
        // auto-applied (§5) — the FE proposes them and the user approves. Owner-scoped.
        group.MapGet("/parsed/{id:guid}/improvements", async (
            Guid id, string? profile, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new SuggestCvImprovementsQuery(id, profile ?? string.Empty), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        // Deterministic CV render (F4-10): the QuestPDF PDF (ATS-plain | visual), streamed
        // compute-on-demand and never persisted (Invariant 3). Returned as the raw PDF body
        // (Results.File), never JSON — a base64 byte[] in JSON would bloat ~33% and the browser
        // could not open it. Owner-scoped; ?profile= must be Ats|Visual.
        group.MapGet("/parsed/{id:guid}/render", async (
            Guid id, string? profile, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new RenderCvQuery(id, profile ?? string.Empty), ct);
            return result is null
                ? Results.NotFound()
                : Results.File(result.PdfBytes, result.ContentType, $"cv-{result.Profile.ToLowerInvariant()}.pdf");
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.ResumeRenderPolicy);

        // Deterministic render of a PROMOTED, canonical Resume by its id (TD-112 / #202) — the
        // render-by-Resume-id path the promoted Resume surface (ResumeCard) needs (the parsed
        // render above keys on a parsedId the promoted grid no longer has). Same QuestPDF output
        // shape (ATS-plain | visual), streamed compute-on-demand, never persisted (Invariant 3).
        // Raw PDF body (Results.File), never JSON. Owner-scoped; ?profile= must be Ats|Visual.
        group.MapGet("/{id:guid}/render", async (
            Guid id, string? profile, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new RenderResumeQuery(id, profile ?? string.Empty), ct);
            return result is null
                ? Results.NotFound()
                : Results.File(result.PdfBytes, result.ContentType, $"cv-{result.Profile.ToLowerInvariant()}.pdf");
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.ResumeRenderPolicy);

        // Deterministic CV review of a PROMOTED/app-built canonical Resume (Fas 4b PR-4,
        // #653, ADR 0093 §D8) — the same rubric engine as the parsed review above, via the
        // canonical adapter over the shared linearizer. Verdicts are computed on demand
        // (ADR 0074) and merged with the persisted finding-status overlay (D2(e)); the
        // evidence is pnr-redacted at the engine choke point exactly like the parsed path.
        // Owner-scoped; ?profile= must be Ats|Visual (validator fails loud → 400).
        group.MapGet("/{id:guid}/review", async (
            Guid id, string? profile, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new ReviewResumeQuery(id, profile ?? string.Empty), ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        // The user's decision on one review finding — "markera som klar" (Resolved),
        // "ignorera regeln" (Ignored) or a revert (Open); handoff §5.3, D2(e). Writes only
        // a status enum + a SERVER-derived fingerprint into the DEK-free ledger (no CV
        // text; the fingerprint is recomputed from the engine's current finding — never
        // client-submitted, Invariant 2). Owner-scoped; audited via IAuditableCommand.
        group.MapPut("/{id:guid}/review/findings/{criterionId}/status", async (
            Guid id, string criterionId, SetFindingStatusBody body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new SetFindingStatusCommand(id, criterionId, body.Status), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : result.Error.ToProblemResult();
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);

        // Promote a PendingReview parsed CV into a canonical Resume (Fas 4 STEG A). The body
        // carries the full, user-approved gap-filled ResumeContentDto (DQ1 Variant A — the
        // approved content IS the Resume; the handler re-scans personnummer over the submitted
        // free text and never synthesises, §5). Owner-scoped, IDOR fail-closed (unknown/cross-
        // user → 404). 201 → the new Resume.
        group.MapPost("/parsed/{id:guid}/promote", async (
            Guid id, PromoteParsedResumeBody body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new PromoteParsedResumeCommand(id, body.Name, body.Content), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/resumes/{result.Value}", new { id = result.Value })
                : result.Error.ToProblemResult();
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);

        group.MapPost("/", async (
            CreateResumeBody body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new CreateResumeCommand(body.Name, body.FullName), ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/resumes/{result.Value}", new { id = result.Value })
                : result.Error.ToProblemResult();
        }).RequireAuthorization();

        group.MapPatch("/{id:guid}", async (
            Guid id, RenameResumeBody body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new RenameResumeCommand(id, body.Name), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : result.Error.ToProblemResult();
        }).RequireAuthorization();

        group.MapPut("/{id:guid}/master", async (
            Guid id, ResumeContentDto content, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new UpdateMasterContentCommand(id, content), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : result.Error.ToProblemResult();
        }).RequireAuthorization();

        group.MapPut("/{id:guid}/language", async (
            Guid id, SetLanguageBody body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new SetResumeLanguageCommand(id, body.Language), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : result.Error.ToProblemResult();
        }).RequireAuthorization();

        group.MapPut("/{id:guid}/set-as-primary", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new SetPrimaryResumeCommand(id), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : result.Error.ToProblemResult();
        }).RequireAuthorization();

        group.MapDelete("/{id:guid}", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new DeleteResumeCommand(id), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : result.Error.ToProblemResult();
        }).RequireAuthorization();

        group.MapDelete("/{id:guid}/versions/{versionId:guid}", async (
            Guid id, Guid versionId, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new DeleteResumeVersionCommand(id, versionId), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : result.Error.ToProblemResult();
        }).RequireAuthorization();

        // EFTER-preview of one frame application (Fas 4b PR-7, #656; handoff §6.2 — the
        // preview is ALWAYS shown before "Åtgärda direkt" activates). A READ that composes
        // the real post-apply text server-side (never client text, ADR 0074) and MINTS the
        // finding fingerprint the apply call echoes back. POST because the slot inputs are
        // a structured payload — semantically still a query, nothing persists.
        group.MapPost("/{id:guid}/improvements/preview", async (
            Guid id, PreviewImprovementBody body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(
                new PreviewCvImprovementQuery(id, body.CriterionId, body.FrameId, body.SlotInputs), ct);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error.ToProblemResult();
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        // The apply-half of propose-and-approve (Fas 4b PR-7, #656; ADR 0093 §D2): ids +
        // frame inputs ONLY — the server recomputes the review, verifies the echoed
        // fingerprint (mismatch → 409 "CV changed, re-review"), composes via FromFrame,
        // personnummer-guards the composed content, and writes ONCE through
        // Resume.UpdateMasterContent. Owner-scoped, audited, write-rate-limited.
        group.MapPost("/{id:guid}/improvements/apply", async (
            Guid id, ApplyImprovementsBody body, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new ApplyCvImprovementsCommand(id, body.Changes), ct);
            return result.IsSuccess
                ? Results.NoContent()
                : result.Error.ToProblemResult();
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.MeWritePolicy);
    }

    private sealed record CreateResumeBody(string Name, string FullName);
    private sealed record RenameResumeBody(string Name);
    private sealed record SetLanguageBody(string Language);
    private sealed record SetFindingStatusBody(string Status);
    private sealed record PromoteParsedResumeBody(string Name, ResumeContentDto Content);
    private sealed record PreviewImprovementBody(
        string CriterionId, string FrameId, IReadOnlyDictionary<string, string> SlotInputs);
    private sealed record ApplyImprovementsBody(IReadOnlyList<FrameApplyInput> Changes);
}
