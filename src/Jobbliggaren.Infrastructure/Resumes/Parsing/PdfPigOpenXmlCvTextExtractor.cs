using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Jobbliggaren.Application.Resumes.Abstractions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Jobbliggaren.Infrastructure.Resumes.Parsing;

/// <summary>
/// Deterministic CV text extraction behind <see cref="ICvTextExtractor"/> (F4-8,
/// NO AI/LLM). PdfPig (PDF) and DocumentFormat.OpenXml (DOCX) live ONLY here — no SDK
/// type crosses the Application port. Fail-soft: a corrupt/encrypted/oversized/scanned
/// file never throws — it returns a fallback status so the handler routes to manual
/// entry (OQ5). Bounded work (page cap + output-char cap + streaming DOCX read + a
/// pre-decompression package-size guard + per-node truncation, #268 SEC-1) keeps
/// extraction within the ADR 0045 memory budget and mitigates zip/decompression bombs.
/// </summary>
internal sealed class PdfPigOpenXmlCvTextExtractor : ICvTextExtractor
{
    // Defence-in-depth bounds (the validator already caps the input at 10 MiB).
    private const int MaxPages = 50;
    private const int MaxOutputChars = 1_000_000;

    // #268 SEC-1 (DOCX decompression/zip bomb): DOCX is a ZIP/OPC container and the
    // validator caps only the COMPRESSED input at 10 MiB. DEFLATE permits ~1000:1, so a
    // small .docx can declare/expand to multi-GB; OpenXmlReader.GetText() would materialize
    // a single oversized <w:t> node whole BEFORE the char cap is checked → OOM on the
    // low-RAM VPS (ADR 0045 budget). Reject before decompression when the package's total
    // declared uncompressed size exceeds this ceiling — generous for a real CV (KB–low-MB),
    // far below RAM exhaustion — and bound each text node to the remaining char budget so a
    // single oversized node is truncated, never appended whole.
    private const long MaxUncompressedBytes = 64L * 1024 * 1024;

    public CvExtractionResult Extract(ReadOnlyMemory<byte> file, CvFileKind kind)
    {
        if (file.IsEmpty)
            return new CvExtractionResult(string.Empty, CvExtractionStatus.Empty);

        return kind switch
        {
            CvFileKind.Pdf => ExtractPdf(file),
            CvFileKind.Docx => ExtractDocx(file),
            _ => new CvExtractionResult(string.Empty, CvExtractionStatus.Empty),
        };
    }

    private static CvExtractionResult ExtractPdf(ReadOnlyMemory<byte> file)
    {
        try
        {
            using var document = PdfDocument.Open(file.ToArray());

            var builder = new StringBuilder();
            var pageCount = 0;

            foreach (var page in document.GetPages())
            {
                if (++pageCount > MaxPages)
                    break;

                // Content-order extraction reconstructs reading order with spacing
                // (raw page.Text concatenates glyphs without reliable spaces).
                var pageText = ContentOrderTextExtractor.GetText(page);
                if (!string.IsNullOrEmpty(pageText))
                {
                    builder.Append(pageText).Append('\n');
                    if (builder.Length >= MaxOutputChars)
                        break;
                }
            }

            var text = Normalize(builder.ToString());

            if (text.Length == 0)
            {
                // Pages existed but yielded no text layer ⇒ almost certainly scanned.
                return new CvExtractionResult(
                    string.Empty,
                    document.NumberOfPages > 0
                        ? CvExtractionStatus.NoTextLayer
                        : CvExtractionStatus.Empty);
            }

            return new CvExtractionResult(text, CvExtractionStatus.Extracted);
        }
        catch (Exception)
        {
            // Encrypted/corrupt/malformed PDF — never surface the library exception or
            // any file content; route to manual fallback (OQ5).
            return new CvExtractionResult(string.Empty, CvExtractionStatus.Empty);
        }
    }

    private static CvExtractionResult ExtractDocx(ReadOnlyMemory<byte> file)
    {
        try
        {
            var bytes = file.ToArray();

            // #268 SEC-1: pre-flight zip-bomb guard — reject before decompression if the OPC
            // package's total DECLARED uncompressed size blows past the ceiling. A real CV is
            // KB–low-MB; a multi-GB-declared part is a bomb. Catches the honest bomb (the
            // crafted document.xml that declares/expands to GBs) without inflating a single byte.
            if (DeclaredUncompressedBytesExceed(bytes, MaxUncompressedBytes))
                return new CvExtractionResult(string.Empty, CvExtractionStatus.Empty);

            // Read-only, in-memory. Stream the parts (OpenXmlReader) rather than loading
            // the whole DOM, so a crafted/oversized main part is bounded by the char cap.
            using var stream = new MemoryStream(bytes, writable: false);
            using var document = WordprocessingDocument.Open(stream, isEditable: false);

            var mainPart = document.MainDocumentPart;
            if (mainPart is null)
                return new CvExtractionResult(string.Empty, CvExtractionStatus.Empty);

            var builder = new StringBuilder();
            using var reader = OpenXmlReader.Create(mainPart);

            while (reader.Read())
            {
                if (reader.ElementType == typeof(Text) && reader.IsStartElement)
                {
                    // #268 SEC-1: bound EACH node to the remaining char budget so a single
                    // oversized <w:t> is truncated, not appended whole (defence-in-depth
                    // beyond the package-size guard above).
                    var node = reader.GetText();
                    var remaining = MaxOutputChars - builder.Length;
                    if (node.Length >= remaining)
                    {
                        builder.Append(node.AsSpan(0, remaining));
                        break;
                    }

                    builder.Append(node);
                }
                else if (reader.ElementType == typeof(Paragraph) && reader.IsEndElement)
                {
                    builder.Append('\n');
                }

                if (builder.Length >= MaxOutputChars)
                    break;
            }

            var text = Normalize(builder.ToString());

            return text.Length == 0
                ? new CvExtractionResult(string.Empty, CvExtractionStatus.Empty)
                : new CvExtractionResult(text, CvExtractionStatus.Extracted);
        }
        catch (Exception)
        {
            // Not a valid OPC package / corrupt DOCX — fail soft.
            return new CvExtractionResult(string.Empty, CvExtractionStatus.Empty);
        }
    }

    // #268 SEC-1: sum the OPC package's DECLARED uncompressed entry sizes (read from the
    // ZIP central directory — no decompression) and report whether the total exceeds the
    // ceiling. A genuine zip bomb declares a multi-GB uncompressed size; this rejects it
    // before WordprocessingDocument inflates anything. Reading the central directory is
    // bounded by the 10 MiB compressed input cap.
    private static bool DeclaredUncompressedBytesExceed(byte[] bytes, long ceiling)
    {
        using var zipStream = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

        long total = 0;
        foreach (var entry in archive.Entries)
        {
            total += entry.Length;
            if (total > ceiling)
                return true;
        }

        return false;
    }

    // Deterministic newline normalization (CRLF/CR → LF) and trimming. Preserves åäö
    // (UTF-8 strings from both libraries are already Unicode); no culture-dependent ops.
    private static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }
}
