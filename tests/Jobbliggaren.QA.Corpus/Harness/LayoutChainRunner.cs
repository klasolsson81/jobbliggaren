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
    int? ParsedExperience,
    int? ParsedEducation,
    int GroundTruthExperience,
    int GroundTruthEducation,
    int? PromotedExperience,
    int? PromotedEducation,
    int? WellFormedPromotedExperience,
    AutoPromoteBlockReason? BlockReason,
    bool Promoted,
    IReadOnlyList<GateCell> Gates,
    IReadOnlyList<MarkerTrace> Markers,
    IReadOnlyList<string> CrossSectionContamination,
    bool? SummaryContainsUnknownHeading,
    bool? UnknownHeadingIsOwnSection,
    int? PromotedSummaryChars,
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
internal static class LayoutChainRunner
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

        var o = await CvChainProbe.RunAsync(c.FileName, c.ContentType, bytes, ct);
        if (o.CrashedWithExceptionType is not null)
            return Crashed(c, fixtureProblems, o.CrashedWithExceptionType);

        var parsed = o.Parsed;
        var content = parsed?.Content;
        var promotedContent = o.PromotedResume?.MasterVersion.Content;

        var markers = MarkerTracer
            .Trace(c.Model.EmploymentMarkers, "employment", o.RawText, parsed, o.PromotedResume)
            .Concat(MarkerTracer.Trace(
                c.Model.EducationMarkers, "education", o.RawText, parsed, o.PromotedResume))
            .ToList();

        // The two public calls the handler itself makes at :134 — run here, not re-typed, so the
        // label rung is resolved by observation rather than by inference.
        var label = ResumeLabelResolver.Resolve(null, FixedClock.Default);
        var pnrInLabel = PersonnummerScanner
            .Scan(PersonnummerTextNormalizer.Normalize(label)).Count > 0;

        var gates = GateLadder.From(
            o.BlockReason, o.Promoted,
            pnrFoundOnParse: parsed?.Personnummer.Found ?? false,
            pnrInResolvedLabel: pnrInLabel,
            fixtureCanCarryPersonnummer: c.CarriesPersonnummer);

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
            ParsedExperience: content?.Experience.Count,
            ParsedEducation: content?.Education.Count,
            GroundTruthExperience: c.Model.GroundTruthEmployments,
            GroundTruthEducation: c.Model.GroundTruthEducations,
            PromotedExperience: promotedContent?.Experiences.Count,
            PromotedEducation: promotedContent?.Educations.Count,
            WellFormedPromotedExperience: wellFormed,
            BlockReason: o.BlockReason,
            Promoted: o.Promoted,
            Gates: gates,
            Markers: markers,
            CrossSectionContamination: Contamination(c.Model, content),
            SummaryContainsUnknownHeading: promotedContent?.Summary?
                .Contains(c.Model.Headings.UnknownProjects, StringComparison.Ordinal),
            UnknownHeadingIsOwnSection: content?.Sections.Any(s => string.Equals(
                s.Heading, c.Model.Headings.UnknownProjects, StringComparison.Ordinal)),
            PromotedSummaryChars: promotedContent?.Summary?.Length,
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

    /// <summary>An authored marker turning up in a section that is not its home. Measured as
    /// DECLARED-marker membership, never by re-typing "what counts as a language" — the corpus
    /// asks whether a string it authored as an employer ended up in the language list, which is a
    /// fact about its own declarations.</summary>
    private static List<string> Contamination(CvModel model, ParsedResumeContent? content)
    {
        if (content is null)
            return [];

        var findings = new List<string>();
        var foreign = model.ProjectLines
            .Concat([model.Headings.UnknownProjects, model.Headings.KnownProjects])
            .ToList();

        foreach (var text in foreign)
        {
            if (content.Languages.Any(l => l.Contains(text, StringComparison.Ordinal)))
                findings.Add($"Languages ← '{Trim(text)}' (declared home: projects)");
            if (content.Skills.Any(s => s.Contains(text, StringComparison.Ordinal)))
                findings.Add($"Skills ← '{Trim(text)}' (declared home: projects)");
        }

        return findings;
    }

    private static string Trim(string s) => s.Length <= 44 ? s : s[..44] + "…";

    private static LayoutCaseObservation Crashed(
        LayoutCase c, IReadOnlyList<string> fixtureProblems, string exceptionType) =>
        new(c, null, fixtureProblems, false, null, 0, 0, 0, false, null, 0, null, null, [],
            null, null, c.Model.GroundTruthEmployments, c.Model.GroundTruthEducations,
            null, null, null, null, false, [], [], [], null, null, null,
            exceptionType, FidelityVerdict.Crashed);
}
