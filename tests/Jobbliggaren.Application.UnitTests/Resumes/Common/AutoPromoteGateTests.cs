using Jobbliggaren.Application.Resumes.Common;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Common;

/// <summary>
/// #1060 PR C — the extracted auto-promote policy evaluator. These are the reason-matrix tests:
/// one pure function, every gate, every order dependency. The two CALL SITES are tested where
/// they live (<c>AutoPromoteParsedResumeCommandHandlerTests</c> for the write path,
/// <c>GetParsedResumeQueryHandlerTests</c> + <c>GetParsedResumeEndpointTests</c> for the read
/// path); what is proven here is that both of them are asking the same question.
///
/// <para><b>Premise (CLAUDE.md §5 <c>Tests:</c>):</b> every fixture goes through
/// <c>ParsedResume.Create</c> — the production import's own entry point — with values the
/// deterministic parser does produce: a flagged scan built by running the real
/// <c>PersonnummerScanner</c> over text, <c>ParseConfidence.Failed(ExtractionFailed)</c> exactly
/// as <c>ImportResumeCommandHandler</c> constructs it when extraction yields nothing, and an
/// experience entry missing its organization, which the segmenter produces whenever a CV lists
/// a role without an employer line. Nothing here is a state <c>src/</c> cannot reach.</para>
/// </summary>
public class AutoPromoteGateTests
{
    private static readonly JobSeekerId Owner = JobSeekerId.New();

    // A real Luhn-valid Swedish personnummer the scanner flags (parity with the handler tests).
    private const string ValidPersonnummer = "811218-9876";

    /// <summary>The account holder's display name — CV CONTENT, the DQ6 guard's subject.</summary>
    private const string AccountName = "Anna Kontosson";

    /// <summary>The generated, non-PII label the resolver produces when no one typed a name.
    /// This is what the READ path always passes (it has no form field).</summary>
    private static string GeneratedLabel =>
        ResumeLabelResolver.Resolve(nameOverride: null, FakeDateTimeProvider.Default);

    private static ParsedResumeContent CleanContent(
        IReadOnlyList<ParsedExperience>? experience = null) =>
        new(
            new ParsedContact("Fil Namnsson", "fil@example.com", "070-1234567", "Stockholm"),
            profile: "Erfaren backend-utvecklare.",
            experience: experience ??
                [new ParsedExperience("Backend-utvecklare", "Beta AB", "2019–2022", "raw entry")],
            education: [new ParsedEducation("KTH", "Civilingenjör", "2013–2018", "raw edu")],
            skills: ["C#"],
            languages: ["Svenska"]);

    private static ParseConfidence Confident() =>
        ParseConfidence.FromSections(
        [
            new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Confident, []),
            new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident, []),
        ]);

    private static ParseConfidence Degraded() =>
        ParseConfidence.FromSections(
        [
            new SectionConfidence(ParsedSectionKind.Contact, SectionConfidenceLevel.Degraded, []),
            new SectionConfidence(ParsedSectionKind.Experience, SectionConfidenceLevel.Confident, []),
        ]);

    private static PersonnummerScanOutcome Flagged() =>
        PersonnummerScanOutcome.FromMatches(
            PersonnummerScanner.Scan($"Pnr {ValidPersonnummer} i CV."));

    private static ParsedResume BuildParsed(
        ParsedResumeContent? content = null,
        ParseConfidence? confidence = null,
        PersonnummerScanOutcome? pnr = null) =>
        ParsedResume.Create(
            Owner, "anna-cv.pdf", "application/pdf", ResumeLanguage.Sv,
            content ?? CleanContent(),
            "raw text",
            confidence ?? Confident(),
            pnr ?? PersonnummerScanOutcome.None,
            [], FakeDateTimeProvider.Default).Value;

    private static AutoPromoteGateVerdict Evaluate(
        ParsedResume parsed, string? personName = null, string? label = null) =>
        AutoPromoteGate.Evaluate(
            parsed,
            personName ?? AccountName,
            label ?? GeneratedLabel,
            Owner,
            FakeDateTimeProvider.Default);

    // ===============================================================
    // Promotable — the arm the write path adopts
    // ===============================================================

    [Fact]
    public void Evaluate_CleanConfidentParse_IsPromotable_AndCarriesTheBuiltResume()
    {
        var verdict = Evaluate(BuildParsed());

        var promotable = verdict.ShouldBeOfType<AutoPromoteGateVerdict.Promotable>();
        promotable.Resume.Origin.ShouldBe(ResumeSourceOrigin.Import);
        // The two name channels stayed separate: the LABEL is the generated default and the
        // person name went into the content. Collapsing them is the defect PR A split apart.
        promotable.Resume.Name.ShouldBe(GeneratedLabel);
        promotable.Resume.MasterVersion.Content.PersonalInfo.FullName.ShouldBe(AccountName);
        verdict.BlockReason.ShouldBeNull();
    }

    [Fact]
    public void Evaluate_DegradedParse_IsPromotable_TheGateNarrowedToFailed()
    {
        // #1060 D1.3 — the R3 reversal. A Degraded parse found SOMETHING; under ADR 0112 that
        // is the CV the reviewer has most to say about. If this ever goes red, read
        // AutoPromoteBlockReason.ParseNotConfident's docblock before "fixing" it.
        var verdict = Evaluate(BuildParsed(confidence: Degraded()));

        verdict.ShouldBeOfType<AutoPromoteGateVerdict.Promotable>();
        verdict.BlockReason.ShouldBeNull();
    }

    // ===============================================================
    // Blocked — one member per reason, the whole surviving set
    // ===============================================================

    [Fact]
    public void Evaluate_FlaggedParse_BlocksOnPersonnummerPresent()
    {
        var verdict = Evaluate(BuildParsed(pnr: Flagged()));

        verdict.BlockReason.ShouldBe(AutoPromoteBlockReason.PersonnummerPresent);
    }

    [Fact]
    public void Evaluate_FailedExtraction_BlocksOnParseNotConfident()
    {
        var parsed = BuildParsed(
            confidence: ParseConfidence.Failed(ParseFallbackReason.ExtractionFailed));

        Evaluate(parsed).BlockReason.ShouldBe(AutoPromoteBlockReason.ParseNotConfident);
    }

    [Fact]
    public void Evaluate_PersonnummerInTheLabel_BlocksOnPersonnummerPresent_NotIncompleteContent()
    {
        // The label channel. Resume.ValidateName would refuse this too, but as a BUILDABILITY
        // failure — which would report IncompleteContent and tell the user to fix her file when
        // the problem is the name she typed. Mis-reporting a verdict is a §5 violation, so the
        // label scan runs first and this test is what pins that ordering.
        var verdict = Evaluate(BuildParsed(), label: $"CV {ValidPersonnummer}");

        verdict.BlockReason.ShouldBe(AutoPromoteBlockReason.PersonnummerPresent);
    }

    [Fact]
    public void Evaluate_PersonnummerInTheAccountDisplayName_BlocksOnItsOwnToken_NotOnPersonnummerPresent()
    {
        // DQ6 on the COMPOSED content: the display name is the one text this composition adds
        // over the raw superset the import scan already covered, so this is the only control
        // that can catch it — and the FILE is clean, which is exactly why it needs a token of
        // its own (CTO-bind D2). PersonnummerPresent drives copy that says "take it out of the
        // file and upload again", advice that cannot work when the file has nothing in it.
        var verdict = Evaluate(BuildParsed(), personName: $"Anna {ValidPersonnummer}");

        verdict.BlockReason.ShouldBe(AutoPromoteBlockReason.PersonnummerInAccountName);
        verdict.BlockReason.ShouldNotBe(AutoPromoteBlockReason.PersonnummerPresent);
    }

    [Fact]
    public void Evaluate_PersonnummerInASCANNEDParseField_ReportsPersonnummerPresent_NotTheAccountNameToken()
    {
        // The counterfactual that makes PersonnummerInAccountName's claim legible
        // (security-auditor round 2, Minor 2). That token asserts WHERE the number is, but
        // ResumeContentPersonnummerGuard returns only pass/fail — the location is reached by
        // ELIMINATION: every other text in the composed DTO is a projection of the parse, and
        // the import scan already covered that raw superset, so a DQ6 failure the parse scan
        // did not see must be the display name.
        //
        // The elimination holds only while the parse scan and the composed scan agree about
        // parse-derived text. If they ever diverged, the FE would tell a user "Filen är däremot
        // ren" about a file that is not, and send her to Inställningar — the loop this PR
        // exists to close, inverted. This pins the direction that matters: a personnummer
        // sitting in a SCANNED parse field is the FILE's, and the Tier-1 flag claims it first.
        var parsed = BuildParsed(
            content: CleanContent(
                experience:
                [
                    new ParsedExperience(
                        "Backend-utvecklare", $"Beta AB {ValidPersonnummer}", "2019–2022", "raw"),
                ]),
            pnr: Flagged());

        var verdict = Evaluate(parsed);

        verdict.BlockReason.ShouldBe(AutoPromoteBlockReason.PersonnummerPresent);
        verdict.BlockReason.ShouldNotBe(AutoPromoteBlockReason.PersonnummerInAccountName);
    }

    [Fact]
    public void Evaluate_ExperienceEntryWithoutOrganization_BlocksOnIncompleteContent()
    {
        // The canonical Resume rejects an entry without an organization; the mapper never drops
        // the entry to make it fit (that would promote a CV saying less than the file did).
        var parsed = BuildParsed(
            content: CleanContent(
                experience: [new ParsedExperience("Backend-utvecklare", null, "2019–2022", "raw")]));

        Evaluate(parsed).BlockReason.ShouldBe(AutoPromoteBlockReason.IncompleteContent);
    }

    // ===============================================================
    // Order — a blocked verdict names ONE reason, and it is the right one
    // ===============================================================

    [Fact]
    public void Evaluate_FlaggedParseThatIsAlsoUnbuildable_ReportsPersonnummerPresent()
    {
        // Both gates would fire. The answer must be the personnummer: it is the highest-priority
        // PII rule, it is what the user must act on, and "complete your file" would be advice
        // that does not fix anything (CLAUDE.md §5 — never mis-report a verdict).
        var parsed = BuildParsed(
            content: CleanContent(
                experience: [new ParsedExperience("Backend-utvecklare", null, "2019–2022", "raw")]),
            pnr: Flagged());

        Evaluate(parsed).BlockReason.ShouldBe(AutoPromoteBlockReason.PersonnummerPresent);
        // …and it is the FILE token, not the account-name one: the parse's own scan fired, so
        // the DQ6 arm was never reached.
        Evaluate(parsed).BlockReason.ShouldNotBe(AutoPromoteBlockReason.PersonnummerInAccountName);
    }

    [Fact]
    public void Evaluate_FailedExtractionThatIsAlsoUnbuildable_ReportsParseNotConfident()
    {
        var parsed = BuildParsed(
            content: CleanContent(
                experience: [new ParsedExperience("Backend-utvecklare", null, "2019–2022", "raw")]),
            confidence: ParseConfidence.Failed(ParseFallbackReason.ExtractionFailed));

        Evaluate(parsed).BlockReason.ShouldBe(AutoPromoteBlockReason.ParseNotConfident);
    }

    // ===============================================================
    // The surviving reason set (#1060: PR B retired one, PR C split one out).
    // No number here on purpose — the assertion below reads the set dynamically, so a count
    // written beside it can only ever rot. Three of round 2's four Minors were exactly that.
    // ===============================================================

    [Fact]
    public void AutoPromoteBlockReason_IsTheLockedFourMemberSet()
    {
        // The FE writes one copy string per member and the review view switches on the token,
        // so a member added without copy would render a block with nothing to read. #844's
        // UnclassifiedPreamble was retired in PR B and PersonnummerInAccountName was split out
        // of PersonnummerPresent in PR C; this pins the set the copy covers.
        Enum.GetNames<AutoPromoteBlockReason>().ShouldBe(
            [
                nameof(AutoPromoteBlockReason.PersonnummerPresent),
                nameof(AutoPromoteBlockReason.PersonnummerInAccountName),
                nameof(AutoPromoteBlockReason.ParseNotConfident),
                nameof(AutoPromoteBlockReason.IncompleteContent),
            ],
            ignoreOrder: true);
    }

    // ===============================================================
    // The domain code (#1060 D3(β) PR 2, CTO constraint 1)
    // ===============================================================

    [Fact]
    public void Evaluate_UnbuildableContent_CarriesTheDomainCodeVerbatim()
    {
        // The point of the field: `IncompleteContent` is ONE token over every code
        // `CreateFromParsed` can return, and which one fired decides whether a per-entry router
        // would help at all. Since #1060 D3(β-2) that reads off the declaring type:
        // `ResumeEntryBuildability` declares the per-entry constraints — the
        // `Resume.ExperienceCompanyRequired` this test asserts is one of them — while
        // `ValidateContent` and `CreateFromParsed`'s own preconditions declare the rest.
        // The gate transports `created.Error.Code` unexamined — no predicate is re-encoded
        // here, so this asserts transport, not a second opinion about what the Domain decided.
        var parsed = BuildParsed(
            content: CleanContent(
                experience: [new ParsedExperience("Backend-utvecklare", null, "2019–2022", "raw")]));

        var blocked = Evaluate(parsed).ShouldBeOfType<AutoPromoteGateVerdict.Blocked>();

        blocked.Reason.ShouldBe(AutoPromoteBlockReason.IncompleteContent);
        blocked.DomainErrorCode.ShouldBe("Resume.ExperienceCompanyRequired");
    }

    [Fact]
    public void Evaluate_EveryArmThatIsNotBuildability_CarriesNoDomainErrorCode()
    {
        // CTO constraint 1, and it is the price of NOT modelling this as a third DU case:
        // `Blocked(PersonnummerPresent, "something")` is representable, so without this pin
        // nothing says it is wrong. A populated code on a policy arm would put a Domain
        // constraint identity beside a verdict no Domain evaluation produced — a mis-report of
        // the same shape §5 forbids, one level down from the token.
        var policyArms = new (string Arm, AutoPromoteBlockReason Expected,
            AutoPromoteGateVerdict Verdict)[]
        {
            ("pnr on parse", AutoPromoteBlockReason.PersonnummerPresent,
                Evaluate(BuildParsed(pnr: Flagged()))),
            ("confidence", AutoPromoteBlockReason.ParseNotConfident, Evaluate(BuildParsed(
                confidence: ParseConfidence.Failed(ParseFallbackReason.ExtractionFailed)))),
            ("pnr in label", AutoPromoteBlockReason.PersonnummerPresent,
                Evaluate(BuildParsed(), label: $"CV {ValidPersonnummer}")),
            ("pnr DQ6", AutoPromoteBlockReason.PersonnummerInAccountName,
                Evaluate(BuildParsed(), personName: $"Anna {ValidPersonnummer}")),
        };

        foreach (var (arm, expected, verdict) in policyArms)
        {
            var blocked = verdict.ShouldBeOfType<AutoPromoteGateVerdict.Blocked>();

            // The expected reason is asserted PER ARM, not only through the closure below. That
            // is what makes deleting a fixture a deleted assertion a reviewer can see, rather
            // than a silent narrowing — see the limitation note under the closure.
            blocked.Reason.ShouldBe(expected, $"the '{arm}' rung");
            blocked.DomainErrorCode.ShouldBeNull(
                $"the '{arm}' rung blocks on policy, so no Domain evaluation produced a code "
                + "— see AutoPromoteGateVerdict.Blocked's docblock");
        }

        // CLOSURE over the declared reason set: the fixtures above plus the buildability arm must
        // cover it, so a fifth AutoPromoteBlockReason reddens this instead of sliding past a
        // hand-written list.
        //
        // ITS LIMIT, stated because it is not the limit it looks like (code-reviewer + test-writer,
        // both measured): this closes over TOKENS, not RUNGS. There are FIVE blocking call sites
        // and only FOUR tokens — "pnr on parse" and "pnr in label" both return
        // PersonnummerPresent — so `Distinct()` is unchanged if either of those two fixtures is
        // removed, and a future SIXTH arm returning an existing token would not redden this
        // either — five blocking call sites exist today, so the next one is the sixth. What covers that
        // is the per-arm expectation in the loop above. What does not exist is a check that the
        // arm COUNT is still five, and the obstacle is not assembly visibility (this project has
        // `InternalsVisibleTo`): call sites are not a runtime surface. A source-text scan could
        // do it, and the repo runs those elsewhere; it is out of scope here.
        policyArms
            .Select(a => a.Expected.ToString())
            .Append(nameof(AutoPromoteBlockReason.IncompleteContent))
            .Distinct()
            .ShouldBe(Enum.GetNames<AutoPromoteBlockReason>(), ignoreOrder: true);
    }

    [Fact]
    public void Evaluate_DoesNotMutateTheArtifact()
    {
        // The read path calls this on every review load. It must be a question, not a step:
        // the artifact stays PendingReview and un-soft-deleted, whichever arm is taken.
        var parsed = BuildParsed();

        Evaluate(parsed).ShouldBeOfType<AutoPromoteGateVerdict.Promotable>();

        parsed.Status.ShouldBe(ParsedResumeStatus.PendingReview);
        parsed.DeletedAt.ShouldBeNull();
    }
}
