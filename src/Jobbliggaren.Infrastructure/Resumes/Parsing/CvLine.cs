using Jobbliggaren.Application.Resumes.Abstractions;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// One line of a section body as the segmenter reads it: its text and what the extractor measured
/// about it (#2353). Carried from <c>RawText</c>'s line index through the section and entry splits, so
/// the entry-boundary reader sees the same line the extractor measured.
/// </summary>
internal readonly record struct CvLine(string Text, CvLineProvenance Provenance);
