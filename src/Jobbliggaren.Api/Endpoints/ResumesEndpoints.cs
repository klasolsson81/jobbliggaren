using Jobbliggaren.Api.RateLimiting;
using Jobbliggaren.Application.JobSeekers.Commands.SetPrimaryResume;
using Jobbliggaren.Application.Resumes.Commands.AutoPromoteParsedResume;
using Jobbliggaren.Application.Resumes.Commands.CreateResume;
using Jobbliggaren.Application.Resumes.Commands.DeleteResume;
using Jobbliggaren.Application.Resumes.Commands.DeleteResumeVersion;
using Jobbliggaren.Application.Resumes.Commands.DiscardParsedResume;
using Jobbliggaren.Application.Resumes.Commands.ImportResume;
using Jobbliggaren.Application.Resumes.Commands.PromoteParsedResume;
using Jobbliggaren.Application.Resumes.Commands.RenameResume;
using Jobbliggaren.Application.Resumes.Commands.SetFindingStatus;
using Jobbliggaren.Application.Resumes.Commands.SetResumeLanguage;
using Jobbliggaren.Application.Resumes.Commands.UpdateMasterContent;
using Jobbliggaren.Application.Resumes.Queries;
using Jobbliggaren.Application.Resumes.Queries.DownloadResumeFile;
using Jobbliggaren.Application.Resumes.Queries.GetLatestPendingParsedResume;
using Jobbliggaren.Application.Resumes.Queries.GetParsedResume;
using Jobbliggaren.Application.Resumes.Queries.GetParsedResumeOccupations;
using Jobbliggaren.Application.Resumes.Queries.GetParsedResumeSkills;
using Jobbliggaren.Application.Resumes.Queries.GetResumeAtsText;
using Jobbliggaren.Application.Resumes.Queries.GetResumeById;
using Jobbliggaren.Application.Resumes.Queries.GetResumes;
using Jobbliggaren.Application.Resumes.Rendering.Queries.RenderCv;
using Jobbliggaren.Application.Resumes.Rendering.Queries.RenderResume;
using Jobbliggaren.Application.Resumes.Review.Queries.ReviewParsedResume;
using Jobbliggaren.Application.Resumes.Review.Queries.ReviewResume;
using Jobbliggaren.Application.Resumes.Sections.Queries.GetCvSectionSuggestions;
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

        // CV import + parse + auto-promote (F4-8; CV-pivot 5c — R4 "spara direkt"). One
        // multipart/form-data upload: a "file" part (PDF/DOCX), an optional
        // "personnummerAcknowledged" flag (the 5b consent re-POST), and an optional "name"
        // (the CV display name the form prefills). The bytes are read into the command HERE
        // — IFormFile never crosses into Application (Clean Architecture). The handler runs
        // the magic-byte gate + the personnummer guard on the raw text before persist
        // (Invariant 1) and encrypts the CV-PII shadows (Invariant 3). On a successful
        // import the endpoint ALWAYS attempts auto-promote (two sends, two UnitOfWork — 5a
        // CTO-bind §3); the response is PII-free (ids + parse-summary + the count/kinds
        // personnummer finding, never a value).
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

            // The personnummer-consent acknowledge (CV-pivot 5b/5c, ADR 0114 §D3): the FE
            // re-POSTs the SAME bytes with this flag after the consent dialog. Fail-closed —
            // an absent/malformed value parses to false, so a flagged body is never captured
            // without an explicit "true". The flag is INERT without a server-side finding: the
            // consent stamps follow the handler's own scan, never this client claim (ADR 0114
            // §D3 / M-A) — a client that sets it on every upload writes no spurious consent.
            // The parse RESULT is consumed (&&): an absent/unparseable value is a deliberate
            // false (fail-closed), never a silently ignored TryParse default (CA1806).
            var personnummerAcknowledged =
                bool.TryParse(form["personnummerAcknowledged"], out var acknowledged) && acknowledged;

            // The CV display name (CV-pivot 5c): the upload form prefills the account holder's
            // name and lets the user edit it. Absent/blank → the AutoPromote handler resolves
            // JobSeeker.DisplayName (5a CTO-bind R5). Never the parsed file's contact name.
            var nameOverride = form["name"].ToString();
            if (string.IsNullOrWhiteSpace(nameOverride))
                nameOverride = null;

            using var buffer = new MemoryStream(
                file.Length <= MaxUploadBytes ? (int)file.Length : 0);
            await file.CopyToAsync(buffer, ct);

            // ── Import (txn 1): extract → personnummer guard → segment → capture the original
            // (consented iff the body is flagged AND acknowledged) → persist the PendingReview
            // parse. The response carries the PII-free personnummer finding the FE needs to
            // decide whether to raise the consent dialog.
            var importResult = await mediator.Send(
                new ImportResumeCommand(
                    file.FileName, file.ContentType, buffer.ToArray(), personnummerAcknowledged),
                ct);
            if (importResult.IsFailure)
                return importResult.Error.ToProblemResult();

            var import = importResult.Value;

            // ── Auto-promote (txn 2, ALWAYS — R4: the import-without-promote onboarding flow is
            // retired; every upload attempts "spara direkt"). Two sends in the endpoint, two
            // UnitOfWork (5a CTO-bind §3): a LeftPending fails before any mutation, so its save
            // is a no-op and the parse stays PendingReview for the review flow; a crash between
            // the two degrades gracefully to that same flow. Routing is on the outcome TYPE
            // (status + Outcome discriminator), never the block-reason string (CLAUDE.md §5).
            var autoPromoteResult = await mediator.Send(
                new AutoPromoteParsedResumeCommand(import.ParsedResumeId, nameOverride), ct);
            if (autoPromoteResult.IsFailure)
                return autoPromoteResult.Error.ToProblemResult();

            return autoPromoteResult.Value switch
            {
                AutoPromoteOutcome.Promoted promoted => Results.Created(
                    $"/api/v1/resumes/{promoted.ResumeId}",
                    new ImportOutcomeResponse(
                        import.ParsedResumeId, import.Personnummer,
                        nameof(AutoPromoteOutcome.Promoted), promoted.ResumeId, BlockReason: null)),
                AutoPromoteOutcome.LeftPending pending => Results.Ok(
                    new ImportOutcomeResponse(
                        import.ParsedResumeId, import.Personnummer,
                        nameof(AutoPromoteOutcome.LeftPending), ResumeId: null,
                        pending.Reason.ToString())),
                _ => throw new InvalidOperationException(
                    $"Ohanterat AutoPromoteOutcome '{autoPromoteResult.Value.GetType().Name}'."),
            };
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

        // GET /parsed/{id}/improvements (the propose-half of propose-and-approve, F4-10) was
        // REMOVED with the åtgärda-lager's deferral (CV-pivot 2026-07-16, ADR 0112 amending
        // ADR 0093, CTO-bind D8 Opt C): without the builder, an applied improvement re-renders
        // away the user's original design, so the whole affordance defers. The MOTOR
        // (CvImprovementEngine + frames + the three handlers) is mothballed in-tree,
        // revert-ready — the pnr-guard subject-set (#650) keeps protecting it.

        // Fas 4b 8b.4a (ADR 0107) — occupation-driven section suggestions for the Slutför guide's
        // "Lägg till sektion" panel. A READ SLICE, deliberately NOT part of the (now retired,
        // ADR 0112) /improvements surface: a section suggestion is not a diff (no Before, no
        // After, no transform), and the improve panel would have labelled it "Ändra
        // sektionsordning". Owner-scoped; the suggestions are offered, never applied (§5 — the
        // engine never rewrites the CV silently).
        group.MapGet("/parsed/{id:guid}/section-suggestions", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetCvSectionSuggestionsQuery(id), ct);
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

        // The mallbyggare's two read surfaces — the ephemeral live-preview
        // (GET /{id}/render/preview, never-persist, 8b.3 CTO-bind Q1 Variant B) and the closed
        // template-options catalog (GET /template-catalog, 8b.3 Q2) — were REMOVED with the
        // builder's deferral (CV-pivot 2026-07-16, ADR 0112 amending ADR 0093): an unreachable
        // authenticated endpoint is attack surface with no product value. The canonical
        // /{id}/render above still renders the PERSISTED options — the substrate
        // (CvTemplateOptions, CvPalette, the six template columns) is load-bearing and stays.

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

        // ATS text view of a canonical Resume (Fas 4b PR-8.2, ADR 0093 §D5(e); CTO-bind
        // Q3): the shared-linearizer text — the "what we generate from your app copy"
        // claim, source-discriminated so it can never be conflated with a parsed
        // "what a parser reads from YOUR file" view (deferred per D8). Owner-scoped,
        // pnr-redacted belt-and-braces, never cached (personal content).
        group.MapGet("/{id:guid}/ats-text", async (
            Guid id, HttpContext http, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new GetResumeAtsTextQuery(id), ct);
            http.Response.Headers.CacheControl = "private, no-store";
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.MeListReadPolicy);

        // Owner-scoped download of a stored ORIGINAL CV file (Fas 4b PR-9b, ADR 0100 §D3 read-path,
        // DPIA #659 M-F2) — the exact uploaded PDF/DOCX, decrypted from its Form C envelope via the
        // owner-warmed DEK (DownloadResumeFileQuery is IRequiresFieldEncryptionKey), returned as the
        // raw bytes (Results.File — never JSON) with a fixed, server-derived content-type and an
        // RFC 6266 attachment filename (already personnummer-redacted at rest, re-redacted in the
        // handler belt-and-braces). Owner-scoped, IDOR fail-closed (unknown/cross-user → 404, no
        // enumeration oracle; cross-user attempt logged). Never cached: the path-scoped header
        // middleware (Program.cs, registered before auth) stamps `no-store` + `nosniff` on EVERY
        // response path — 200, 404, the 401 challenge, and a 405 — which the endpoint delegate alone
        // could not cover. Heavy DEK-decrypting read → ResumeRenderPolicy bucket (parity /render).
        group.MapGet("/files/{id:guid}/original", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var dto = await mediator.Send(new DownloadResumeFileQuery(id), ct);
            return dto is null
                ? Results.NotFound()
                : Results.File(dto.Content, dto.ContentType, dto.FileName, enableRangeProcessing: false);
        }).RequireAuthorization()
          .RequireRateLimiting(RateLimitingExtensions.ResumeRenderPolicy);

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

        // Discard a PendingReview parsed CV (Fas 4b PR-8, CTO-bind Q6 — the action card's
        // "Ta bort utkastet"). POST, not DELETE: a soft-delete state transition (the
        // artifact is retained for audit until the retention sweep), parity /promote.
        // Owner-scoped, IDOR fail-closed (unknown/cross-user → 404). Audited.
        group.MapPost("/parsed/{id:guid}/discard", async (
            Guid id, IMediator mediator, CancellationToken ct) =>
        {
            var result = await mediator.Send(new DiscardParsedResumeCommand(id), ct);
            return result.IsSuccess
                ? Results.NoContent()
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

        // PUT /{id}/template-options (the CvTemplateOptions write-half, 8b.2) was REMOVED with
        // the builder's deferral (CV-pivot 2026-07-16, ADR 0112): its only consumer was the
        // mallbyggare, and an unreachable authenticated WRITE endpoint is the worst kind of
        // dormant attack surface. Resume.ChangeTemplateOptions (Domain) stays — persisted
        // options still drive every /{id}/render.

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

        // POST /{id}/improvements/preview (the EFTER-preview, Fas 4b PR-7 #656) and
        // POST /{id}/improvements/apply (the apply-half of propose-and-approve, ADR 0093 §D2)
        // were REMOVED with the åtgärda-lager's deferral (CV-pivot 2026-07-16, ADR 0112,
        // CTO-bind D8 Opt C) — the apply-half was an authenticated WRITE with zero reachable
        // FE consumers. The handlers (Preview/Apply + their fingerprint-echo and
        // personnummer-guard) are mothballed in-tree, revert-ready, still covered by the
        // guard's architecture subject-set (#650) and the Worker-encryption tests.
    }

    private sealed record CreateResumeBody(string Name, string FullName);
    private sealed record RenameResumeBody(string Name);
    private sealed record SetLanguageBody(string Language);
    private sealed record SetFindingStatusBody(string Status);
    private sealed record PromoteParsedResumeBody(string Name, ResumeContentDto Content);

    // The composed outcome of the import→auto-promote endpoint (CV-pivot 5c). Two Application
    // results folded into one HTTP contract: the always-present parse id + the PII-free
    // personnummer finding (the consent-dialog trigger — count/kinds/bool, never a value), plus
    // the auto-promote disposition. Outcome is the TYPE discriminator ("Promoted"/"LeftPending")
    // the FE routes on together with the status code; BlockReason is copy/telemetry only
    // (CLAUDE.md §5 — never routed on). ResumeId is set iff Promoted (201); BlockReason iff
    // LeftPending (200).
    private sealed record ImportOutcomeResponse(
        Guid ParsedResumeId,
        PersonnummerScanDto Personnummer,
        string Outcome,
        Guid? ResumeId,
        string? BlockReason);
}
