using Jobbliggaren.Domain.Resumes.Parsing;

namespace Jobbliggaren.Infrastructure.Resumes.Review.Rules;

/// <summary>
/// Swedish presentation labels for the parse-diagnostic enums surfaced in CV-review evidence
/// (D1 file-format, D6 standard-headings). The enum members carry English identifiers (code is
/// English, §1) but the evidence text is user-facing Swedish (§10), so a raw enum token must
/// never leak into it (#478 Low: "Experience, Education" / "ExtractionFailed" were rendered
/// verbatim). Keeping the map HERE — a presentation concern in the review layer, not in Domain —
/// preserves the Clean Architecture boundary (Domain stays UI-copy-free). Every enum value has a
/// label; the exhaustiveness test + the throwing default arm are the drift-guard: a new value
/// cannot ship without a Swedish label (parity the crossref-badge label pattern).
/// </summary>
internal static class ReviewEvidenceLabels
{
    /// <summary>The Swedish product name for a detected CV section — the vocabulary the
    /// <see cref="ParsedSectionKind"/> XML-doc already documents (kontakt / profil /
    /// arbetslivserfarenhet / utbildning / kompetenser / språk). Lowercase: it appears mid-sentence
    /// in the D6 evidence, where Swedish common nouns are not capitalised.</summary>
    public static string Section(ParsedSectionKind kind) => kind switch
    {
        ParsedSectionKind.Contact => "kontakt",
        ParsedSectionKind.Profile => "profil",
        ParsedSectionKind.Experience => "arbetslivserfarenhet",
        ParsedSectionKind.Education => "utbildning",
        ParsedSectionKind.Skills => "kompetenser",
        ParsedSectionKind.Languages => "språk",
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, "New ParsedSectionKind has no Swedish label. Add it to ReviewEvidenceLabels."),
    };

    /// <summary>The Swedish reason for a degraded/uncertain parse (D1) — so the evidence explains
    /// the degradation to the user instead of leaking an English enum token.</summary>
    public static string Fallback(ParseFallbackReason reason) => reason switch
    {
        ParseFallbackReason.None => "ingen avvikelse",
        ParseFallbackReason.ExtractionFailed => "extraktionen misslyckades",
        ParseFallbackReason.NoSectionsDetected => "inga sektioner kunde identifieras",
        ParseFallbackReason.EncodingSuspect => "teckenkodningen ser felaktig ut",
        ParseFallbackReason.ScannedImageNoText => "inscannad bild utan textlager",
        _ => throw new ArgumentOutOfRangeException(
            nameof(reason), reason, "New ParseFallbackReason has no Swedish label. Add it to ReviewEvidenceLabels."),
    };
}
