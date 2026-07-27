using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Application.Resumes.Commands.AutoPromoteParsedResume;
using Jobbliggaren.Application.Resumes.Common;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.QA.Corpus.Generation;
using Jobbliggaren.QA.Corpus.Layout;

namespace Jobbliggaren.QA.Corpus.Harness;

/// <summary>How faithfully one case's CV survived the chain. Deliberately NOT a boolean: the
/// measured naive PR E turns 5 employments into 15 fragments and then blocks, and a
/// "silent loss = promoted AND delta &gt; 0" boolean would score that GREEN.</summary>
public enum FidelityVerdict
{
    /// <summary>Promoted, and every authored employment and education survived as itself.</summary>
    PromotedFaithful,

    /// <summary>Promoted with fewer entries than authored. The product said the CV was saved and
    /// content is missing — the finding this corpus exists to expose.</summary>
    PromotedLossy,

    /// <summary>Promoted with MORE entries than authored: fragments invented by over-splitting.
    /// Distinct from lossy, and equally dishonest.</summary>
    PromotedInflated,

    /// <summary>A gate blocked. Nothing was claimed and nothing was lost silently.</summary>
    Blocked,

    /// <summary>The auto-promote handler returned a genuine FAILURE rather than a gate verdict.
    /// Its own contract reserves that for an unknown or foreign artifact, or infrastructure -- it
    /// is never a policy decision. Kept distinct from Blocked because rendering a fault as an
    /// honest block is exactly the mis-report this corpus exists to catch.</summary>
    PromoteFaulted,

    /// <summary>The import handler returned a failure before an artifact existed.</summary>
    ImportFailed,

    /// <summary>Extraction produced no usable text.</summary>
    ExtractionFailed,

    /// <summary>The case's own bytes or model are broken. An instrument failure, not a finding.</summary>
    FixtureInvalid,

    /// <summary>The case threw. Loud, and it never aborts the other rows.</summary>
    Crashed,
}

/// <summary>The flat per-case record every report column reads from.</summary>
public sealed record LayoutCaseObservation(
    LayoutCase Case,
    string? ByteProofFailure,
    IReadOnlyList<string> FixtureProblems,
    bool KindResolved,
    CvExtractionStatus? ExtractionStatus,
    int CharCount,
    int LineCount,
    int BlankLineCount,
    bool SegmentRan,
    string? DetectedLanguage,
    int HeadingsDetected,
    int? PreambleChars,
    string? ConfidenceOverall,
    IReadOnlyList<string> SectionEvidence,
    bool? PersonnummerFoundOnParse,
    string? FirstExtractedLine,
    bool ContainsFusedPeriodRole,
    bool AnyLineCarriesBothColumns,
    string ExtractedTextDigest,
    IReadOnlyList<string> ParsedFreeSectionHeadings,
    int? ParsedExperience,
    int? ParsedEducation,
    int GroundTruthExperience,
    int GroundTruthEducation,
    int? PromotedExperience,
    int? PromotedEducation,
    int? WellFormedPromotedExperience,

    // #1060 — what the PROMOTED CV holds, not what the parse held. Without this the corpus
    // cannot distinguish "promoted carrying the preamble" from "promoted having dropped it":
    // PreambleChars above reads ParsedResumeContent, so a mutation that discards the carrier
    // on the way to the canonical CV leaves every row byte-identical. Measured — it did.
    int? PromotedPreambleChars,
    AutoPromoteBlockReason? BlockReason,
    bool Promoted,
    IReadOnlyList<GateCell> Gates,
    IReadOnlyList<MarkerTrace> Markers,
    IReadOnlyList<string> CrossSectionContamination,
    bool? SummaryContainsRenderedProjectHeading,
    bool? RenderedProjectHeadingIsOwnSection,
    int? PromotedSummaryChars,
    string? PromoteFailureCode,
    string? CrashedWithExceptionType,
    FidelityVerdict Verdict);

/// <summary>
/// Runs one <see cref="LayoutCase"/> end to end and produces its observation.
///
/// <para>Every case is wrapped: an exception becomes a <see cref="FidelityVerdict.Crashed"/> row
/// carrying the exception TYPE only (never the message, which could hold CV text). A byte-proof
/// failure is likewise recorded rather than thrown here — the artifact is written BEFORE any
/// assert runs, so the one run that matters always leaves a complete, readable report on disk.</para>
/// </summary>
internal static partial class LayoutChainRunner
{
    internal static async Task<LayoutCaseObservation> RunAsync(LayoutCase c, CancellationToken ct)
    {
        var fixtureProblems = CvGroundTruth.Validate(c.Model);
        byte[] bytes;
        string? byteProofFailure = null;

        try
        {
            bytes = c.Render(c.Model);
        }
        catch (Exception ex)
        {
            return Crashed(c, fixtureProblems, ex.GetType().Name);
        }

        try
        {
            c.ProveBytes(new ByteProofContext(c.Id, bytes));
        }
        catch (ByteProofException ex)
        {
            byteProofFailure = ex.Message;
        }
        catch (Exception ex)
        {
            // A malformed-PDF or ZIP failure inside the proof READER would otherwise escape both
            // this method and the probe's own catch, aborting the run BEFORE the artifact is
            // written -- which breaks the promise that even a breach leaves a readable report.
            // Type only, never the message: the bytes it was parsing are a CV, and one case's are
            // personnummer-bearing.
            byteProofFailure =
                $"INSTRUMENT: the byte-proof reader threw {ex.GetType().Name} for '{c.Id}'";
        }

        var o = await CvChainProbe.RunAsync(
            c.FileName, c.ContentType, bytes, c.AccountDisplayName, ct);
        if (o.CrashedWithExceptionType is not null)
            return Crashed(c, fixtureProblems, o.CrashedWithExceptionType);

        var parsed = o.Parsed;
        var content = parsed?.Content;
        var promotedContent = o.PromotedResume?.MasterVersion.Content;

        var faulted = o.PromoteFailureCode is not null;

        var markers = MarkerTracer
            .Trace(c.Model.EmploymentMarkers, MarkerKind.Employment, o.RawText, parsed,
                o.PromotedResume, faulted)
            .Concat(MarkerTracer.Trace(c.Model.EducationMarkers, MarkerKind.Education, o.RawText,
                parsed, o.PromotedResume, faulted))
            .ToList();

        // The two public calls the handler itself makes at :134 — run here, not re-typed, so the
        // label rung is resolved by observation rather than by inference.
        var label = ResumeLabelResolver.Resolve(null, FixedClock.Default);
        var pnrInLabel = PersonnummerScanner
            .Scan(PersonnummerTextNormalizer.Normalize(label)).Count > 0;

        var gates = GateLadder.From(
            o.BlockReason, o.Promoted, faulted,
            pnrFoundOnParse: parsed?.Personnummer.Found ?? false,
            pnrInResolvedLabel: pnrInLabel);

        var lines = o.RawText.Split('\n');

        var wellFormed = promotedContent?.Experiences.Count(e =>
            !string.IsNullOrWhiteSpace(e.Role)
            && !string.IsNullOrWhiteSpace(e.Company)
            && !string.IsNullOrWhiteSpace(e.RawPeriod));

        return new LayoutCaseObservation(
            Case: c,
            ByteProofFailure: byteProofFailure,
            FixtureProblems: fixtureProblems,
            KindResolved: o.KindResolved,
            ExtractionStatus: o.ExtractionStatus,
            CharCount: o.RawText.Length,
            LineCount: o.LineCount,
            BlankLineCount: o.BlankLineCount,
            SegmentRan: o.SegmentRan,
            DetectedLanguage: parsed?.DetectedLanguage.ToString(),
            HeadingsDetected: CountDetectedHeadings(content),
            PreambleChars: content?.Preamble?.Length,
            ConfidenceOverall: parsed?.Confidence.Overall.ToString(),
            SectionEvidence: SectionEvidence(parsed),

            // The OBSERVED personnummer flag, not the case's declaration. The two are printed side
            // by side: if extraction ever loses an authored personnummer, the divergence is the
            // finding, and a column that printed only the declaration would hide it behind the
            // very content loss this corpus measures.
            PersonnummerFoundOnParse: parsed?.Personnummer.Found,

            // Product-side observables. Each pairs with a byte proof that asserts the AUTHORED
            // geometry: the proof alone would restate the generator, and the observable alone
            // would not say what shape produced it.
            FirstExtractedLine: lines.FirstOrDefault(),
            ContainsFusedPeriodRole: lines.Any(FusedPeriodRole().IsMatch),
            AnyLineCarriesBothColumns: lines.Any(l => CarriesBothColumns(l, c.Model)),
            ExtractedTextDigest: Digest(o.RawText),
            ParsedFreeSectionHeadings: [.. content?.Sections.Select(x => x.Heading) ?? []],
            ParsedExperience: content?.Experience.Count,
            ParsedEducation: content?.Education.Count,
            GroundTruthExperience: c.Model.GroundTruthEmployments,
            GroundTruthEducation: c.Model.GroundTruthEducations,
            PromotedExperience: promotedContent?.Experiences.Count,
            PromotedEducation: promotedContent?.Educations.Count,
            WellFormedPromotedExperience: wellFormed,
            PromotedPreambleChars: promotedContent?.Preamble?.Length,
            BlockReason: o.BlockReason,
            Promoted: o.Promoted,
            Gates: gates,
            Markers: markers,
            CrossSectionContamination: Contamination(c.Model, content, c.ProjectHeadingRendered),
            // Measured against the heading this case ACTUALLY renders. Measuring every case
            // against the unknown heading made the paired control read "no" unconditionally --
            // a control that cannot fall is not a control.
            SummaryContainsRenderedProjectHeading: c.ProjectHeadingRendered is null
                ? null
                : promotedContent?.Summary?.Contains(
                    c.ProjectHeadingRendered, StringComparison.Ordinal),
            RenderedProjectHeadingIsOwnSection: c.ProjectHeadingRendered is null
                ? null
                : content?.Sections.Any(x => string.Equals(
                    x.Heading, c.ProjectHeadingRendered, StringComparison.Ordinal)),
            PromotedSummaryChars: promotedContent?.Summary?.Length,
            PromoteFailureCode: o.PromoteFailureCode,
            CrashedWithExceptionType: null,
            Verdict: Decide(o, fixtureProblems, promotedContent?.Experiences.Count,
                promotedContent?.Educations.Count, c.Model));
    }

    private static FidelityVerdict Decide(
        CvChainObservation o, IReadOnlyList<string> fixtureProblems,
        int? promotedExperience, int? promotedEducation, CvModel model)
    {
        if (fixtureProblems.Count > 0)
            return FidelityVerdict.FixtureInvalid;
        if (o.ImportFailureCode is not null)
            return FidelityVerdict.ImportFailed;
        if (o.ExtractionStatus != CvExtractionStatus.Extracted)
            return FidelityVerdict.ExtractionFailed;
        if (o.PromoteFailureCode is not null)
            return FidelityVerdict.PromoteFaulted;
        if (!o.Promoted)
            return FidelityVerdict.Blocked;

        var exp = promotedExperience ?? 0;
        var edu = promotedEducation ?? 0;

        if (exp > model.GroundTruthEmployments || edu > model.GroundTruthEducations)
            return FidelityVerdict.PromotedInflated;
        if (exp < model.GroundTruthEmployments || edu < model.GroundTruthEducations)
            return FidelityVerdict.PromotedLossy;

        return FidelityVerdict.PromotedFaithful;
    }

    private static int CountDetectedHeadings(ParsedResumeContent? content)
    {
        if (content is null)
            return 0;

        var count = content.Sections.Count;
        if (content.Experience.Count > 0) count++;
        if (content.Education.Count > 0) count++;
        if (content.Skills.Count > 0) count++;
        if (content.Languages.Count > 0) count++;
        if (!string.IsNullOrWhiteSpace(content.Profile)) count++;
        return count;
    }

    private static IReadOnlyList<string> SectionEvidence(ParsedResume? parsed) =>
        parsed is null
            ? []
            : [.. parsed.Confidence.Sections.Select(s =>
                $"{s.Kind}: {s.Level} — {string.Join("; ", s.Evidence)}")];

    /// <summary>
    /// An authored string turning up in a section that is not its declared home. Measured as
    /// membership of the corpus's OWN declarations, never by re-typing what counts as a language.
    ///
    /// <para>Matching runs from the RECEIVING entry outward, and only against what this case
    /// actually rendered. An earlier revision matched every case against both project headings with
    /// a substring test, and because the known heading is a prefix of the unknown one it reported a
    /// finding for a heading the document never contained. Exact equality is reported as
    /// contamination; a receiving entry that is a proper fragment of an authored project line is
    /// reported AS a fragment, because the list parser atomises prose on commas and an
    /// equality-only sweep would silently miss both halves.</para>
    /// </summary>
    private static List<string> Contamination(
        CvModel model, ParsedResumeContent? content, string? renderedProjectHeading)
    {
        if (content is null)
            return [];

        var authored = new List<string>(model.ProjectLines);
        if (renderedProjectHeading is not null)
            authored.Add(renderedProjectHeading);

        var findings = new List<string>();
        Scan("Languages", content.Languages);
        Scan("Skills", content.Skills);
        return findings;

        void Scan(string section, IReadOnlyList<string> entries)
        {
            foreach (var entry in entries)
            {
                var exact = authored.FirstOrDefault(
                    a => string.Equals(a, entry, StringComparison.Ordinal));
                if (exact is not null)
                {
                    findings.Add($"{section} ← '{Trim(exact)}' (declared home: projects)");
                    continue;
                }

                var fragmentOf = authored.FirstOrDefault(a =>
                    entry.Length > 0 && a.Length > entry.Length
                    && a.Contains(entry, StringComparison.Ordinal));
                if (fragmentOf is not null)
                {
                    findings.Add(
                        $"{section} ← '{Trim(entry)}' — a FRAGMENT of the authored project line "
                        + $"'{Trim(fragmentOf)}' (the list parser atomised it)");
                }
            }
        }
    }

    /// <summary>Does the line carry tokens from BOTH authored columns? The product-side half of
    /// the interleaved case's claim; its byte proof asserts the authored geometry.</summary>
    private static bool CarriesBothColumns(string line, CvModel model) =>
        model.Skills.Any(s => line.Contains(s, StringComparison.Ordinal))
        && (line.Contains(model.Headings.Profile, StringComparison.Ordinal)
            || line.Contains(model.Headings.Experience, StringComparison.Ordinal)
            || model.Employments.Any(e => line.Contains(e.Marker, StringComparison.Ordinal)));

    /// <summary>A short, stable digest of the extracted text. A PER-CASE value, never an
    /// aggregation: two rows sharing a digest is a reader's inference, not an emitted ratio. It is
    /// what lets the table-invisibility twin emit its claim (equal digests = the table changed
    /// nothing) instead of leaving a reader to compare character counts by eye.</summary>
    private static string Digest(string text)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes)[..12];
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^\d{4}\s*-\s*\d{4}\p{L}")]
    private static partial System.Text.RegularExpressions.Regex FusedPeriodRole();

    private static string Trim(string s) => s.Length <= 44 ? s : s[..44] + "…";

    private static LayoutCaseObservation Crashed(
        LayoutCase c, IReadOnlyList<string> fixtureProblems, string exceptionType) =>
        new(c, null, fixtureProblems, false, null, 0, 0, 0, false, null, 0, null, null, [],
            null, null, false, false, string.Empty, [],
            null, null, c.Model.GroundTruthEmployments, c.Model.GroundTruthEducations,
            null, null, null, null, null, false, [], [], [], null, null, null, null,
            exceptionType, FidelityVerdict.Crashed);
}
