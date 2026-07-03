using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.KnowledgeBank;

/// <summary>
/// Fas 4 STEG 7 (F4-7) — the committed CV lexicon (cliche-list.v2.json) loads through the real
/// <see cref="ClicheLexicon"/>. The lexicon is VERSIONED DATA (CLAUDE.md §5: "cliché lists ...
/// versioned data/config per the knowledge bank, not inline strings"). v2 (#490/#495/#496):
/// every entry carries a <see cref="ClicheKind"/> discriminator (one phrase → exactly one
/// criterion — A7 for <see cref="ClicheKind.Cliche"/>, A9 for <see cref="ClicheKind.SoftSkill"/>),
/// an advisory <see cref="ClicheEntry.Guidance"/> field, and an OPTIONAL
/// <see cref="ClicheEntry.DropInReplacement"/> (the ONLY literal the propose step applies verbatim).
///
/// Drift-robust (mirrors VerbMapperTests / TaxonomySnapshotSeederTests): assert a `>=` floor +
/// per-entry structural completeness + a few pinned classifications, NOT a brittle exact total.
/// </summary>
public class ClicheLexiconTests
{
    private static ClicheList LoadList() => new ClicheLexicon().GetClicheList();

    [Fact]
    public void GetClicheList_ShouldLoadVersionedEmbeddedResource_WhenCalled()
    {
        var list = LoadList();

        list.ShouldNotBeNull();
        list.Version.ShouldNotBeNullOrWhiteSpace();
        // Sentinel guard — the committed list must bear a real version, not the loader's
        // default fallback.
        list.Version.ShouldNotBe("unknown");
    }

    [Fact]
    public void GetClicheList_ShouldStampVersionTwo_WhenCalled()
    {
        // Version-pin (#490/#495/#496): the schema bump (kind + guidance + dropInReplacement) rides
        // a file rename cliche-list.v1.json → v2.json + a clicheListVersion bump to "2". Pinned so a
        // stale asset (still "1") or a missed csproj/LogicalName rename fails CI (parity the rubric
        // v1.1.0 rename #488).
        LoadList().Version.ShouldBe("2");
    }

    [Fact]
    public void GetClicheList_ShouldContainAtLeastTwelveEntries_WhenCalled()
    {
        // Drift-robust floor (architect: >=12). Asserted as `>=`, not `==`, so the lexicon can
        // grow without breaking the test.
        LoadList().Entries.Count.ShouldBeGreaterThanOrEqualTo(12);
    }

    [Fact]
    public void GetClicheList_ShouldHavePhraseWhyAndGuidanceNonEmptyOnEveryEntry_WhenCalled()
    {
        // Every entry diagnoses (Why) AND advises (Guidance) — the propose-and-explain contract.
        // An entry with an empty Why or Guidance would be a flag-without-help (rejected by §5).
        LoadList().Entries.ShouldAllBe(e =>
            !string.IsNullOrWhiteSpace(e.Phrase)
            && !string.IsNullOrWhiteSpace(e.Why)
            && !string.IsNullOrWhiteSpace(e.Guidance));
    }

    [Fact]
    public void GetClicheList_ShouldHaveNoEmDashInRenderedFields_WhenCalled()
    {
        // #579 (epic #478 follow-track a): the lexicon's RENDERED fields must not carry an em-dash
        // (U+2014), forbidden in UI copy (CLAUDE.md §5). Rendered = `Why` (ClicheTransform surfaces it
        // as the improvement `ProposedChange.Rationale` → CvImprovementDto → SuggestCvImprovementsQuery)
        // AND a present `DropInReplacement` (applied verbatim as the replacement `After`,
        // ClicheTransform.cs). All drop-ins are null today (see below), so that arm is a forward-safe
        // guarantee. This is the KB-DATA counterpart to the C# source-scan in
        // EmDashInReviewCopyGuardTests, pinned HERE at the lexicon's own invariant home because a
        // source-scan cannot know KB projection semantics. `Guidance` is NOT rendered (surfaced
        // nowhere), so an em-dash there is allowed and deliberately not asserted.
        var offending = LoadList().Entries
            .Where(e => e.Why.Contains('—')
                        || (e.DropInReplacement is not null && e.DropInReplacement.Contains('—')))
            .Select(e => e.Phrase)
            .ToList();

        offending.ShouldBeEmpty(
            "Följande cliché-entries har em-dash (U+2014) i ett RENDERAT fält (`why` visas som " +
            "improvement-rationale; `dropInReplacement` appliceras verbatim) — förbjudet i UI-copy " +
            "(CLAUDE.md §5). Ersätt med kolon/komma/punkt. (`guidance` renderas ej och kontrolleras " +
            "avsiktligt inte.) Träffar: " + string.Join(", ", offending));
    }

    [Fact]
    public void GetClicheList_ShouldHaveUniquePhrases_WhenCalled()
    {
        // No duplicate phrase (case-insensitive) — a duplicate would double-flag the same span and
        // bloat the verdict (and could split a phrase across two kinds).
        var phrases = LoadList().Entries
            .Select(e => e.Phrase.Trim().ToLowerInvariant())
            .ToList();
        phrases.Distinct().Count().ShouldBe(phrases.Count);
    }

    [Fact]
    public void GetClicheList_ShouldNotUseTheGuidanceAsATautologyOfThePhrase_WhenCalled()
    {
        // The Guidance must actually differ from the flagged Phrase — a "replace X with X" entry
        // would be useless.
        LoadList().Entries.ShouldAllBe(e =>
            !string.Equals(e.Phrase.Trim(), e.Guidance.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetClicheList_ShouldCarryBothKinds_WhenCalled()
    {
        // The v2 split is real data: the lexicon carries BOTH empty-buzzword clichés (A7's domain)
        // AND bare personality adjectives (A9's domain). If either kind were empty the split would
        // be inert and one of the two rules would never fire.
        var list = LoadList();

        list.Entries.ShouldContain(e => e.Kind == ClicheKind.Cliche);
        list.Entries.ShouldContain(e => e.Kind == ClicheKind.SoftSkill);
    }

    [Theory]
    // Empty buzzword phrases → A7's anti-cliché domain.
    [InlineData("Brinner för", ClicheKind.Cliche)]
    [InlineData("Resultatorienterad", ClicheKind.Cliche)]
    [InlineData("Self-starter", ClicheKind.Cliche)]
    // Bare personality adjectives → A9's soft-skills domain.
    [InlineData("Social", ClicheKind.SoftSkill)]
    [InlineData("Noggrann", ClicheKind.SoftSkill)]
    [InlineData("Stresstålig", ClicheKind.SoftSkill)]
    public void GetClicheList_ShouldClassifyRepresentativePhrases_WhenCalled(string phrase, ClicheKind expected)
    {
        // Classification drift-guard (#490): a flip of a representative phrase's kind (which would
        // re-introduce the A7/A9 double-punishment or mis-route a phrase) fails CI. Pinned per
        // phrase (not the whole set) so the lexicon can still grow.
        var entry = LoadList().Entries
            .Where(e => string.Equals(e.Phrase, phrase, StringComparison.OrdinalIgnoreCase))
            .ToList()
            .ShouldHaveSingleItem();
        entry.Kind.ShouldBe(expected);
    }

    [Fact]
    public void GetClicheList_ShouldTreatEveryDropInReplacementAsANonEmptyNonTautology_WhenPresent()
    {
        // A present DropInReplacement is a GENUINE same-meaning literal (the only thing the propose
        // step applies verbatim) — never blank and never the phrase itself. A blank one is
        // normalised to null by the loader (so it is not "present" here).
        LoadList().Entries
            .Where(e => e.DropInReplacement is not null)
            .ShouldAllBe(e =>
                !string.IsNullOrWhiteSpace(e.DropInReplacement)
                && !string.Equals(e.Phrase.Trim(), e.DropInReplacement!.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetClicheList_ShouldCarryNoDropInReplacementYet_WhenCalled()
    {
        // Today's authored asset has ZERO genuine drop-ins — the advisory Guidance blends example
        // numbers + meta-instructions that must never be applied verbatim (#495). Pinned so the
        // improve engine's "0 cliché proposals today" behaviour is a data fact, not an accident;
        // the day a curated drop-in is added this test is updated deliberately alongside it.
        LoadList().Entries.ShouldAllBe(e => e.DropInReplacement == null);
    }

    // ── N-1 back-compat: a missing kind/dropInReplacement key maps to a safe CLR default
    //    (house rule: a JSON key → CLR default MUST carry a back-compat test) ─────────────

    [Fact]
    public void ClicheEntryKind_ShouldDefaultToCliche_WhenTheAssetOmitsTheKind()
    {
        // An older asset without `kind` keeps the original anti-cliché-only routing (A7's domain),
        // never silently a soft-skill — fail-safe minimal default.
        KnowledgeBankTokens.ClicheEntryKind(null).ShouldBe(ClicheKind.Cliche);
    }

    [Fact]
    public void ClicheEntryKind_ShouldFailLoud_WhenTheKindTokenIsUnknown()
    {
        // A present-but-unknown token is a typo that must fail loud, never drop to a default.
        Should.Throw<InvalidOperationException>(() => KnowledgeBankTokens.ClicheEntryKind("buzzword"));
    }

    [Fact]
    public void ClicheEntryFile_ShouldDeserialiseWithNullKindAndDropIn_WhenTheAssetOmitsThem()
    {
        // The raw deserialisation form defaults the optional keys to null (skip-unknown/omitted),
        // so the loader's null-mapping (kind → Cliche, dropIn → null) is what an N-1 asset resolves.
        var legacy = System.Text.Json.JsonSerializer.Deserialize<ClicheListFile.ClicheEntryFile>(
            """{ "phrase": "Brinner för", "why": "Tom", "guidance": "Var konkret" }""",
            KnowledgeBankJson.Options);

        legacy.ShouldNotBeNull();
        legacy!.Kind.ShouldBeNull();
        legacy.DropInReplacement.ShouldBeNull();
    }
}
