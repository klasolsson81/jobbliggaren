using Jobbliggaren.Application.KnowledgeBank.Abstractions;
using Jobbliggaren.Application.Resumes.Improvement.Abstractions;
using Jobbliggaren.Application.Resumes.Review.Abstractions;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.Resumes.Improvement;
using NSubstitute;
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
/// (<c>cliche-list.v2.json</c> / <c>verb-mapping.v1.json</c> / <c>rubric.v2.0.0.json</c>), so
/// the <c>After</c>-text can never drift from the data the engine actually reads. The cliché
/// drop-in arm is driven via a fake <c>IClicheLexicon</c> because today's real asset carries no
/// genuine drop-in (#495 — advisory guidance is never applied verbatim).
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
    // 1. ClicheReplacement (A7) — KnowledgeBank arm, drop-in ONLY (#495/#496)
    // ===============================================================

    // A substitute IClicheLexicon serving a single synthetic entry, so the drop-in emit path is
    // covered even though today's REAL asset carries no genuine drop-in (#495 — the advisory
    // Guidance must never be applied verbatim). The engine reads the lexicon through the port, so
    // a fake list is the honest way to drive the drop-in arm without inventing an asset drop-in.
    private static IClicheLexicon FakeLexicon(
        string phrase, string? dropIn, ClicheKind kind = ClicheKind.Cliche) =>
        FakeLexiconFrom(new ClicheEntry(phrase, kind, "Tom passion-signal", "Var konkret", dropIn));

    private static IClicheLexicon FakeLexiconFrom(params ClicheEntry[] entries)
    {
        var lexicon = Substitute.For<IClicheLexicon>();
        lexicon.GetClicheList().Returns(new ClicheList("test-cliche", entries));
        return lexicon;
    }

    private static async Task<CvImprovementResult> SuggestWithAsync(
        IClicheLexicon lexicon, ParsedResume resume, CvReviewResult? review = null) =>
        await new CvImprovementEngine(lexicon, RealVerbMapper(), RealRubricProvider(), Analyzer())
            .SuggestAsync(resume, review, RenderProfile.Ats, TestContext.Current.CancellationToken);

    [Fact]
    public async Task SuggestAsync_ShouldProposeNoClicheReplacement_FromTodaysRealAsset()
    {
        // #495: today's committed asset carries ZERO genuine drop-ins (every alternative is
        // advisory Guidance that may embed illustrative numbers / a meta-instruction). A CV FULL of
        // real clichés must therefore yield NO ClicheReplacement — the F4-9 A7 review flags them,
        // the improve engine synthesises nothing.
        var clicheProfile = string.Join(". ", RealClicheList().Entries.Take(4).Select(e => e.Phrase)) + ".";
        var resume = Resume(profile: clicheProfile);

        Of(await SuggestAsync(resume), ProposedChangeKind.ClicheReplacement).ShouldBeEmpty(
            "Utan äkta drop-in i assetet föreslår motorn ingen klyscha-ersättning (#495).");
    }

    [Fact]
    public async Task SuggestAsync_ShouldProposeClicheReplacement_WhenTheEntryCarriesAGenuineDropIn()
    {
        // The drop-in emit path (driven via a fake lexicon): a phrase WITH a genuine same-meaning
        // drop-in proposes it verbatim, with the KB provenance + the Why as rationale.
        const string dropIn = "Byggde betalplattform som sänkte kostnaden med 30 procent";
        var lexicon = FakeLexicon("Brinner för", dropIn);
        var resume = Resume(profile: "Brinner för kvalitet och samarbete.");

        var change = Single(await SuggestWithAsync(lexicon, resume), ProposedChangeKind.ClicheReplacement);

        change.Kind.ShouldBe(ProposedChangeKind.ClicheReplacement);
        change.Category.ShouldBe(RubricCategory.Content);
        change.Evidence.ShouldBeOfType<TextSpanEvidence>(
            "A7-klyscha citerar CV-spannet (TextSpanEvidence).");
        change.Replacement.ShouldNotBeNull();
        change.Replacement!.Before.ShouldBe("Brinner för",
            "Before ska vara den ordagrant citerade klyschan.");
        change.Replacement.Before.ShouldBe(((TextSpanEvidence)change.Evidence).Span.Quote,
            "Before måste vara EXAKT det citerade spannets Quote (propose-and-approve-kontraktet).");
        change.Replacement.After.ShouldBe(dropIn,
            "After ska vara EXAKT entryns DropInReplacement (ingen syntes).");
        change.Operation.ShouldBeNull("En KB-ersättning bär ingen StructuralOperation.");

        var kb = change.Provenance.ShouldBeOfType<KnowledgeBankProvenance>();
        kb.Source.ShouldBe("cliche-list");
        kb.Version.ShouldBe("test-cliche");
        kb.Key.ShouldBe("Brinner för", "Provenance.Key ska peka på källfrasen i kunskapsbanken.");
        change.Rationale.ShouldBe("Tom passion-signal",
            "Rationale ska komma från KB-entryns Why-fält (ingen påhittad motivering).");
    }

    [Fact]
    public async Task SuggestAsync_ShouldNotProposeClicheReplacement_WhenTheMatchedEntryHasNoDropIn()
    {
        // A cliché that matches but has NO drop-in (dropIn == null) is flagged by A7, never
        // rewritten (#495) — the improve engine proposes nothing for it.
        var lexicon = FakeLexicon("Brinner för", dropIn: null);
        var resume = Resume(profile: "Brinner för kvalitet och samarbete.");

        Of(await SuggestWithAsync(lexicon, resume), ProposedChangeKind.ClicheReplacement).ShouldBeEmpty();
    }

    [Fact]
    public async Task SuggestAsync_ShouldNotProposeClicheReplacement_WhenProfileHasNoCliche()
    {
        var lexicon = FakeLexicon("Brinner för", dropIn: "Byggde X");
        var resume = Resume(profile:
            "Backend-utvecklare med 8 års erfarenhet av betalsystem. Migrerade 3 plattformar 2024.");

        Of(await SuggestWithAsync(lexicon, resume), ProposedChangeKind.ClicheReplacement).ShouldBeEmpty();
    }

    [Fact]
    public async Task SuggestAsync_ShouldMatchClicheDropInOnAWordBoundary_NotMidWord()
    {
        // #496: "Social" must not splice mid-word inside "sociala medier" (which produced a
        // "…al medier" corruption); only a standalone occurrence proposes.
        var lexicon = FakeLexicon("Social", dropIn: "Höll 12 kundpresentationer 2024");

        Of(await SuggestWithAsync(lexicon, Resume(profile: "Ansvarade för sociala medier-strategin.")),
            ProposedChangeKind.ClicheReplacement).ShouldBeEmpty(
            "Klyschan 'Social' ska inte matcha inuti 'sociala' (#496).");

        Single(await SuggestWithAsync(lexicon, Resume(profile: "Social och trevlig i teamet.")),
            ProposedChangeKind.ClicheReplacement)
            .Replacement!.Before.ShouldBe("Social", "En fristående förekomst ska matchas ordagrant.");
    }

    [Fact]
    public async Task SuggestAsync_ShouldProposeEveryOccurrenceOfAClicheDropIn_Deterministically()
    {
        // #496: every word-bounded occurrence is proposed, left to right — not just the first.
        var lexicon = FakeLexicon("Self-starter", dropIn: "Initierade pilotprojekt Y på 4 månader");
        var resume = Resume(profile: "Self-starter i grunden. Alltid en self-starter i nya team.");

        Of(await SuggestWithAsync(lexicon, resume), ProposedChangeKind.ClicheReplacement).Count
            .ShouldBe(2, "Båda förekomsterna av klyschan ska föreslås deterministiskt (#496).");
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
            Experience(bullets: [$"{Capitalize(mapping.Weak)} ett område utan tydligt resultat."]),
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
            Experience(bullets: [$"{Capitalize(strong)} teamet om 8 personer med tydligt resultat 2024."]),
        ]);

        Of(await SuggestAsync(resume), ProposedChangeKind.WeakVerbUpgrade).ShouldBeEmpty();
    }

    [Fact]
    public async Task SuggestAsync_ShouldNotProposeWeakVerbUpgrade_WhenTheWeakVerbIsNotDropInSafe()
    {
        // #494: an unsafe weak verb ("var med och" → "genomförde" = double finite verb; "deltog i"
        // → "genomförde" = role-overreach ADR 0071 forbids inventing) must NOT get a literal KB
        // replacement. The F4-9 A2 review verdict still FLAGS the weak opener; the improve engine
        // proposes NO rewrite for it.
        var notSafe = RealVerbMapping().WeakVerbs.First(w => !w.DropInSafe); // "var med och"
        var resume = Resume(experience:
        [
            Experience(bullets: [$"{Capitalize(notSafe.Weak)} ett projekt utan tydligt resultat."]),
        ]);

        Of(await SuggestAsync(resume), ProposedChangeKind.WeakVerbUpgrade).ShouldBeEmpty(
            "Ett icke-drop-in-säkert svagt verb får ingen literal ersättning (#494, ADR 0071 no-synthesis).");
    }

    [Fact]
    public async Task SuggestAsync_ShouldProposeWeakVerbUpgrade_ForTheSecondDropInSafeVerb()
    {
        // Coverage of the second drop-in-safe pair: "hade hand om" → "ansvarade för" (same
        // valency). The transform proposes it with the verbatim KB After.
        var safe = RealVerbMapping().WeakVerbs.First(w => w.DropInSafe && w.Weak == "hade hand om");
        var resume = Resume(experience:
        [
            Experience(bullets: [$"{Capitalize(safe.Weak)} budget och personal."]),
        ]);

        var change = Single(await SuggestAsync(resume), ProposedChangeKind.WeakVerbUpgrade);
        change.Replacement!.After.ShouldBe(safe.SuggestedStrong,
            "After ska vara EXAKT SuggestedStrong ur verb-mapping (ingen syntes).");
    }

    [Fact]
    public async Task SuggestAsync_ShouldAlignBeforeWithTheVerbatimOpener_WhenBulletHasLeadingWhitespace()
    {
        // Regression guard on the load-bearing per-line Trim: ReviewText.DescriptionLines trims each
        // description line BEFORE the transform slices bullet[..Weak.Length], so the cited Before
        // equals the verbatim opener even when the line carries leading whitespace. (The #494 audit
        // called the naive slice a misalignment; it is NOT one while the trim stands — this red-flags
        // any future edit that drops the trim and lets leading whitespace mis-cite the user's span.)
        // #534: the whitespace lives on the DESCRIPTION line, not the header.
        var safe = RealVerbMapping().WeakVerbs[0]; // "var ansvarig för" (drop-in-safe)
        var resume = Resume(experience:
        [
            Experience(bullets: [$"   {Capitalize(safe.Weak)} ett område."]),
        ]);

        var change = Single(await SuggestAsync(resume), ProposedChangeKind.WeakVerbUpgrade);
        change.Replacement!.Before.ShouldBe(Capitalize(safe.Weak),
            "Before ska vara det verbatim inledande verbet (trimmat), inte en felriktad slice (#494).");
        var span = change.Evidence.ShouldBeOfType<TextSpanEvidence>();
        span.Span.Quote.ShouldBe(change.Replacement.Before);
    }

    [Fact]
    public async Task SuggestAsync_ShouldProposeWeakVerbUpgradeExactlyForDropInSafeVerbs_ForEveryWeakVerbInTheAsset()
    {
        // #494 full behaviour spec: iterate EVERY weak verb in the real asset and assert the
        // transform proposes a literal replacement IFF the pair is drop-in-safe — and that After
        // is the verbatim KB SuggestedStrong (no synthesis). Asset-driven (no hardcoded list) so
        // it tracks the data in lock-step with the drift-guard.
        foreach (var w in RealVerbMapping().WeakVerbs)
        {
            var resume = Resume(experience:
            [
                Experience(bullets: [$"{Capitalize(w.Weak)} verksamheten under 2024."]),
            ]);

            var changes = Of(await SuggestAsync(resume), ProposedChangeKind.WeakVerbUpgrade);

            if (w.DropInSafe)
            {
                var change = changes.ShouldHaveSingleItem();
                change.Replacement!.After.ShouldBe(w.SuggestedStrong,
                    $"'{w.Weak}' är drop-in-säkert → After ska vara EXAKT SuggestedStrong (verbatim KB).");
            }
            else
            {
                changes.ShouldBeEmpty(
                    $"'{w.Weak}' är INTE drop-in-säkert → ingen literal ersättning (#494, ADR 0071).");
            }
        }
    }

    [Fact]
    public async Task SuggestAsync_ShouldProposeOnlyForTheDropInSafeBullets_WhenACvMixesSafeAndUnsafeWeakOpeners()
    {
        // #494 per-bullet selectivity in ONE realistic CV: safe + unsafe + safe experience rows.
        // Only the two safe rows get a proposal; the unsafe one contributes zero; the two emitted
        // changes carry DISTINCT stable targetIds; every After is a verbatim drop-in-safe KB value.
        var safeA = RealVerbMapping().WeakVerbs.First(w => w.DropInSafe && w.Weak == "var ansvarig för");
        var unsafeB = RealVerbMapping().WeakVerbs.First(w => !w.DropInSafe); // "var med och"
        var safeC = RealVerbMapping().WeakVerbs.First(w => w.DropInSafe && w.Weak == "hade hand om");
        var safeAfters = RealVerbMapping().WeakVerbs
            .Where(w => w.DropInSafe).Select(w => w.SuggestedStrong).ToHashSet();

        var resume = Resume(experience:
        [
            Experience(bullets: [$"{Capitalize(safeA.Weak)} ett affärsområde."]),
            Experience(bullets: [$"{Capitalize(unsafeB.Weak)} ett projekt."]),
            Experience(bullets: [$"{Capitalize(safeC.Weak)} budget och personal."]),
        ]);

        var changes = Of(await SuggestAsync(resume), ProposedChangeKind.WeakVerbUpgrade);

        changes.Count.ShouldBe(2, "Bara de två drop-in-säkra raderna föreslås.");
        changes.Select(c => c.TargetId).Distinct().Count().ShouldBe(2,
            "Varje föreslagen ändring bär ett unikt stabilt targetId.");
        changes.ShouldAllBe(c => safeAfters.Contains(c.Replacement!.After),
            "Ingen syntes — varje After är ett verbatim drop-in-säkert KB-värde.");
    }

    [Fact]
    public async Task SuggestAsync_ShouldProposeWeakVerbUpgrade_WhenADescriptionBulletOpensWithAWeakVerb_ThoughTheTitleDoesNot()
    {
        // #534 (improve-side analog of the review-side #487): on a REAL parsed CV the entry's
        // RawText opens with the title/organisation HEADER, not a description verb. The transform
        // must score the DESCRIPTION bullets (ReviewText.DescriptionLines), so a weak opener in a
        // bullet is caught even though the header ("Backend-utvecklare — Acme AB") is not a weak
        // verb. Pre-#534 the transform read the whole block and matched the TITLE, so weak-verb
        // rewrites never fired in production. This is the load-bearing regression proof.
        var safe = RealVerbMapping().WeakVerbs[0]; // "var ansvarig för" → "ansvarade för" (drop-in-safe)
        var resume = Resume(experience:
        [
            Experience(
                title: "Backend-utvecklare",
                organization: "Acme AB",
                bullets: [$"{Capitalize(safe.Weak)} ett affärsområde."]),
        ]);

        var change = Single(await SuggestAsync(resume), ProposedChangeKind.WeakVerbUpgrade);
        change.Replacement!.Before.ShouldBe(Capitalize(safe.Weak),
            "Before ska vara det verbatim inledande verbet i BESKRIVNINGSraden, inte titelraden (#534).");
        change.Replacement!.After.ShouldBe(safe.SuggestedStrong,
            "After ska vara EXAKT SuggestedStrong ur verb-mapping (ingen syntes).");
        change.Evidence.ShouldBeOfType<TextSpanEvidence>().Span.Quote.ShouldBe(change.Replacement.Before);
    }

    [Fact]
    public async Task SuggestAsync_ShouldNotProposeWeakVerbUpgrade_WhenOnlyTheTitleLineOpensWithAWeakVerb()
    {
        // #534: the header line (title/organisation) is NOT a scored bullet. A weak-verb-shaped
        // TITLE must never be rewritten — only description bullets are scored. Pre-#534 the
        // transform read the whole block and WOULD have proposed a rewrite of the title (a false
        // positive on a job title that happens to open with a weak-verb phrase).
        var safe = RealVerbMapping().WeakVerbs[0]; // "var ansvarig för"
        var resume = Resume(experience:
        [
            Experience(
                title: $"{Capitalize(safe.Weak)} verksamheten",
                organization: "Acme AB",
                bullets: ["Ledde teamet om 8 personer och ökade konverteringen med 23 procent."]),
        ]);

        Of(await SuggestAsync(resume), ProposedChangeKind.WeakVerbUpgrade).ShouldBeEmpty(
            "Titelraden får aldrig poängsättas som en bulletöppnare (#534).");
    }

    [Fact]
    public async Task SuggestAsync_ShouldProposeAnUpgradePerDescriptionBullet_WhenOneExperienceHasTwoWeakOpeners()
    {
        // #534: an experience with several description bullets is scored PER bullet. Two drop-in-safe
        // weak openers in one entry yield two proposals with distinct, stable targetIds (a single
        // running index across all bullets of all experiences).
        var safeA = RealVerbMapping().WeakVerbs.First(w => w.DropInSafe && w.Weak == "var ansvarig för");
        var safeC = RealVerbMapping().WeakVerbs.First(w => w.DropInSafe && w.Weak == "hade hand om");
        var resume = Resume(experience:
        [
            Experience(
                title: "Projektledare",
                organization: "Acme AB",
                bullets:
                [
                    $"{Capitalize(safeA.Weak)} ett affärsområde.",
                    $"{Capitalize(safeC.Weak)} budget och personal.",
                ]),
        ]);

        var changes = Of(await SuggestAsync(resume), ProposedChangeKind.WeakVerbUpgrade);
        changes.Count.ShouldBe(2, "Varje beskrivningsrad med ett svagt inledande verb ger ett förslag (#534).");
        changes.Select(c => c.TargetId).ShouldBe(["weakverb:0", "weakverb:1"],
            "Varje förslag bär ett unikt stabilt targetId — löpande index från 0 över alla bullets.");
    }

    [Fact]
    public async Task SuggestAsync_ShouldNotProposeWeakVerbUpgrade_WhenTheEntryHasNoDescriptionBullets()
    {
        // #534: an experience with only a header/period line (no description bullets) yields nothing
        // to score — even if the TITLE is shaped like a weak verb. Guards the empty-DescriptionLines
        // loop boundary directly.
        var safe = RealVerbMapping().WeakVerbs[0]; // "var ansvarig för"
        var resume = Resume(experience:
        [
            Experience(title: $"{Capitalize(safe.Weak)} verksamheten", organization: "Acme AB", bullets: []),
        ]);

        Of(await SuggestAsync(resume), ProposedChangeKind.WeakVerbUpgrade).ShouldBeEmpty(
            "En post utan beskrivningsrader ger inget förslag, oavsett titelns form (#534).");
    }

    [Fact]
    public async Task SuggestAsync_ShouldProposeExactlyOneUpgradeForTheBullet_WhenBothTitleAndBulletOpenWithAWeakVerb()
    {
        // #534: even when the TITLE also opens with a (different) weak verb, only the DESCRIPTION
        // bullet is scored — the header never ADDS a proposal. Distinct verbs on the two lines make
        // the assertion unambiguous: exactly one change, citing the bullet's opener, not the title's.
        var titleVerb = RealVerbMapping().WeakVerbs.First(w => w.DropInSafe && w.Weak == "hade hand om");
        var bulletVerb = RealVerbMapping().WeakVerbs.First(w => w.DropInSafe && w.Weak == "var ansvarig för");
        var resume = Resume(experience:
        [
            Experience(
                title: $"{Capitalize(titleVerb.Weak)} verksamheten",
                organization: "Acme AB",
                bullets: [$"{Capitalize(bulletVerb.Weak)} ett affärsområde."]),
        ]);

        var change = Single(await SuggestAsync(resume), ProposedChangeKind.WeakVerbUpgrade);
        change.Replacement!.Before.ShouldBe(Capitalize(bulletVerb.Weak),
            "Endast beskrivningsradens öppnare citeras — titelraden adderar aldrig ett förslag (#534).");
    }

    [Fact]
    public async Task SuggestAsync_ShouldRunTheTargetIdIndexAcrossExperiences_WhenSeveralEntriesHaveWeakOpeners()
    {
        // #534: the running index spans ALL bullets of ALL experiences, so targetIds stay unique and
        // continuous across entry boundaries (not reset per experience).
        var safeA = RealVerbMapping().WeakVerbs.First(w => w.DropInSafe && w.Weak == "var ansvarig för");
        var safeC = RealVerbMapping().WeakVerbs.First(w => w.DropInSafe && w.Weak == "hade hand om");
        var resume = Resume(experience:
        [
            Experience(title: "Projektledare", bullets: [$"{Capitalize(safeA.Weak)} ett affärsområde."]),
            Experience(title: "Teamledare", bullets: [$"{Capitalize(safeC.Weak)} budget och personal."]),
        ]);

        var changes = Of(await SuggestAsync(resume), ProposedChangeKind.WeakVerbUpgrade);
        changes.Select(c => c.TargetId).ShouldBe(["weakverb:0", "weakverb:1"],
            "Index löper över alla erfarenheter — inte per post (#534).");
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

    [Fact]
    public async Task SuggestAsync_ShouldNotProposeDateNormalization_WhenPeriodIsIsoYearMonthRange()
    {
        // #420 harm 2: an ISO 8601 YYYY-MM range is canonical and machine-readable — the segmenter
        // itself extracts it. B6 must NOT emit a false "icke-standard datumformat" ReformatDate flag
        // on a date the engine already parsed (CLAUDE.md §5: a propose-and-approve flag must not
        // mis-report correct data).
        var resume = Resume(experience:
        [
            Experience(period: "2020-06 – 2024-03", rawText: "Sjuksköterska 2020-06 – 2024-03"),
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

    [Fact]
    public async Task SuggestAsync_ShouldDetectAstralPlaneEmoji_WhenSanitizingForAts()
    {
        // Emoji are among the most ATS-hostile glyphs and live in the ASTRAL planes (surrogate
        // PAIRS in UTF-16). A per-char scan saw each half as a lone Surrogate and missed every
        // emoji; the rune-based scan detects the whole glyph (#478 Low). The emoji is the ONLY
        // non-standard char here, so pre-fix this yielded ZERO changes (Single would throw).
        const string emoji = "\U0001F600"; // GRINNING FACE, U+1F600 — 2 UTF-16 code units
        var resume = Resume(rawText: $"Anna Andersson\nLedde teamet {emoji} och drev projekt 2024.");

        var change = Single(
            await SuggestAsync(resume, profile: RenderProfile.Ats),
            ProposedChangeKind.AtsSanitization);

        var span = change.Evidence.ShouldBeOfType<TextSpanEvidence>().Span;
        span.Quote.ShouldBe(emoji, "the cited quote is the WHOLE valid rune, never a split surrogate.");
        span.Length.ShouldBe(2);
        char.IsHighSurrogate(span.Quote[0]).ShouldBeTrue();
        span.Start.ShouldBeGreaterThanOrEqualTo(0);
    }

    // ── #478 Low 4 augmentation: astral-plane edge cases beyond the base emoji case ──
    // The rune-based scan must cite the EXACT UTF-16 offset (offset != 0), track its own
    // variable width per rune, count every offending glyph, handle a private-use astral
    // codepoint, keep BMP↔astral widths straight, and never throw on ill-formed UTF-16.

    [Fact]
    public async Task SuggestAsync_ShouldCiteTheExactUtf16Offset_WhenTheAstralEmojiIsMidText()
    {
        // The base test only asserts Start >= 0. This pins the EXACT offset: an astral glyph sitting
        // AFTER multi-byte Swedish letters must be cited at its true UTF-16 index (found here by an
        // independent IndexOf oracle), and Substring(Start, Length) must round-trip back to the whole
        // rune. Pre-fix the per-char scan never flagged the emoji at all (0 changes → Single throws).
        const string emoji = "\U0001F4A1"; // ELECTRIC LIGHT BULB, U+1F4A1 — 2 UTF-16 code units
        var resume = Resume(rawText: $"Förändrade kärnverksamheten på avdelningen {emoji} under 2024.");
        var expectedStart = resume.RawText.IndexOf(emoji, StringComparison.Ordinal);

        var span = Single(
            await SuggestAsync(resume, profile: RenderProfile.Ats),
            ProposedChangeKind.AtsSanitization)
            .Evidence.ShouldBeOfType<TextSpanEvidence>().Span;

        span.Start.ShouldBe(expectedStart,
            "the offset must be the emoji's true UTF-16 index, not a fabricated 0 or a byte offset.");
        span.Start.ShouldBeGreaterThan(0, "the glyph is genuinely mid-text, not at position 0.");
        span.Length.ShouldBe(2);
        resume.RawText.Substring(span.Start, span.Length).ShouldBe(emoji,
            "Substring(Start, Length) must round-trip to the whole rune (offset+width line up with RawText).");
    }

    [Fact]
    public async Task SuggestAsync_ShouldCountEveryAstralGlyphAndCiteTheFirst_WhenSeveralArePresent()
    {
        // Two DISTINCT astral emoji separated by ASCII: the scan must count BOTH (the count rides in
        // the evidence note) and cite the FIRST one at its exact offset. Pre-fix the per-char scan saw
        // four lone surrogates, none in a symbol category → count 0 → 0 changes (Single throws).
        const string first = "\U0001F680"; // ROCKET, U+1F680
        const string second = "\U0001F4C8"; // CHART INCREASING, U+1F4C8
        var resume = Resume(rawText: $"Ledde teamet {first} och drev leveransen {second} till 2024.");
        var expectedStart = resume.RawText.IndexOf(first, StringComparison.Ordinal);

        var evidence = Single(
            await SuggestAsync(resume, profile: RenderProfile.Ats),
            ProposedChangeKind.AtsSanitization)
            .Evidence.ShouldBeOfType<TextSpanEvidence>();

        evidence.Span.Start.ShouldBe(expectedStart, "the FIRST offending rune is cited.");
        evidence.Span.Quote.ShouldBe(first, "the cited quote is the first whole rune, not the second.");
        evidence.Note.ShouldNotBeNull();
        // The count rides as the numeric prefix of the note ("2 icke-standardtecken …") — a
        // language-neutral count, not localized prose. Both astral glyphs must be counted.
        evidence.Note!.ShouldStartWith("2 ");
    }

    [Fact]
    public async Task SuggestAsync_ShouldDetectAnAstralPrivateUseGlyph_WhenSanitizingForAts()
    {
        // A Supplementary Private Use Area-A codepoint (U+F0000, category PrivateUse) is astral too:
        // it encodes as a surrogate pair, so the per-char scan missed it exactly like an emoji. The
        // rune-based scan classifies the WHOLE rune as PrivateUse → flagged, quote round-trips (len 2).
        const string pua = "\U000F0000"; // SUPPLEMENTARY PRIVATE USE AREA-A first codepoint
        var resume = Resume(rawText: $"Anna Andersson\nDrev projektet {pua} till leverans 2024.");
        var expectedStart = resume.RawText.IndexOf(pua, StringComparison.Ordinal);

        var span = Single(
            await SuggestAsync(resume, profile: RenderProfile.Ats),
            ProposedChangeKind.AtsSanitization)
            .Evidence.ShouldBeOfType<TextSpanEvidence>().Span;

        span.Start.ShouldBe(expectedStart);
        span.Length.ShouldBe(2, "an astral private-use codepoint is 2 UTF-16 code units.");
        resume.RawText.Substring(span.Start, span.Length).ShouldBe(pua);
    }

    [Fact]
    public async Task SuggestAsync_ShouldCiteTheFirstOffendingRuneWithItsOwnWidth_WhenABmpSymbolPrecedesAnAstralEmoji()
    {
        // A single-unit BMP symbol (★ U+2605, OtherSymbol) sits BEFORE an astral emoji. The first
        // offending rune is the ★ — cited with LENGTH 1 (its own UTF-16 width), NOT the emoji's 2 —
        // while the count still includes the astral glyph the per-char scan would have missed. This
        // pins that firstOffendingLength is the offending rune's real width, never a hardcoded 1-or-2.
        const string star = "★"; // BLACK STAR, U+2605 — 1 UTF-16 code unit, OtherSymbol
        const string emoji = "\U0001F600"; // GRINNING FACE, U+1F600 — 2 UTF-16 code units
        var resume = Resume(rawText: $"Meriter\n{star} Ledde teamet {emoji} med resultat 2024.");
        var expectedStart = resume.RawText.IndexOf(star, StringComparison.Ordinal);

        var evidence = Single(
            await SuggestAsync(resume, profile: RenderProfile.Ats),
            ProposedChangeKind.AtsSanitization)
            .Evidence.ShouldBeOfType<TextSpanEvidence>();

        evidence.Span.Start.ShouldBe(expectedStart, "the BMP star is the first offending rune.");
        evidence.Span.Quote.ShouldBe(star, "the first offending rune is the star, cited verbatim.");
        evidence.Span.Length.ShouldBe(1, "a BMP symbol is 1 UTF-16 unit — its own width, not the emoji's 2.");
        evidence.Note.ShouldNotBeNull();
        // The count must include the astral emoji the old per-char scan missed (star + emoji = 2).
        evidence.Note!.ShouldStartWith("2 ");
    }

    [Fact]
    public async Task SuggestAsync_ShouldNotThrowAndKeepTheSpanInRange_WhenRawTextContainsALoneSurrogate()
    {
        // Robustness guard (NOT a red-vs-pre-fix case — the per-char scan also never threw here): an
        // ill-formed UTF-16 string carrying a LONE high surrogate must not crash the rune-based scan.
        // EnumerateRunes substitutes U+FFFD for the ill-formed unit, so the whole engine completes and
        // any emitted Ats span stays a valid, in-range slice of RawText (no out-of-range Substring).
        const string loneSurrogate = "\uD83D"; // high surrogate with no following low surrogate
        var resume = Resume(rawText: $"Anna Andersson\nLedde teamet {loneSurrogate} och drev projekt 2024.");

        // A crash in the rune-based scan would surface as a thrown exception here — reaching the
        // assertions below IS the no-crash proof (EnumerateRunes substitutes U+FFFD for the ill-formed unit).
        var result = await SuggestAsync(resume, profile: RenderProfile.Ats);

        foreach (var span in Of(result, ProposedChangeKind.AtsSanitization)
            .Select(c => c.Evidence).OfType<TextSpanEvidence>().Select(e => e.Span))
        {
            span.Start.ShouldBeInRange(0, resume.RawText.Length - span.Length);
            Should.NotThrow(() => resume.RawText.Substring(span.Start, span.Length),
                "any cited Ats span must be a valid, in-range slice of RawText.");
        }
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
                    bullets: [$"{Capitalize(RealVerbMapping().WeakVerbs[0].Weak)} ett område jan 2022 - juni 2024."]),
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
                    bullets: ["Led a team of 8 and increased conversion by 23% in 2024."]),
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
        // the change carries a null CriterionId because no review criterion is bound to it. Driven
        // via a fake lexicon carrying a drop-in (today's real asset has none — #495).
        var lexicon = FakeLexicon("Brinner för", dropIn: "Byggde X som gav Y");
        var resume = Resume(profile: "Brinner för kvalitet.");

        var change = Single(await SuggestWithAsync(lexicon, resume), ProposedChangeKind.ClicheReplacement);

        change.CriterionId.ShouldBeNull(
            "Utan föregående review är ingen kriterie-id bunden till ändringen.");
    }

    [Fact]
    public async Task SuggestAsync_ShouldPopulateCriterionId_WhenReviewSuppliesTheMatchingCriterion()
    {
        // A supplied review with an A7 FAIL (the cliché criterion) → the ClicheReplacement
        // change is bound to that criterion id (the propose step references the verdict).
        var lexicon = FakeLexicon("Brinner för", dropIn: "Byggde X som gav Y");
        var resume = Resume(profile: "Brinner för kvalitet.");

        var a7Fail = CvCriterionVerdict.Assessed(
            "A7", RubricCategory.Content, CriterionVerdict.Fail,
            [new TextSpanEvidence(new TextSpan(0, "Brinner för".Length, "Brinner för"), "klyscha")]);
        var review = new CvReviewResult(
            RealRubric().Version, RenderProfile.Ats,
            Categories: [],
            Verdicts: [a7Fail],
            CriticalFails: [],
            AssessedCount: 1,
            TotalCount: 1);

        var change = Single(await SuggestWithAsync(lexicon, resume, review), ProposedChangeKind.ClicheReplacement);

        change.CriterionId.ShouldBe("A7",
            "Med en review som flaggar A7 ska klyscha-ändringen bindas till A7.");
    }
}
