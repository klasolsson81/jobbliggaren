namespace Jobbliggaren.Application.Resumes.Abstractions;

/// <summary>
/// The supported CV upload formats for deterministic text extraction (F4-8).
/// PdfPig handles <see cref="Pdf"/>; DocumentFormat.OpenXml handles <see cref="Docx"/>
/// (both confined to Infrastructure — the SDK types never cross this port).
/// </summary>
public enum CvFileKind
{
    Pdf,
    Docx,
}
