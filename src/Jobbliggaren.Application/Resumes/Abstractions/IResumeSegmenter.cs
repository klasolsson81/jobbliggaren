namespace Jobbliggaren.Application.Resumes.Abstractions;

/// <summary>
/// Deterministic segmentation of extracted CV text into structured sections +
/// per-section parse confidence + detected language (F4-8, NO AI/LLM). A pure
/// string-algorithm port: it takes the raw extracted text and is testable without any
/// binary PDF/DOCX fixture (CLAUDE.md §2.4 — the split from <see cref="ICvTextExtractor"/>
/// exists precisely so the segmentation logic is unit-testable on a plain string).
/// </summary>
public interface IResumeSegmenter
{
    /// <summary>
    /// Segments <paramref name="rawText"/> (the original extracted text, NOT the
    /// personnummer-normalized scan-copy) into a <see cref="ResumeSegmentationResult"/>.
    /// Deterministic: identical text yields identical content, confidence and language.
    /// </summary>
    ResumeSegmentationResult Segment(string rawText);
}
