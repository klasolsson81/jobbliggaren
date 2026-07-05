using Jobbliggaren.Application.KnowledgeBank.Abstractions;

namespace Jobbliggaren.Application.UnitTests.Resumes.Improvement;

/// <summary>
/// Shared builders for the Fas 4b PR-7 frame-apply tests (#656, ADR 0093 §D2/§D3). The two
/// frames mirror the REAL committed <c>frames.v1.json</c> catalog PR-5 shipped (sentence-ledde,
/// measure-antal-per-period) so the FromFrame / grounding / apply tests exercise the same slot
/// shapes production reads — constructed as <see cref="CvFrame"/> records directly (the catalog
/// contract is Application-visible from PR-5), never re-parsed from the asset.
///
/// SPEC-DRIVEN: RED until the PR-7 apply-half (UserParameterizedFrameProvenance,
/// ProposedChange.FromFrame, FrameSlotGrounding, FrameApplyComposer, the apply command +
/// preview query) ships. The frame DATA these builders return already exists (PR-5).
/// </summary>
internal static class FrameFixtures
{
    /// <summary>The verb-mapping version the committed frame catalog is pinned to (frames.v1.json).</summary>
    internal const string VerbMappingVersion = "1.1";

    // A weak A2/C3 opener line whose tokens ground every noun slot of sentence-ledde
    // (kundtjänst / support / butiken / leverans / kvalitet are all word-boundary tokens).
    internal const string WeakLine =
        "Ansvarig för kundtjänst, support, leverans, kvalitet och butiken";

    // The exact string sentence-ledde builds from WeakLine + the LeddeSlots below (first char
    // already capital, so the mechanical first-char upcase is a no-op here).
    internal const string LeddeAfter =
        "Ledde kundtjänst och support i butiken, med ansvar för leverans och kvalitet.";

    // A weak A1 line whose tokens ground the measure frame's noun slots (paket / kollin).
    internal const string MeasureLine = "Skötte paket, kollin och pallar i lagret";

    /// <summary>The sentence-ledde frame: fixed lead verb "ledde", five noun slots (§6.2 sentence mechanic).</summary>
    internal static CvFrame SentenceLedde() =>
        new(
            "sentence-ledde",
            FrameKind.Sentence,
            ["A2", "C3"],
            "ledde",
            [
                new FrameSlot("del1", FrameSlotKind.Noun),
                new FrameSlot("del2", FrameSlotKind.Noun),
                new FrameSlot("kontext", FrameSlotKind.Noun),
                new FrameSlot("del3", FrameSlotKind.Noun),
                new FrameSlot("del4", FrameSlotKind.Noun),
            ],
            "Ledde {del1} och {del2} i {kontext}, med ansvar för {del3} och {del4}.");

    /// <summary>The measure-antal-per-period frame: user-filled verb slot, noun vad/enhet, number antal, free-text period.</summary>
    internal static CvFrame MeasureAntalPerPeriod() =>
        new(
            "measure-antal-per-period",
            FrameKind.Measure,
            ["A1"],
            Verb: null,
            [
                new FrameSlot("verb", FrameSlotKind.Verb),
                new FrameSlot("vad", FrameSlotKind.Noun),
                new FrameSlot("antal", FrameSlotKind.Number),
                new FrameSlot("enhet", FrameSlotKind.Noun),
                new FrameSlot("period", FrameSlotKind.Text),
            ],
            "{verb} {vad} av {antal} {enhet} per {period}.");

    /// <summary>The five sentence-ledde slot inputs grounded in <see cref="WeakLine"/>.</summary>
    internal static Dictionary<string, string> LeddeSlots() =>
        new()
        {
            ["del1"] = "kundtjänst",
            ["del2"] = "support",
            ["kontext"] = "butiken",
            ["del3"] = "leverans",
            ["del4"] = "kvalitet",
        };

    /// <summary>The five measure-antal-per-period slot inputs grounded in <see cref="MeasureLine"/>.</summary>
    internal static Dictionary<string, string> MeasureSlots(
        string verb = "skickade", string antal = "30", string period = "dag") =>
        new()
        {
            ["verb"] = verb,
            ["vad"] = "paket",
            ["antal"] = antal,
            ["enhet"] = "kollin",
            ["period"] = period,
        };

    /// <summary>An OrdinalIgnoreCase strong-verb closure (the FromFrame invariant-b membership set).</summary>
    internal static IReadOnlySet<string> StrongVerbs(params string[] verbs) =>
        new HashSet<string>(verbs, StringComparer.OrdinalIgnoreCase);

    /// <summary>A frame catalog stamped with the committed <see cref="VerbMappingVersion"/>.</summary>
    internal static FrameCatalog Catalog(params CvFrame[] frames) =>
        new("1.0", VerbMappingVersion, frames);

    /// <summary>A verb mapping whose single strong-verb group carries <paramref name="strongVerbs"/>.</summary>
    internal static VerbMapping VerbMappingWith(params string[] strongVerbs) =>
        new(VerbMappingVersion, [new StrongVerbGroup("Ledarskap & ansvar", strongVerbs)], []);
}
