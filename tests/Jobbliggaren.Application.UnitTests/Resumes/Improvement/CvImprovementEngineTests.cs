using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Improvement;
using Shouldly;
using static Jobbliggaren.Application.UnitTests.Resumes.Improvement.CvImprovementFixtures;

namespace Jobbliggaren.Application.UnitTests.Resumes.Improvement;

/// <summary>
/// Fas 4 STEG 10 (F4-10, ADR 0071/0074) Phase A — the deterministic CV-build/improve engine.
/// NO AI/LLM: every proposed change is a rule over the parsed CV + the versioned knowledge
/// bank, carrying the KB-resolved replacement and its provenance (CLAUDE.md §5: "applying a
/// CV change without an explicit propose-and-approve diff" and "synthesising prose the user
/// did not write" are forbidden — the engine diagnoses and structures, never invents). NO
/// QuestPDF: Phase A is the BCL-only engine + contracts; the IDocument renderer is Phase B.
///
/// Golden expectations come from the REAL committed assets via the real loaders
/// (<c>cliche-list.v1.json</c> / <c>verb-mapping.v1.json</c> / <c>rubric.v1.0.1.json</c>), so
/// the <c>After</c>-text can never drift from the data the engine actually reads.
///
/// The internal sealed <see cref="CvImprovementEngine"/> is constructed directly
/// (Infrastructure exposes internals to this assembly, parity CvReviewEngineTests). The
/// engine takes (IClicheLexicon, IVerbMapper, IRubricProvider, ITextAnalyzer) — NO AppDbContext,
/// NO ILogger (architect-bound surface).
///
/// Coverage map (each success + each failure/edge case per CLAUDE.md §7) — all 9
/// <see cref="ProposedChangeKind"/> members:
///   - per transform: a CV that TRIGGERS it → the emitted ProposedChange (Kind, evidence
///     channel, Before==quote, After==exact KB value, Provenance arm + Key, rationale source);
///   - per transform: a CV that does NOT trigger it → zero changes (honest, no fabricated edit);
///   - PhotoStrip + SectionReorder → ALWAYS zero changes in v1 (signal absent / no
///     rubric-recommended-order source — honest "not assessed v1", never fabricated);
///   - determinism (same input twice → identical ordered output);
///   - version stamping + profile echo;
///   - language dispatch (Swedish + English both wired for WeakVerb);
///   - review null vs supplied → CriterionId null vs populated.
///
/// RED until ICvImprovementEngine + the result/change types ship in Application and
/// CvImprovementEngine ships internal sealed in Jobbliggaren.Infrastructure.Resumes.Improvement.
/// </summary>
public class CvImprovementEngineTests
{
    private static CvImprovementEngine NewEngine() =>
        new(RealClicheLexicon(), RealVerbMapper(), RealRubricProvider(), Analyzer());

    private static async Task<CvImprovementResult> SuggestAsync(
        ParsedResume resume,
        CvReviewResult? review = null,
        RenderProfile profile = RenderProfile.Ats) =>
        await NewEngine().SuggestAsync(resume, review, profile, TestContext.Current.CancellationToken);

    // ===============================================================
    // 0. Result envelope — version stamped, profile echoed (no opaque total)
    // ===============================================================

    [Fact]
    public async Task SuggestAsync_ShouldStampKnowledgeBankVersionsAndProfile_WhenCalled()
    {
        var result = await SuggestAsync(Resume(), profile: RenderProfile.Ats);

        result.ClicheListVersion.ShouldBe(RealClicheList().Version);
        result.VerbMappingVersion.ShouldBe(RealVerbMapping().Version);
        result.RubricVersion.ShouldBe(RealRubric().Version);
        result.Profile.ShouldBe(RenderProfile.Ats);
    }

    [Fact]
    public async Task SuggestAsync_ShouldEchoVisualProfile_WhenProfileIsVisual()
    {
        var result = await SuggestAsync(Resume(), profile: RenderProfile.Visual);

        result.Profile.ShouldBe(RenderProfile.Visual);
    }

    [Fact]
    public async Task SuggestAsync_ShouldProposeNothing_WhenCvIsAlreadyClean()
    {
        // The default fixture CV is clean (no clichés, strong verbs, normalized dates,
        // standard headings, no personnummer/GPA). A clean CV yields ZERO changes — the
        // engine never fabricates an edit (CLAUDE.md §5).
        var result = await SuggestAsync(Resume());

        result.Changes.ShouldBeEmpty(
            "Ett redan rent CV ska ge noll föreslagna ändringar (ingen fabricerad redigering).");
    }

    // ===============================================================
    // 1. ClicheReplacement (A7) — KnowledgeBank arm, TextSpan evidence
    // ===============================================================

    [Fact]
    public async Task SuggestAsync_ShouldProposeClicheReplacement_WhenProfileContainsACliche()
    {
        // Use a REAL cliché phrase + its EXACT BetterAlternative from cliche-list.v1.json.
        var entry = RealClicheList().Entries[0]; // "Brinner för"
        var resume = Resume(profile: $"{entry.Phrase} kvalitet och samarbete.");

        var change = Single(await SuggestAsync(resume), ProposedChangeKind.ClicheReplacement);

        change.Kind.ShouldBe(ProposedChangeKind.ClicheReplacement);
        change.Category.ShouldBe(RubricCategory.Content);
        change.Evidence.ShouldBeOfType<TextSpanEvidence>(
            "A7-klyscha citerar CV-spannet (TextSpanEvidence).");
        change.Replacement.ShouldNotBeNull();
        change.Replacement!.Before.ShouldBe(entry.Phrase,
            "Before ska vara den citerade klyschan ordagrant.");
        change.Replacement.After.ShouldBe(entry.BetterAlternative,
            "After ska vara EXAKT BetterAlternative ur cliche-list.v1.json (ingen syntes).");
        change.Operation.ShouldBeNull("En KB-ersättning bär ingen StructuralOperation.");

        var kb = change.Provenance.ShouldBeOfType<KnowledgeBankProvenance>();
        kb.Source.ShouldNotBeNullOrWhiteSpace();
        kb.Version.ShouldBe(RealClicheList().Version);
        kb.Key.ShouldBe(entry.Phrase,
            "Provenance.Key ska peka på källfrasen i kunskapsbanken.");
        change.Rationale.ShouldBe(entry.Why,
            "Rationale ska komma från KB-entryns Why-fält (ingen påhittad motivering).");
    }

    [Fact]
    public async Task SuggestAsync_ShouldNotProposeClicheReplacement_WhenProfileHasNoCliche()
    {
        var resume = Resume(profile:
            "Backend-utvecklare med 8 års erfarenhet av betalsystem. Migrerade 3 plattformar 2024.");

        Of(await SuggestAsync(resume), ProposedChangeKind.ClicheReplacement).ShouldBeEmpty();
    }

    // ===============================================================
    // 2. WeakVerbUpgrade (A2/C3) — KnowledgeBank arm, TextSpan, NLP path
    // ===============================================================

    [Fact]
    public async Task SuggestAsync_ShouldProposeWeakVerbUpgrade_WhenBulletStartsWithAWeakVerb()
    {
        // Use a REAL weak verb + its EXACT SuggestedStrong from verb-mapping.v1.json.
        var mapping = RealVerbMapping().WeakVerbs[0]; // "var ansvarig för" → "ansvarade för"
        var resume = Resume(experience:
        [
            Experience(rawText: $"{Capitalize(mapping.Weak)} ett område utan tydligt resultat."),
        ]);

        var change = Single(await SuggestAsync(resume), ProposedChangeKind.WeakVerbUpgrade);

        change.Category.ShouldBeOneOf(RubricCategory.Content, RubricCategory.Language);
        change.Evidence.ShouldBeOfType<TextSpanEvidence>(
            "A2/C3-svagt-verb citerar bullet-spannet (TextSpanEvidence).");
        change.Replacement.ShouldNotBeNull();
        change.Replacement!.After.ShouldBe(mapping.SuggestedStrong,
            "After ska vara EXAKT SuggestedStrong ur verb-mapping.v1.json (ingen syntes).");

        var kb = change.Provenance.ShouldBeOfType<KnowledgeBankProvenance>();
        kb.Version.ShouldBe(RealVerbMapping().Version);
        kb.Key.ShouldBe(mapping.Weak);
    }

    [Fact]
    public async Task SuggestAsync_ShouldNotProposeWeakVerbUpgrade_WhenBulletStartsWithAStrongVerb()
    {
        var strong = RealVerbMapping().StrongVerbGroups[0].Verbs[0]; // "ledde"
        var resume = Resume(experience:
        [
            Experience(rawText: $"{Capitalize(strong)} teamet om 8 personer med tydligt resultat 2024."),
        ]);

        Of(await SuggestAsync(resume), ProposedChangeKind.WeakVerbUpgrade).ShouldBeEmpty();
    }

    // ===============================================================
    // 3. DateNormalization (B6) — Structural arm ReformatDate, TextSpan on period
    // ===============================================================

    [Fact]
    public async Task SuggestAsync_ShouldProposeDateNormalization_WhenPeriodFormatIsInconsistent()
    {
        // Mixed/non-canonical period formats trigger a ReformatDate structural transform.
        var resume = Resume(experience:
        [
            Experience(period: "jan 2022 - juni 2024", rawText: "Backend-utvecklare jan 2022 - juni 2024"),
        ]);

        var change = Single(await SuggestAsync(resume), ProposedChangeKind.DateNormalization);

        change.Kind.ShouldBe(ProposedChangeKind.DateNormalization);
        change.Category.ShouldBe(RubricCategory.Structure);
        change.Evidence.ShouldBeOfType<TextSpanEvidence>(
            "DateNormalization citerar periodspannet i CV-texten (TextSpanEvidence).");
        change.Operation.ShouldNotBeNull();
        change.Operation!.Kind.ShouldBe(StructuralTransformKind.ReformatDate);

        var prov = change.Provenance.ShouldBeOfType<StructuralTransformProvenance>();
        prov.Transform.ShouldBe(StructuralTransformKind.ReformatDate);
    }

    [Fact]
    public async Task SuggestAsync_ShouldNotProposeDateNormalization_WhenPeriodsAreAlreadyCanonical()
    {
        var resume = Resume(experience:
        [
            Experience(period: "01/2022 – 06/2024", rawText: "Backend-utvecklare 01/2022 – 06/2024"),
        ]);

        Of(await SuggestAsync(resume), ProposedChangeKind.DateNormalization).ShouldBeEmpty();
    }

    // ===============================================================
    // 4. GpaStrip — Structural arm RemoveGpa, TextSpan evidence
    // ===============================================================

    [Fact]
    public async Task SuggestAsync_ShouldProposeGpaStrip_WhenEducationCitesAGpa()
    {
        var resume = Resume(education:
        [
            Education(rawText: "KTH, Civilingenjör, 2016–2021, GPA 4.0/5.0"),
        ]);

        var change = Single(await SuggestAsync(resume), ProposedChangeKind.GpaStrip);

        change.Kind.ShouldBe(ProposedChangeKind.GpaStrip);
        change.Evidence.ShouldBeOfType<TextSpanEvidence>(
            "GpaStrip citerar GPA-spannet i utbildningstexten (TextSpanEvidence).");
        change.Operation.ShouldNotBeNull();
        change.Operation!.Kind.ShouldBe(StructuralTransformKind.RemoveGpa);

        var prov = change.Provenance.ShouldBeOfType<StructuralTransformProvenance>();
        prov.Transform.ShouldBe(StructuralTransformKind.RemoveGpa);
    }

    [Fact]
    public async Task SuggestAsync_ShouldNotProposeGpaStrip_WhenEducationHasNoGpa()
    {
        var resume = Resume(education: [Education(rawText: "KTH, Civilingenjör, 2016–2021")]);

        Of(await SuggestAsync(resume), ProposedChangeKind.GpaStrip).ShouldBeEmpty();
    }

    // ===============================================================
    // 5. AtsSanitization — ATS-profile-only; StripNonStandardChars; never strips åäö
    // ===============================================================

    [Fact]
    public async Task SuggestAsync_ShouldProposeAtsSanitization_WhenRawTextHasNonStandardChars()
    {
        // Non-standard glyphs (bullet ornaments / smart symbols) that an ATS parser mangles.
        var resume = Resume(rawText: "Anna Andersson\n★ Ledde teamet ▪ drev projekt → 2024.");

        var change = Single(
            await SuggestAsync(resume, profile: RenderProfile.Ats),
            ProposedChangeKind.AtsSanitization);

        change.Kind.ShouldBe(ProposedChangeKind.AtsSanitization);
        change.Category.ShouldBe(RubricCategory.AtsParsability);
        change.Evidence.ShouldBeOfType<TextSpanEvidence>();
        change.Operation.ShouldNotBeNull();
        change.Operation!.Kind.ShouldBe(StructuralTransformKind.StripNonStandardChars);

        var prov = change.Provenance.ShouldBeOfType<StructuralTransformProvenance>();
        prov.Transform.ShouldBe(StructuralTransformKind.StripNonStandardChars);
    }

    [Fact]
    public async Task SuggestAsync_ShouldNotStripSwedishLetters_WhenSanitizingForAts()
    {
        // åäö (and ÅÄÖ) are valid CV content, NOT "non-standard" — sanitization must never
        // touch them (honest: it removes ornaments, never the user's words, CLAUDE.md §5).
        var resume = Resume(rawText: "Förändrade arbetssättet på avdelningen för kärnverksamheten.");

        var changes = Of(await SuggestAsync(resume, profile: RenderProfile.Ats),
            ProposedChangeKind.AtsSanitization);

        changes.ShouldBeEmpty(
            "Sanering får aldrig flagga åäö som icke-standard — det är giltigt CV-innehåll.");
    }

    [Fact]
    public async Task SuggestAsync_ShouldNotProposeAtsSanitization_WhenProfileIsVisual()
    {
        // AtsSanitization is an ATS-profile-only concern; the Visual profile never emits it.
        var resume = Resume(rawText: "Anna Andersson\n★ Ledde teamet ▪ drev projekt → 2024.");

        Of(await SuggestAsync(resume, profile: RenderProfile.Visual),
            ProposedChangeKind.AtsSanitization).ShouldBeEmpty();
    }

    // ===============================================================
    // 6. PersonnummerStrip (B4) — Structural RemovePersonnummer;
    //    StructuralEvidence with count only (never value/offset); Replacement null
    // ===============================================================

    [Fact]
    public async Task SuggestAsync_ShouldProposePersonnummerStrip_WhenPersonnummerFlagged()
    {
        var flagged = PersonnummerScanOutcome.FromMatches(
            PersonnummerScanner.Scan("Personnummer 811218-9876 i CV."));
        var resume = Resume(personnummer: flagged);

        var change = Single(await SuggestAsync(resume), ProposedChangeKind.PersonnummerStrip);

        change.Kind.ShouldBe(ProposedChangeKind.PersonnummerStrip);
        change.Category.ShouldBe(RubricCategory.Structure);
        change.Operation.ShouldNotBeNull();
        change.Operation!.Kind.ShouldBe(StructuralTransformKind.RemovePersonnummer);

        // A pure removal — no before/after replacement text (nothing to quote without
        // echoing the raw personnummer, Inv.1).
        change.Replacement.ShouldBeNull(
            "PersonnummerStrip är en ren borttagning — ingen ProposedReplacement.");

        // Evidence is the PII-safe count/structure, NEVER the raw value or an offset (Inv.1).
        var evidence = change.Evidence.ShouldBeOfType<StructuralEvidence>(
            "B4 citerar antalet strukturellt — aldrig råvärdet eller offset (Inv.1).");
        evidence.Observation.ShouldNotContain("811218-9876", Case.Sensitive,
            "Personnummer-evidensen får aldrig eka råvärdet.");

        change.Provenance.ShouldBeOfType<StructuralTransformProvenance>()
            .Transform.ShouldBe(StructuralTransformKind.RemovePersonnummer);
    }

    [Fact]
    public async Task SuggestAsync_ShouldNotProposePersonnummerStrip_WhenNoPersonnummerFound()
    {
        Of(await SuggestAsync(Resume(personnummer: PersonnummerScanOutcome.None)),
            ProposedChangeKind.PersonnummerStrip).ShouldBeEmpty();
    }

    // ===============================================================
    // 7. HeadingNormalization — Structural NormalizeHeadingCase
    // ===============================================================

    [Fact]
    public async Task SuggestAsync_ShouldProposeHeadingNormalization_WhenAHeadingIsNonStandardCased()
    {
        // A non-standard-cased heading in the raw text (e.g. ALL-CAPS / lowercase).
        var resume = Resume(rawText: "ARBETSLIVSERFARENHET\nBackend-utvecklare, Acme AB, 2021–2024");

        var change = Single(await SuggestAsync(resume), ProposedChangeKind.HeadingNormalization);

        change.Kind.ShouldBe(ProposedChangeKind.HeadingNormalization);
        change.Evidence.ShouldBeOfType<TextSpanEvidence>(
            "HeadingNormalization citerar rubrikspannet (TextSpanEvidence).");
        change.Operation.ShouldNotBeNull();
        change.Operation!.Kind.ShouldBe(StructuralTransformKind.NormalizeHeadingCase);

        change.Provenance.ShouldBeOfType<StructuralTransformProvenance>()
            .Transform.ShouldBe(StructuralTransformKind.NormalizeHeadingCase);
    }

    [Fact]
    public async Task SuggestAsync_ShouldNotProposeHeadingNormalization_WhenHeadingsAlreadyStandard()
    {
        var resume = Resume(rawText: "Arbetslivserfarenhet\nBackend-utvecklare, Acme AB, 2021–2024");

        Of(await SuggestAsync(resume), ProposedChangeKind.HeadingNormalization).ShouldBeEmpty();
    }

    // ===============================================================
    // 8. PhotoStrip — ALWAYS zero in v1 (signal absent from text-only parse)
    // ===============================================================

    [Fact]
    public async Task SuggestAsync_ShouldNeverProposePhotoStrip_BecauseTheSignalIsAbsentInV1()
    {
        // The deterministic text-only parse cannot SEE a photo (no layout/image metadata in
        // ParsedResume v1). PhotoStrip must therefore yield ZERO changes — honest "not
        // assessed v1", never a fabricated edit against a signal that does not exist (§5).
        var withPhotoText = Resume(
            rawText: "Anna Andersson\nFoto bifogat\nBackend-utvecklare, Acme AB");

        Of(await SuggestAsync(withPhotoText), ProposedChangeKind.PhotoStrip).ShouldBeEmpty(
            "PhotoStrip är 'ej bedömt v1' — text-parsen ser ingen bild, ingen fabricerad ändring.");
    }

    // ===============================================================
    // 9. SectionReorder — ALWAYS zero in v1 (no rubric-recommended-order source)
    // ===============================================================

    [Fact]
    public async Task SuggestAsync_ShouldNeverProposeSectionReorder_BecauseNoRecommendedOrderSourceExistsInV1()
    {
        // There is no machine-readable rubric-recommended section order in the knowledge bank
        // (no hardcoded order in C# — CLAUDE.md §5). SectionReorder must yield ZERO changes
        // until such a data source exists (no fabricated reorder).
        var oddOrder = Resume(
            rawText: "Utbildning\nKTH 2016–2021\nArbetslivserfarenhet\nBackend-utvecklare 2021–2024");

        Of(await SuggestAsync(oddOrder), ProposedChangeKind.SectionReorder).ShouldBeEmpty(
            "SectionReorder saknar rubrik-rekommenderad-ordningskälla i v1 — ingen fabricerad omsortering.");
    }

    // ===============================================================
    // 10. Determinism — same input twice → identical ordered output
    // ===============================================================

    [Fact]
    public async Task SuggestAsync_ShouldBeDeterministic_WhenCalledTwiceOnTheSameInput()
    {
        // A CV that triggers several transforms; the proposal list (kinds + targetids +
        // order) must be byte-for-byte identical across runs (a rule engine is deterministic).
        var resume = Resume(
            profile: $"{RealClicheList().Entries[0].Phrase} kvalitet.",
            experience:
            [
                Experience(
                    period: "jan 2022 - juni 2024",
                    rawText: $"{Capitalize(RealVerbMapping().WeakVerbs[0].Weak)} ett område jan 2022 - juni 2024."),
            ]);

        var first = await SuggestAsync(resume);
        var second = await SuggestAsync(resume);

        first.Changes.Select(c => (c.Kind, c.TargetId))
            .ShouldBe(second.Changes.Select(c => (c.Kind, c.TargetId)),
                "Samma input ska ge identisk ordnad utdata (deterministisk motor).");
    }

    // ===============================================================
    // 11. Language dispatch — English CV routes WeakVerb through the English NLP path
    // ===============================================================

    [Fact]
    public async Task SuggestAsync_ShouldDispatchEnglishNlpPath_WhenCvIsEnglish()
    {
        // An English CV (DetectedLanguage.En) must analyse via TextLanguage.English without
        // throwing NotSupported (English is wired here per the TextLanguage contract). The
        // weak-verb transform still runs against the Swedish KB (the v1 verb map is Swedish),
        // so the assertion is on the dispatch NOT throwing, not on an English match.
        var resume = Resume(
            detectedLanguage: ResumeLanguage.En,
            profile: "Backend engineer with 8 years building payment platforms.",
            experience:
            [
                Experience(title: "Backend Engineer", organization: "Acme Inc",
                    period: "01/2022 – 06/2024",
                    rawText: "Led a team of 8 and increased conversion by 23% in 2024."),
            ]);

        var act = async () => await SuggestAsync(resume);

        await act.ShouldNotThrowAsync(
            "En engelsk CV ska dispatchas via TextLanguage.English, inte kasta NotSupported.");
    }

    [Fact]
    public async Task SuggestAsync_ShouldDispatchSwedishNlpPath_WhenCvIsSwedish()
    {
        var resume = Resume(detectedLanguage: ResumeLanguage.Sv);

        var act = async () => await SuggestAsync(resume);

        await act.ShouldNotThrowAsync();
    }

    // ===============================================================
    // 12. CriterionId — null when review absent, populated when review supplied
    // ===============================================================

    [Fact]
    public async Task SuggestAsync_ShouldProposeWithNullCriterionId_WhenReviewIsNull()
    {
        // The engine still proposes without a prior review (it can detect a cliché on its own);
        // the change carries a null CriterionId because no review criterion is bound to it.
        var entry = RealClicheList().Entries[0];
        var resume = Resume(profile: $"{entry.Phrase} kvalitet.");

        var change = Single(await SuggestAsync(resume, review: null), ProposedChangeKind.ClicheReplacement);

        change.CriterionId.ShouldBeNull(
            "Utan föregående review är ingen kriterie-id bunden till ändringen.");
    }

    [Fact]
    public async Task SuggestAsync_ShouldPopulateCriterionId_WhenReviewSuppliesTheMatchingCriterion()
    {
        // A supplied review with an A7 FAIL (the cliché criterion) → the ClicheReplacement
        // change is bound to that criterion id (the propose step references the verdict).
        var entry = RealClicheList().Entries[0];
        var resume = Resume(profile: $"{entry.Phrase} kvalitet.");

        var a7Fail = CvCriterionVerdict.Assessed(
            "A7", RubricCategory.Content, CriterionVerdict.Fail,
            [new TextSpanEvidence(new TextSpan(0, entry.Phrase.Length, entry.Phrase), "klyscha")]);
        var review = new CvReviewResult(
            RealRubric().Version, RenderProfile.Ats,
            Categories: [],
            Verdicts: [a7Fail],
            CriticalFails: [],
            AssessedCount: 1,
            TotalCount: 1);

        var change = Single(await SuggestAsync(resume, review: review), ProposedChangeKind.ClicheReplacement);

        change.CriterionId.ShouldBe("A7",
            "Med en review som flaggar A7 ska klyscha-ändringen bindas till A7.");
    }
}
