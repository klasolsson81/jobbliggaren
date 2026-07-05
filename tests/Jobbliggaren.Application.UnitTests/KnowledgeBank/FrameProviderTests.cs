using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.KnowledgeBank;

/// <summary>
/// Fas 4b PR-5 (ADR 0093 §D3) — the committed frame catalog (frames.v1.json) loads
/// through the real <see cref="FrameProvider"/> (which runs the FULL cross-asset
/// validation against the real verb mapping at construction) and satisfies the §D2
/// contract shape the PR-7 apply-half builds against. The frames are VERSIONED DATA
/// (CLAUDE.md §5), not C# literals.
/// </summary>
public class FrameProviderTests
{
    private static FrameCatalog LoadCatalog() => new FrameProvider(new VerbMapper()).GetFrameCatalog();

    [Fact]
    public void GetFrameCatalog_ShouldLoadVersionedEmbeddedResource_WhenCalled()
    {
        var catalog = LoadCatalog();

        catalog.ShouldNotBeNull();
        catalog.Version.ShouldBe("1.0");
        // The cross-asset pin: the catalog is authored against verb-mapping v1.1 and
        // the loader fails loud on drift (proven in FramesLoaderTests) — here we prove
        // the COMMITTED pair agrees.
        catalog.VerbMappingVersion.ShouldBe(new VerbMapper().GetVerbMapping().Version);
    }

    [Fact]
    public void GetFrameCatalog_ShouldCarryTheFourSentenceFramesAndOneMeasureFrame_WhenCalled()
    {
        // Prototype FRAMES minus "kvalitetssäkrade" (not in verb-mapping v1.1's strong
        // set — adding it is an A2 engine-behaviour change awaiting Klas; see
        // FramesLoader XML doc) + the §6.2 measure frame.
        var catalog = LoadCatalog();

        catalog.Frames.Count.ShouldBe(5);
        catalog.Frames.Count(f => f.Kind == FrameKind.Sentence).ShouldBe(4);
        catalog.Frames.Count(f => f.Kind == FrameKind.Measure).ShouldBe(1);
    }

    [Fact]
    public void GetFrameCatalog_ShouldResolveEverySentenceVerbInTheStrongGroups_WhenCalled()
    {
        // ADR 0093 §D2 invariant (b) made structural at PR-5 time: the apply-half can
        // never meet a frame whose lead verb the knowledge bank does not endorse.
        var strongVerbs = new VerbMapper().GetVerbMapping().StrongVerbGroups
            .SelectMany(g => g.Verbs)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var sentenceFrames = LoadCatalog().Frames
            .Where(f => f.Kind == FrameKind.Sentence)
            .ToList();

        sentenceFrames.ShouldAllBe(f => f.Verb != null && strongVerbs.Contains(f.Verb));
    }

    [Fact]
    public void GetFrameCatalog_ShouldTargetOnlyRealRubricCriteria_WhenCalled()
    {
        // Cross-asset sanity vs the committed rubric: a frame must remedy criteria that
        // actually exist (typo guard — loader checks the SHAPE, this pins the EXISTENCE).
        var rubricIds = new RubricProvider().GetRubric().Criteria
            .Select(c => c.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var frame in LoadCatalog().Frames)
        {
            frame.CriterionIds.ShouldAllBe(id => rubricIds.Contains(id),
                $"frame '{frame.Id}' pekar på ett kriterium som inte finns i rubriken.");
        }
    }

    [Fact]
    public void GetFrameCatalog_ShouldMapSentenceFramesToWeakOpenerCriteria_WhenCalled()
    {
        // Sentence frames remedy A2 (weak openers) / C3 (passive voice); the measure
        // frame remedies A1 (quantification) — handoff §6.2 mechanics 1 + 2.
        var catalog = LoadCatalog();

        foreach (var frame in catalog.Frames.Where(f => f.Kind == FrameKind.Sentence))
        {
            frame.CriterionIds.ShouldBe(["A2", "C3"]);
        }

        catalog.Frames.Single(f => f.Kind == FrameKind.Measure)
            .CriterionIds.ShouldBe(["A1"]);
    }

    [Fact]
    public void GetFrameCatalog_ShouldCarryTheMeasureFrameSlotContract_WhenCalled()
    {
        // §D2 invariant (c) shape: the measure frame binds the user's own echoed number
        // (exactly one number slot in v1) and picks its verb at apply time (exactly one
        // verb slot) — "aldrig påhittade siffror" (handoff §6.2).
        var measure = LoadCatalog().Frames.Single(f => f.Kind == FrameKind.Measure);

        measure.Verb.ShouldBeNull();
        measure.Slots.Count(s => s.Kind == FrameSlotKind.Verb).ShouldBe(1);
        measure.Slots.Count(s => s.Kind == FrameSlotKind.Number).ShouldBe(1);
    }

    [Fact]
    public void GetFrameCatalog_ShouldUseOnlyNounSlotsInSentenceFrames_WhenCalled()
    {
        // §D2 invariant (a) shape: sentence-frame slots are noun slots filled from the
        // cited Before-span — no free-text, no numbers, no verb slots in v1.
        var sentenceFrames = LoadCatalog().Frames.Where(f => f.Kind == FrameKind.Sentence);

        foreach (var frame in sentenceFrames)
        {
            frame.Slots.ShouldAllBe(s => s.Kind == FrameSlotKind.Noun,
                $"frame '{frame.Id}' bär en icke-noun-slot.");
        }
    }

    [Fact]
    public void GetFrameCatalog_ShouldBakeTheCapitalisedLeadVerbIntoEachSentenceTemplate_WhenCalled()
    {
        // The template IS the frame ("samma indata + samma verb = samma utdata,
        // alltid" — §6.2): the fixed lead verb opens the template, capitalised.
        foreach (var frame in LoadCatalog().Frames.Where(f => f.Kind == FrameKind.Sentence))
        {
            var expectedOpener = char.ToUpperInvariant(frame.Verb![0]) + frame.Verb[1..];
            frame.Template.ShouldStartWith(expectedOpener);
        }
    }
}
