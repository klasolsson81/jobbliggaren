namespace Jobbliggaren.QA.Corpus.Generation;

/// <summary>
/// One title→ssyk-4 ground-truth pair, injected into the generator (CTO Fork 2 anti-stale).
/// The generator stays a pure function (testable without a DB in PR 1); the deriver frontend
/// (PR 2) supplies the REAL pairs derived live from the seeded taxonomy tree + the frozen
/// <c>occupation-name-to-ssyk-level-4.v30.json</c> map — never hard-coded.
/// </summary>
/// <param name="OccupationName">The occupation-name label (the title the deriver receives).</param>
/// <param name="ExpectedSsyk4ConceptId">The ssyk-4 occupation-group concept-id the title must resolve to.</param>
public sealed record OccupationGroundTruth(string OccupationName, string ExpectedSsyk4ConceptId);
