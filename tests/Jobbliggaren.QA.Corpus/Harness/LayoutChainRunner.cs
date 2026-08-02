using Jobbliggaren.Application.Resumes.Abstractions;
using Jobbliggaren.Application.Resumes.Commands.AutoPromoteParsedResume;
using Jobbliggaren.Application.Resumes.Common;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
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

    /// <summary>Promoted with MORE entries than the document ATTRIBUTES. Distinct from lossy, and
    /// equally dishonest.
    /// <para><b>Over-splitting is one mechanism, not the definition.</b> This member went
    /// unexercised until #1060 β-3, and the first row ever to publish it did so by a different
    /// route: the entry COUNT was right — six blocks in, six entries out — and the CV was inflated
    /// because one block naming no employer was promoted as an employment anyway, its organization
    /// fabricated from the period line below it. So the two measured mechanisms are fragments
    /// invented by over-splitting, and a field invented for a block the document never attributed.
    /// Read the row, not the word: a verdict term covering two mechanisms cannot tell you which
    /// one you have.</para></summary>
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

    // RENAMED 2026-07-28, and the rename IS the fix. It was `WellFormedPromotedExperience` and
    // counted `Role && Company && RawPeriod` non-blank — a hand copy of a predicate this corpus's
    // own doctrine forbids copying, and wrong about its subject twice over. The buildability rule
    // REQUIRES Company and Role and only LENGTH-CAPS RawPeriod — the three arms are
    // `ExperienceCompanyRequired`, `ExperienceRoleRequired` and `ExperienceRawPeriodTooLong`, all
    // declared by `ResumeEntryBuildability` since #1060 D3(β-2) and reached from
    // `Resume.ValidateContent`. Anchored by NAME, not by line number: the previous revision cited
    // three `Resume.cs` line numbers and β-2's own refactor silently moved every one of them. So a
    // PROMOTED row the first two conjuncts are true by invariant — every promoted entry already
    // passed that validation. The count reduced to raw-period presence while wearing a validity
    // name, and the baseline shows it: it equalled `Promoted exp` on every row it ever printed.
    // It never discriminated. Naming what it measures removes the copied predicate instead of
    // renaming around it. FALSIFIER, deliberately cheap: if the two conjuncts really are invariant,
    // this PR's diff for the column is header-only — any VALUE change refutes the argument above.
    int? PromotedExperienceWithRawPeriod,

    // #1060 — what the PROMOTED CV holds, not what the parse held. Without this the corpus
    // cannot distinguish "promoted carrying the preamble" from "promoted having dropped it":
    // PreambleChars above reads ParsedResumeContent, so a mutation that discards the carrier
    // on the way to the canonical CV leaves every row byte-identical. Measured — it did.
    int? PromotedPreambleChars,
    AutoPromoteBlockReason? BlockReason,

    // #1060 D3(β) PR 2 — WHICH Domain constraint refused the CV, behind the one token that
    // collapses `CreateFromParsed`'s whole error set. Null on every arm but buildability and on
    // every promote; `BlockDetailUnreadable` is the separate instrument fact, never folded in.
    string? DomainErrorCode,
    bool BlockDetailUnreadable,
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
            // The variable, not `null`: it is provably null here (the proof below has not run),
            // and passing it keeps both call sites uniform and documents WHY it is null.
            return Crashed(c, fixtureProblems, ex.GetType().Name, byteProofFailure);
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
        // The fourth argument is what stops a case whose bytes were already wrong, and which then
        // crashed, from being published under "byte proofs held" with its message discarded.
        //
        // It has NO DEFAULT, deliberately. With one, dropping this argument compiled and silently
        // restored the defect — measured: that mutation survived the whole suite. Without one it is
        // a build error, which moves the defect from detectable to UNREPRESENTABLE. Same move as
        // the shape-based whitelist in `GateLadder.IsWellFormed`: close the class by construction
        // rather than guard the instance.
        //
        // No test drives THIS line, and the reason is not that such a fixture is impossible — one
        // could author bytes that fail their own proof and then crash the real extractor, faking
        // nothing. It is that the test's premise would be "the extractor THROWS on these bytes"
        // rather than degrading, and §9 records that this corpus deliberately authors no
        // pathological bytes. Harden the extractor to degrade and that test goes red for a reason
        // unrelated to the seam it guards — a suite blocking its own remedy, which is precisely
        // what the assert rule exists to prevent. `Crashed`'s own contract is pinned by
        // `Crashed_CarriesTheByteProofFailureItWasGiven`.
        if (o.CrashedWithExceptionType is not null)
            return Crashed(c, fixtureProblems, o.CrashedWithExceptionType, byteProofFailure);

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

        var withRawPeriod = promotedContent is null
            ? null
            : (int?)CountWithRawPeriod(promotedContent.Experiences);

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
            PromotedExperienceWithRawPeriod: withRawPeriod,
            PromotedPreambleChars: promotedContent?.Preamble?.Length,
            BlockReason: o.BlockReason,
            DomainErrorCode: o.DomainErrorCode,
            BlockDetailUnreadable: o.BlockDetailUnreadable,
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

    /// <summary>Raw-period presence, and ONLY that. Lifted out of <c>RunAsync</c> so it is callable
    /// — inline, it was a corpus-authored predicate that no mutation could reach, because the suite
    /// asserts no promoted count and the artifact is the only place it shows. It is
    /// the corpus's OWN declaration (assert-rule category (b)), so pinning it is legitimate and
    /// costs the observe-only rule nothing.
    ///
    /// <para>The Role/Company conjuncts this once also tested are gone: <c>ValidateContent</c>
    /// REQUIRES both, so on a promoted entry they are true by invariant and the count was
    /// period-presence wearing a validity name.</para></summary>
    internal static int CountWithRawPeriod(IEnumerable<Experience> experiences) =>
        experiences.Count(e => !string.IsNullOrWhiteSpace(e.RawPeriod));

    private static string Trim(string s) => s.Length <= 44 ? s : s[..44] + "…";

    /// <summary>An observation for a case whose chain never completed.
    ///
    /// <para><paramref name="byteProofFailure"/> is a PARAMETER and not a hardcoded null, because
    /// the second crash exit runs AFTER the byte proof has been evaluated: a case whose authored
    /// bytes were already wrong and which then crashed was published under "byte proofs held" with
    /// its failure message discarded. §0 named it among the healthy.</para></summary>
    internal static LayoutCaseObservation Crashed(
        LayoutCase c, IReadOnlyList<string> fixtureProblems, string exceptionType,
        string? byteProofFailure) =>
        // Named, not positional. The record has 41 parameters (39 before #1060 D3(β) PR 2 added
        // DomainErrorCode and BlockDetailUnreadable) and positions 21-28 are eight consecutive
        // int/int? — ParsedExperience, ParsedEducation, the two ground truths, the two promoted
        // counts, PromotedExperienceWithRawPeriod, PromotedPreambleChars — with an implicit
        // int -> int? conversion between them. Inserting one more in that run shifts every
        // following nullable-int SILENTLY and the compiler accepts it. A corpus that reports
        // promoted-education under "promoted experience" is the exact class of quiet content
        // loss this instrument exists to measure.
        //
        // The same hazard exists on the bool runs, and PR 2 created the THIRD of them, not the
        // second: 17-18 (ContainsFusedPeriodRole, AnyLineCarriesBothColumns), 36-37
        // (SummaryContainsRenderedProjectHeading, RenderedProjectHeadingIsOwnSection — both
        // pre-existing), and now 31-32 (BlockDetailUnreadable, Promoted). Both numbers in that
        // last pair were wrong when first written (32-33, "the second"), and TWO reviewers
        // measured it independently — the CTO bind then upheld both halves, which is a third
        // reading but not a third measurement: position 33 is `Gates`. Naming arguments closes
        // the runs named above AND every other type-COMPATIBLE adjacency in the record (the
        // hazard is the implicit int -> int? conversion, not strict type equality) — 6-8 are three
        // `int`s, 11-12 an `int` beside an `int?`, 39-40 two `string?`s, and this list is an
        // EXAMPLE rather than a census, which is the point: that is why the rule is "named at
        // every construction site" rather than a note about any one block.
        new(
            Case: c,
            ByteProofFailure: byteProofFailure,
            FixtureProblems: fixtureProblems,
            KindResolved: false,
            ExtractionStatus: null,
            CharCount: 0,
            LineCount: 0,
            BlankLineCount: 0,
            SegmentRan: false,
            DetectedLanguage: null,
            HeadingsDetected: 0,
            PreambleChars: null,
            ConfidenceOverall: null,
            SectionEvidence: [],
            PersonnummerFoundOnParse: null,
            FirstExtractedLine: null,
            ContainsFusedPeriodRole: false,
            AnyLineCarriesBothColumns: false,
            ExtractedTextDigest: string.Empty,
            ParsedFreeSectionHeadings: [],
            ParsedExperience: null,
            ParsedEducation: null,
            GroundTruthExperience: c.Model.GroundTruthEmployments,
            GroundTruthEducation: c.Model.GroundTruthEducations,
            PromotedExperience: null,
            PromotedEducation: null,
            PromotedExperienceWithRawPeriod: null,
            PromotedPreambleChars: null,
            BlockReason: null,
            // A crash reached no gate, so there is no block and nothing to read: no code, and
            // the reading did not fail — it was never attempted.
            DomainErrorCode: null,
            BlockDetailUnreadable: false,
            Promoted: false,
            Gates: [],
            Markers: [],
            CrossSectionContamination: [],
            SummaryContainsRenderedProjectHeading: null,
            RenderedProjectHeadingIsOwnSection: null,
            PromotedSummaryChars: null,
            PromoteFailureCode: null,
            CrashedWithExceptionType: exceptionType,
            Verdict: FidelityVerdict.Crashed);
}
