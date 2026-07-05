using System.Text;
using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.KnowledgeBank;

/// <summary>
/// Fas 4b PR-5 — <see cref="FramesLoader"/> fail-loud validation + forward-compat, via
/// the same <c>LoadFrom(Stream, ...)</c> seam the rubric N-1 tests use (synthetic
/// fixtures drive the REAL parse+validate path, never a parallel one). Every §D2-shape
/// invariant the loader enforces has a RED case here: a malformed frames asset can
/// never reach the PR-7 apply-half.
/// </summary>
public class FramesLoaderTests
{
    private static readonly VerbMapping Mapping = new(
        "1.1",
        [new StrongVerbGroup("Ledarskap & ansvar", ["ledde", "ansvarade för"])],
        []);

    private static FrameCatalog Load(string json, VerbMapping? mapping = null)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return FramesLoader.LoadFrom(stream, mapping ?? Mapping);
    }

    private static string ValidCatalog(
        string framesVersion = "1.0",
        string verbMappingVersion = "1.1",
        string frames = """
            [
              {
                "id": "sentence-ledde",
                "kind": "sentence",
                "criterionIds": ["A2", "C3"],
                "verb": "ledde",
                "slots": [
                  { "name": "del1", "kind": "noun" },
                  { "name": "kontext", "kind": "noun" }
                ],
                "template": "Ledde {del1} i {kontext}."
              }
            ]
            """) =>
        $$"""
        {
          "framesVersion": "{{framesVersion}}",
          "verbMappingVersion": "{{verbMappingVersion}}",
          "frames": {{frames}}
        }
        """;

    // ───────────────────────────────────────────────────────────────────
    // Happy path + forward-compat (the N-1 discipline for a v1 asset)
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void LoadFrom_ShouldLoadAValidCatalog_WhenGivenTheMinimalShape()
    {
        var catalog = Load(ValidCatalog());

        catalog.Version.ShouldBe("1.0");
        catalog.VerbMappingVersion.ShouldBe("1.1");
        var frame = catalog.Frames.ShouldHaveSingleItem();
        frame.Kind.ShouldBe(FrameKind.Sentence);
        frame.Verb.ShouldBe("ledde");
        frame.Slots.Select(s => s.Name).ShouldBe(["del1", "kontext"]);
    }

    [Fact]
    public void LoadFrom_ShouldIgnoreUnknownMembers_WhenAFutureAssetAddsFields()
    {
        // Forward-compat parity with the rubric N-1 tests: default STJ skips unknown
        // members, so a v1.1 frames asset with new fields still loads on this reader.
        var json = """
        {
          "framesVersion": "1.0",
          "verbMappingVersion": "1.1",
          "futureTopLevel": { "x": 1 },
          "frames": [
            {
              "id": "sentence-ledde",
              "kind": "sentence",
              "criterionIds": ["A2"],
              "verb": "ledde",
              "futurePerFrame": "y",
              "slots": [ { "name": "del1", "kind": "noun", "futurePerSlot": true } ],
              "template": "Ledde {del1}."
            }
          ]
        }
        """;

        var catalog = Load(json);

        catalog.Frames.ShouldHaveSingleItem().Verb.ShouldBe("ledde");
    }

    // ───────────────────────────────────────────────────────────────────
    // Fail-loud paths — one RED case per structural invariant
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public void LoadFrom_ShouldThrow_WhenVerbMappingVersionDoesNotMatchTheLoadedMapping()
    {
        // The §D2 "verb list at a specific version" contract made literal: a verb-list
        // bump without a frames re-validation fails the host at startup.
        var act = () => Load(ValidCatalog(verbMappingVersion: "1.0"));

        Should.Throw<InvalidOperationException>(act)
            .Message.ShouldContain("verb-mapping");
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenASentenceVerbDoesNotResolveInTheStrongGroups()
    {
        // The prototype's "kvalitetssäkrade" gap made structural: an unendorsed lead
        // verb is a cross-asset drift, never silently shipped.
        var frames = """
            [
              {
                "id": "sentence-x",
                "kind": "sentence",
                "criterionIds": ["A2"],
                "verb": "kvalitetssäkrade",
                "slots": [ { "name": "del1", "kind": "noun" } ],
                "template": "Kvalitetssäkrade {del1}."
              }
            ]
            """;

        var act = () => Load(ValidCatalog(frames: frames));

        Should.Throw<InvalidOperationException>(act)
            .Message.ShouldContain("kvalitetssäkrade");
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenTemplatePlaceholdersDoNotMatchDeclaredSlots()
    {
        var frames = """
            [
              {
                "id": "sentence-ledde",
                "kind": "sentence",
                "criterionIds": ["A2"],
                "verb": "ledde",
                "slots": [ { "name": "del1", "kind": "noun" } ],
                "template": "Ledde {del1} i {kontext}."
              }
            ]
            """;

        var act = () => Load(ValidCatalog(frames: frames));

        Should.Throw<InvalidOperationException>(act)
            .Message.ShouldContain("arity");
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenAPlaceholderRepeats()
    {
        var frames = """
            [
              {
                "id": "sentence-ledde",
                "kind": "sentence",
                "criterionIds": ["A2"],
                "verb": "ledde",
                "slots": [ { "name": "del1", "kind": "noun" } ],
                "template": "Ledde {del1} och {del1}."
              }
            ]
            """;

        var act = () => Load(ValidCatalog(frames: frames));

        Should.Throw<InvalidOperationException>(act)
            .Message.ShouldContain("mer än en gång");
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenFrameIdsAreDuplicated()
    {
        var frames = """
            [
              {
                "id": "sentence-ledde",
                "kind": "sentence",
                "criterionIds": ["A2"],
                "verb": "ledde",
                "slots": [ { "name": "del1", "kind": "noun" } ],
                "template": "Ledde {del1}."
              },
              {
                "id": "sentence-ledde",
                "kind": "sentence",
                "criterionIds": ["C3"],
                "verb": "ansvarade för",
                "slots": [ { "name": "del2", "kind": "noun" } ],
                "template": "Ansvarade för {del2}."
              }
            ]
            """;

        var act = () => Load(ValidCatalog(frames: frames));

        Should.Throw<InvalidOperationException>(act)
            .Message.ShouldContain("duplicerat");
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenKindTokenIsUnknown()
    {
        var frames = """
            [
              {
                "id": "x",
                "kind": "paragraph",
                "criterionIds": ["A2"],
                "verb": "ledde",
                "slots": [ { "name": "del1", "kind": "noun" } ],
                "template": "Ledde {del1}."
              }
            ]
            """;

        var act = () => Load(ValidCatalog(frames: frames));

        Should.Throw<InvalidOperationException>(act)
            .Message.ShouldContain("kind-token");
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenSlotKindTokenIsUnknown()
    {
        var frames = """
            [
              {
                "id": "x",
                "kind": "sentence",
                "criterionIds": ["A2"],
                "verb": "ledde",
                "slots": [ { "name": "del1", "kind": "adjective" } ],
                "template": "Ledde {del1}."
              }
            ]
            """;

        var act = () => Load(ValidCatalog(frames: frames));

        Should.Throw<InvalidOperationException>(act)
            .Message.ShouldContain("adjective");
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenCriterionIdHasTheWrongShape()
    {
        var frames = """
            [
              {
                "id": "x",
                "kind": "sentence",
                "criterionIds": ["F1"],
                "verb": "ledde",
                "slots": [ { "name": "del1", "kind": "noun" } ],
                "template": "Ledde {del1}."
              }
            ]
            """;

        var act = () => Load(ValidCatalog(frames: frames));

        Should.Throw<InvalidOperationException>(act)
            .Message.ShouldContain("rubrik-id");
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenAMeasureFrameCarriesAFixedVerb()
    {
        var frames = """
            [
              {
                "id": "measure-x",
                "kind": "measure",
                "criterionIds": ["A1"],
                "verb": "ledde",
                "slots": [
                  { "name": "verb", "kind": "verb" },
                  { "name": "antal", "kind": "number" }
                ],
                "template": "{verb} {antal}."
              }
            ]
            """;

        var act = () => Load(ValidCatalog(frames: frames));

        Should.Throw<InvalidOperationException>(act)
            .Message.ShouldContain("measure-frame");
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenAMeasureFrameHasNoNumberSlot()
    {
        // "Aldrig påhittade siffror" (handoff §6.2): the measure mechanic IS the user's
        // own number — a measure frame without a number slot is a contradiction.
        var frames = """
            [
              {
                "id": "measure-x",
                "kind": "measure",
                "criterionIds": ["A1"],
                "verb": null,
                "slots": [
                  { "name": "verb", "kind": "verb" },
                  { "name": "vad", "kind": "noun" }
                ],
                "template": "{verb} {vad}."
              }
            ]
            """;

        var act = () => Load(ValidCatalog(frames: frames));

        Should.Throw<InvalidOperationException>(act)
            .Message.ShouldContain("number-slot");
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenASentenceFrameCarriesANumberSlot()
    {
        var frames = """
            [
              {
                "id": "sentence-x",
                "kind": "sentence",
                "criterionIds": ["A2"],
                "verb": "ledde",
                "slots": [
                  { "name": "del1", "kind": "noun" },
                  { "name": "antal", "kind": "number" }
                ],
                "template": "Ledde {del1} med {antal}."
              }
            ]
            """;

        var act = () => Load(ValidCatalog(frames: frames));

        Should.Throw<InvalidOperationException>(act)
            .Message.ShouldContain("number-slots");
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenTheCatalogHasNoFrames()
    {
        var act = () => Load(ValidCatalog(frames: "[]"));

        Should.Throw<InvalidOperationException>(act)
            .Message.ShouldContain("inga frames");
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenSlotNameIsNotALowercaseIdentifier()
    {
        var frames = """
            [
              {
                "id": "sentence-x",
                "kind": "sentence",
                "criterionIds": ["A2"],
                "verb": "ledde",
                "slots": [ { "name": "Del1", "kind": "noun" } ],
                "template": "Ledde {Del1}."
              }
            ]
            """;

        var act = () => Load(ValidCatalog(frames: frames));

        Should.Throw<InvalidOperationException>(act)
            .Message.ShouldContain("gemen identifierare");
    }

    [Fact]
    public void LoadFrom_ShouldThrow_WhenTemplateHasUnbalancedBraces()
    {
        var frames = """
            [
              {
                "id": "sentence-x",
                "kind": "sentence",
                "criterionIds": ["A2"],
                "verb": "ledde",
                "slots": [ { "name": "del1", "kind": "noun" } ],
                "template": "Ledde {del1} i {."
              }
            ]
            """;

        var act = () => Load(ValidCatalog(frames: frames));

        Should.Throw<InvalidOperationException>(act)
            .Message.ShouldContain("klammerparenteser");
    }
}
