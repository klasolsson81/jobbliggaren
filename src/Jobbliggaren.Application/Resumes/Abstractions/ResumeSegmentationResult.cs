using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Application.Resumes.Abstractions;

/// <summary>
/// The result of segmenting extracted CV text into structured content (F4-8). Composes
/// Domain value objects directly (Application depends on Domain — legal): the parsed
/// <see cref="Content"/> and its first-class <see cref="Confidence"/> (OQ5) become the
/// <c>ParsedResume</c> aggregate's state, and <see cref="DetectedLanguage"/> is the
/// deterministic language detection (F4-8 scope; English analysis deferred to F4-9).
/// </summary>
public sealed record ResumeSegmentationResult(
    ParsedResumeContent Content,
    ResumeLanguage DetectedLanguage,
    ParseConfidence Confidence);
