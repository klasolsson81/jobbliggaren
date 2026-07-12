namespace Jobbliggaren.Application.UnitTests.Resumes.Rendering;

/// <summary>
/// Serialises every test class that generates a PDF through QuestPDF. QuestPDF's font manager (the
/// glyph-subset cache) is process-global and mutated on each <c>GeneratePdf</c>; two renders on
/// different threads can race on it and occasionally corrupt the output — a dropped glyph then makes a
/// text-fidelity assertion flake intermittently (the byte-size drift is the benign version of the same
/// shared-state hazard the CTO flagged). Placing the renderer classes in one collection makes them run
/// serially relative to each other (xUnit parallelises by collection), and <c>DisableParallelization</c>
/// keeps the collection off the parallel phase entirely — the race is removed deterministically without
/// disabling parallelism for the whole (16k-test) assembly. Cross-namespace: collections match by name.
/// </summary>
[CollectionDefinition("QuestPdfRendering", DisableParallelization = true)]
public sealed class QuestPdfRenderingSerial;
