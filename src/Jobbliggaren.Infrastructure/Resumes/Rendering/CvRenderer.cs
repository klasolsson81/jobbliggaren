using Jobbliggaren.Application.Resumes.Rendering.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Jobbliggaren.Infrastructure.Resumes.Rendering;

/// <summary>
/// The deterministic CV renderer (Fas 4 STEG 10, F4-10; QuestPDF) — produces an ATS-plain or a
/// visual PDF from the SAME <see cref="CvDocumentModel"/> projection of the parsed CV (BUILD
/// §8.3). Pure: takes an already-decrypted <see cref="ParsedResume"/> (Invariant 3 owned by the
/// read-handler), produces bytes in memory, and streams them compute-on-demand (CTO Q6 — no
/// artifact persistence, nothing written to disk). The document metadata timestamps are PINNED
/// (no wall-clock — §5 forbids DateTime.Now) so the same CV renders to stable, deterministic
/// CONTENT (identical visible text + layout run-to-run). The exact PDF BYTES are deliberately NOT
/// identity-stable — QuestPDF varies the PDF <c>/ID</c> and its process-global font-subset packing —
/// so callers must never hash / ETag / dedupe the bytes; determinism here is a content guarantee,
/// not a byte guarantee. NO AI/LLM. The QuestPDF Community licence is set (idempotently) in this type's static
/// constructor — so every construction path is covered (DI via <c>AddCvRendering</c> and
/// direct construction in tests).
/// </summary>
internal sealed class CvRenderer : ICvRenderer
{
    // Pinned, non-wall-clock metadata stamp — deterministic output for a given input.
    private static readonly DateTime FixedTimestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // QuestPDF requires the licence type to be declared once before any document is generated.
    // Set here (idempotent) so any construction path — DI (AddCvRendering) or direct (tests) —
    // has it; Community is source-available, free under USD 1M revenue, non-copyleft (ADR 0050).
    static CvRenderer() =>
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    public ValueTask<RenderedCv> RenderAsync(
        ParsedResume parsedResume, CvTemplateOptions options, RenderProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parsedResume);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var model = CvDocumentModel.From(parsedResume.Content);
        var labels = CvRenderStrings.For(parsedResume.DetectedLanguage);

        var document = Document.Create(container =>
            CvDocumentComposer.Compose(container, model, labels, options, profile));

        var metadata = new DocumentMetadata
        {
            Title = "CV",
            CreationDate = FixedTimestamp,
            ModifiedDate = FixedTimestamp,
        };

        var bytes = document.WithMetadata(metadata).GeneratePdf();

        return ValueTask.FromResult(
            new RenderedCv(bytes, "application/pdf", profile, parsedResume.DetectedLanguage));
    }

    public ValueTask<RenderedCv> RenderAsync(
        ResumeContent content, ResumeLanguage language, CvTemplateOptions options, RenderProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var labels = CvRenderStrings.For(language);
        var model = CvDocumentModel.From(
            content, labels.Ongoing, proficiency => CvRenderStrings.ProficiencyLabel(proficiency, language));

        var document = Document.Create(container =>
            CvDocumentComposer.Compose(container, model, labels, options, profile));

        var metadata = new DocumentMetadata
        {
            Title = "CV",
            CreationDate = FixedTimestamp,
            ModifiedDate = FixedTimestamp,
        };

        var bytes = document.WithMetadata(metadata).GeneratePdf();

        return ValueTask.FromResult(
            new RenderedCv(bytes, "application/pdf", profile, language));
    }
}
