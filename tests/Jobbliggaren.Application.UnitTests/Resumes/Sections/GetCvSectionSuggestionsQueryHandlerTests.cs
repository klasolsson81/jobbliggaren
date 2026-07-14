using Jobbliggaren.Application.Common.Abstractions;
using Jobbliggaren.Application.Common.Auditing;
using Jobbliggaren.Application.JobAds.Abstractions;
using Jobbliggaren.Application.JobAds.Queries.GetTaxonomyTree;
using Jobbliggaren.Application.Resumes.Sections.Queries.GetCvSectionSuggestions;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.Privacy;
using Jobbliggaren.Domain.Resumes;
using Jobbliggaren.Domain.Resumes.Parsing;
using Jobbliggaren.Infrastructure.KnowledgeBank;
using Jobbliggaren.Infrastructure.Persistence;
using Jobbliggaren.Infrastructure.Resumes.Parsing;
using NSubstitute;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.Resumes.Sections;

/// <summary>
/// Fas 4b 8b.4a (ADR 0107) — the occupation-driven section-suggestion read-slice.
/// <para>
/// Driven against the REAL branschgrupp asset and the REAL parsing lexicon (never a fixture
/// table): the point of this slice is that the shipped data agrees with itself, and a test with
/// its own private copy of the rules would prove nothing about what users actually get.
/// </para>
/// </summary>
public class GetCvSectionSuggestionsQueryHandlerTests
{
    private const string DataIt = "apaJ_2ja_LuF";
    private const string HalsoSjukvard = "NYW6_mP6_vwf";
    private const string Bygg = "j7Cq_ZJe_GkT";        // one of the 17 → ovriga

    // ssyk-4 occupation-GROUP ids — what MatchPreferences actually holds. The fake maps them to
    // their parent FIELD, which is exactly what the real read-model does off the seeded snapshot.
    private const string GroupSystemutvecklare = "DJh5_yyF_hEM";
    private const string GroupUnderskoterska = "Z8ci_bBE_tmx";
    private const string GroupByggnadstrahantverkare = "TxQD_ZP5_hAo";

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IFailedAccessLogger _failedAccessLogger = Substitute.For<IFailedAccessLogger>();
    private readonly Guid _userId = Guid.NewGuid();

    public GetCvSectionSuggestionsQueryHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
    }

    private static CvParsingLexiconProvider RealLexicon() => new(CvParsingLexiconLoader.Load());

    private static BranschgruppProvider RealBranschgrupp() => new(RealLexicon());

    private GetCvSectionSuggestionsQueryHandler CreateHandler(AppDbContext db) =>
        new(db, _currentUser, new FakeTaxonomy(), RealBranschgrupp(), RealLexicon(), _failedAccessLogger);

    /// <summary>
    /// Group → parent FIELD: the same 1:1 parent edge <c>TaxonomyReadModel</c> reads out of the
    /// seeded snapshot (proven against the real DB in TaxonomyReadModelIntegrationTests). An
    /// unknown group contributes nothing — graceful, parity the real impl.
    /// </summary>
    private sealed class FakeTaxonomy : ITaxonomyReadModel
    {
        private static readonly Dictionary<string, string> FieldByGroup = new(StringComparer.Ordinal)
        {
            [GroupSystemutvecklare] = DataIt,
            [GroupUnderskoterska] = HalsoSjukvard,
            [GroupByggnadstrahantverkare] = Bygg,
        };

        public ValueTask<IReadOnlyList<string>> GetContainingOccupationFieldsAsync(
            IReadOnlyList<string> occupationGroupConceptIds, CancellationToken cancellationToken)
            => ValueTask.FromResult<IReadOnlyList<string>>(
                [.. occupationGroupConceptIds
                    .Where(FieldByGroup.ContainsKey)
                    .Select(g => FieldByGroup[g])
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(id => id, StringComparer.Ordinal)]);

        public ValueTask<TaxonomyTreeDto> GetTreeAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException("Sektionsförslagen läser bara containment.");

        public ValueTask<IReadOnlyList<TaxonomyLabelDto>> ResolveLabelsAsync(
            IReadOnlyList<string> conceptIds, CancellationToken cancellationToken)
            => throw new NotSupportedException("Sektionsförslagen läser bara containment.");

        public ValueTask<IReadOnlyList<TaxonomySuggestionDto>> SuggestByPrefixAsync(
            string prefix, int limit, CancellationToken cancellationToken)
            => throw new NotSupportedException("Sektionsförslagen läser bara containment.");

        public ValueTask<IReadOnlyList<string>> GetRelatedOccupationGroupsAsync(
            IReadOnlyList<string> ssyk4ConceptIds, CancellationToken cancellationToken)
            => throw new NotSupportedException("Sektionsförslagen läser bara containment.");

        public ValueTask<IReadOnlyList<string>> GetContainingRegionsAsync(
            IReadOnlyList<string> municipalityConceptIds, CancellationToken cancellationToken)
            => throw new NotSupportedException("Sektionsförslagen läser bara containment.");
    }

    // ParsedResume.Content is an EF-ignored, encrypted Form-B shadow: InMemory + AsNoTracking
    // re-materialises it as NULL, and only the real decryption interceptor populates it. The house
    // seam is FakeContentHydrationInterceptor (the same one GetParsedResumeQueryHandlerTests uses);
    // the real decrypt path stays proven end-to-end by the Api/Worker integration tests. The
    // interceptor is constructed WITH the content, so the content must exist before the DbContext.
    private static AppDbContext NewDb(ParsedResumeContent content) =>
        TestAppDbContextFactory.Create(new FakeContentHydrationInterceptor(content));

    private static ParsedResumeContent Content(IReadOnlyList<ParsedSection>? sections = null) =>
        new(ParsedContact.Empty, profile: "Erfaren.", sections: sections);

    private static ParsedSection Section(string heading) =>
        new(heading, [new ParsedSectionEntry("Titel", ["Brödtext"])]);

    private static ParseConfidence ConfidentConfidence() =>
        ParseConfidence.FromSections(
        [
            new SectionConfidence(
                ParsedSectionKind.Contact, SectionConfidenceLevel.Confident, ["kontakt hittad"]),
        ]);

    private static async Task<ParsedResume> SeedAsync(
        AppDbContext db,
        Guid userId,
        ParsedResumeContent content,
        IEnumerable<string>? occupationGroups = null,
        IEnumerable<ProposedOccupation>? occupationProposals = null)
    {
        var seeker = JobSeeker.Register(userId, "Test User", FakeDateTimeProvider.Default).Value;

        if (occupationGroups is not null)
        {
            var prefs = MatchPreferences.Create(occupationGroups, null, null).Value;
            seeker.UpdateMatchPreferences(prefs, FakeDateTimeProvider.Default);
        }

        db.JobSeekers.Add(seeker);

        var parsed = ParsedResume.Create(
            seeker.Id, "cv.pdf", "application/pdf", ResumeLanguage.Sv, content,
            rawText: "Anna Andersson", ConfidentConfidence(),
            PersonnummerScanOutcome.None, occupationProposals ?? [], FakeDateTimeProvider.Default).Value;

        db.ParsedResumes.Add(parsed);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return parsed;
    }

    /// <summary>The common arrangement: one job seeker who owns one parsed CV.</summary>
    private async Task<(AppDbContext Db, ParsedResume Parsed)> ArrangeAsync(
        IEnumerable<string>? occupationGroups = null,
        IReadOnlyList<ParsedSection>? sections = null,
        IEnumerable<ProposedOccupation>? occupationProposals = null)
    {
        var content = Content(sections);
        var db = NewDb(content);
        var parsed = await SeedAsync(db, _userId, content, occupationGroups, occupationProposals);
        return (db, parsed);
    }

    // ───────────────────────────────────────────────────────────────────
    // THE TWO EMPTY STATES — the CTO bind's central constraint. They must NOT be merged.
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ShouldReportNoOccupationPreference_WhenTheUserHasStatedNoOccupation()
    {
        // State (1): she never told us. The guide shows the generic row AND asks for an
        // occupation (handoff rule (d)).
        var (db, parsed) = await ArrangeAsync();

        var result = await CreateHandler(db).Handle(
            new GetCvSectionSuggestionsQuery(parsed.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result.HasOccupationPreference.ShouldBeFalse();
        result.Branschgrupp.ShouldBe("ovriga");
        result.Suggestions.ShouldNotBeEmpty("Övriga är en förstklassig rad, inte ett hål.");
    }

    [Fact]
    public async Task Handle_ShouldReportHasOccupationPreference_WhenAStatedOccupationResolvesToOvriga()
    {
        // State (2): she DID tell us — she is a byggnadsträhantverkare, and Bygg is one of the 17
        // fields with no specialised rule-table (the 62.1 % majority). Same suggestions as state
        // (1), but she must NOT be asked for her occupation again: asking a user for something she
        // already gave you is the product telling her it wasn't listening.
        //
        // If the two states were collapsed into one "is it Övriga?" boolean, this test and the one
        // above would be indistinguishable. That is precisely why the flag exists.
        var (db, parsed) = await ArrangeAsync(occupationGroups: [GroupByggnadstrahantverkare]);

        var result = await CreateHandler(db).Handle(
            new GetCvSectionSuggestionsQuery(parsed.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result.HasOccupationPreference.ShouldBeTrue();
        result.Branschgrupp.ShouldBe("ovriga");
    }

    // ───────────────────────────────────────────────────────────────────
    // The happy paths, against the REAL asset
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ShouldOfferLegitimationFirstAndNotOfferProjekt_ForAVardOccupation()
    {
        // The product promise of 8b.4a, end to end: an undersköterska is offered "Legitimation och
        // intyg" — and is NOT offered Projekt (design handoff §7, vård row).
        var (db, parsed) = await ArrangeAsync(occupationGroups: [GroupUnderskoterska]);

        var result = await CreateHandler(db).Handle(
            new GetCvSectionSuggestionsQuery(parsed.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result.Branschgrupp.ShouldBe("vard");
        result.HasOccupationPreference.ShouldBeTrue();
        result.Rationale.ShouldBe("Vanligt inom vård och omsorg");

        // Standard sections lead — the guide surfaces them differently from the merely-common ones.
        result.Suggestions[0].SectionId.ShouldBe("legitimation");
        result.Suggestions[0].IsStandard.ShouldBeTrue();
        result.Suggestions[0].Heading.ShouldBe("Legitimation och intyg");

        result.Suggestions.Select(s => s.SectionId).ShouldNotContain("projekt");
        result.Suggestions.Select(s => s.SectionId).ShouldBe(["legitimation", "kurser", "korkort"]);
    }

    [Fact]
    public async Task Handle_ShouldOfferProjektAsStandard_ForAnItOccupation()
    {
        var (db, parsed) = await ArrangeAsync(occupationGroups: [GroupSystemutvecklare]);

        var result = await CreateHandler(db).Handle(
            new GetCvSectionSuggestionsQuery(parsed.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result.Branschgrupp.ShouldBe("it");
        result.Suggestions[0].SectionId.ShouldBe("projekt");
        result.Suggestions[0].IsStandard.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_ShouldReturnOvriga_WhenTheStatedOccupationsSpanTwoBranschgrupper()
    {
        // The tie-break, through the handler: an IT group AND a vård group → refuse, do not guess.
        // The two rule-tables genuinely disagree (IT makes Projekt its standard section; vård does
        // not offer it at all), so there is no defensible merge.
        var (db, parsed) = await ArrangeAsync(
            occupationGroups: [GroupSystemutvecklare, GroupUnderskoterska]);

        var result = await CreateHandler(db).Handle(
            new GetCvSectionSuggestionsQuery(parsed.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result.Branschgrupp.ShouldBe("ovriga");
        result.HasOccupationPreference.ShouldBeTrue();
    }

    // ───────────────────────────────────────────────────────────────────
    // Handoff rule (a) — the file always wins
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ShouldNotReofferASection_WhenTheCvAlreadyHasItUnderADifferentSynonym()
    {
        // THE reason PR-1 had to create section identity. Her CV says "Kurser och intyg"; the vård
        // rule-table offers sectionId `kurser`. Those are the same thing — and ONLY the lexicon can
        // know that. Without the canonical id, the panel would cheerfully offer her a section she
        // is already looking at.
        var (db, parsed) = await ArrangeAsync(
            occupationGroups: [GroupUnderskoterska],
            sections: [Section("Kurser och intyg")]);

        var result = await CreateHandler(db).Handle(
            new GetCvSectionSuggestionsQuery(parsed.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result.Suggestions.Select(s => s.SectionId).ShouldNotContain("kurser");
        result.Suggestions.Select(s => s.SectionId).ShouldBe(["legitimation", "korkort"]);
    }

    [Fact]
    public async Task Handle_ShouldIgnoreAFreeSectionTheLexiconCannotResolve_WhenComputingPresence()
    {
        // A heading the lexicon does not own ("Mina husdjur") resolves to null. It must remove
        // nothing from the suggestions — and must not throw.
        var (db, parsed) = await ArrangeAsync(
            occupationGroups: [GroupUnderskoterska],
            sections: [Section("Mina husdjur")]);

        var result = await CreateHandler(db).Handle(
            new GetCvSectionSuggestionsQuery(parsed.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result.Suggestions.Select(s => s.SectionId).ShouldBe(["legitimation", "kurser", "korkort"]);
    }

    [Fact]
    public async Task Handle_ShouldReturnEmptySuggestions_WhenTheCvAlreadyHasEverythingOffered()
    {
        // An honest "nothing to add" — never a fabricated row to keep the panel looking busy.
        var (db, parsed) = await ArrangeAsync(
            occupationGroups: [GroupUnderskoterska],
            sections: [Section("Legitimation och intyg"), Section("Kurser"), Section("Körkort")]);

        var result = await CreateHandler(db).Handle(
            new GetCvSectionSuggestionsQuery(parsed.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result.Suggestions.ShouldBeEmpty();
        result.Branschgrupp.ShouldBe("vard");
    }

    // ───────────────────────────────────────────────────────────────────
    // Ownership
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ShouldReturnNullAndLogCrossUserAttempt_WhenTheResumeBelongsToSomeoneElse()
    {
        var content = Content();
        var db = NewDb(content);
        var otherParsed = await SeedAsync(db, Guid.NewGuid(), content);
        await SeedAsync(db, _userId, content);   // the caller exists but does not own otherParsed

        var result = await CreateHandler(db).Handle(
            new GetCvSectionSuggestionsQuery(otherParsed.Id.Value), CancellationToken.None);

        result.ShouldBeNull();
        _failedAccessLogger.Received(1).LogCrossUserAttempt(
            "ParsedResume", otherParsed.Id.Value, _userId, "GetCvSectionSuggestions");
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenTheParsedResumeDoesNotExist()
    {
        var content = Content();
        var db = NewDb(content);
        await SeedAsync(db, _userId, content);

        var result = await CreateHandler(db).Handle(
            new GetCvSectionSuggestionsQuery(Guid.NewGuid()), CancellationToken.None);

        result.ShouldBeNull();
        _failedAccessLogger.DidNotReceiveWithAnyArgs()
            .LogCrossUserAttempt(default!, default, default, default!);
    }

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenThereIsNoAuthenticatedUser()
    {
        var (db, parsed) = await ArrangeAsync();
        _currentUser.UserId.Returns((Guid?)null);

        var result = await CreateHandler(db).Handle(
            new GetCvSectionSuggestionsQuery(parsed.Id.Value), CancellationToken.None);

        result.ShouldBeNull();
    }

    // ───────────────────────────────────────────────────────────────────
    // The CONFIRMED axis (ADR 0040) — and the drift case
    // ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ShouldUseTheConfirmedOccupation_WhenTheCvProposesADifferentOne()
    {
        // The axis test, made ADVERSARIAL. Until now the slice only used the confirmed axis by
        // ACCIDENT: no test ever planted an occupation proposal, so a developer who re-pointed the
        // handler at ParsedResume.OccupationProposals would have seen the suite stay green (the
        // proposals were always empty), or "fixed" a red test by seeding them.
        //
        // Here the two axes DISAGREE on purpose: the CV proposes an IT occupation; she has
        // confirmed a vård one in her match settings. The answer must be vård.
        //
        // Why this matters beyond tidiness: ProposedOccupation is unconfirmed by contract and drops
        // MatchKind, so an exact hit and a stemmed guess are indistinguishable on the artifact.
        // Driving a rule-table off it would silently promote "we think you might be a developer"
        // into "here are a developer's sections" — for a nurse.
        var (db, parsed) = await ArrangeAsync(
            occupationGroups: [GroupUnderskoterska],
            occupationProposals:
            [
                new ProposedOccupation(GroupSystemutvecklare, "Systemutvecklare", "titel", 5),
            ]);

        var result = await CreateHandler(db).Handle(
            new GetCvSectionSuggestionsQuery(parsed.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result.Branschgrupp.ShouldBe("vard",
            "yrket kommer från matchningsinställningarna (bekräftat), aldrig från CV:ts gissning.");
        result.Suggestions.Select(s => s.SectionId).ShouldContain("legitimation");
        result.Suggestions.Select(s => s.SectionId).ShouldNotContain("projekt");
    }

    [Fact]
    public async Task Handle_ShouldStillNotAskForAnOccupation_WhenTheStatedGroupIsUnknownToTheTaxonomy()
    {
        // Taxonomy drift: she stated an occupation, but the snapshot no longer carries that ssyk-4
        // group, so it resolves to nothing → Övriga. She must STILL not be asked again:
        // hasOccupationPreference stays true. She answered. That we can no longer use her answer is
        // OUR failure, not a reason to make her repeat herself.
        //
        // The behaviour was previously unpinned, and the DTO doc claimed the true+ovriga case meant
        // "her occupation is one of the 17" — which is false here. Both are now corrected.
        var (db, parsed) = await ArrangeAsync(occupationGroups: ["ZZZZ_borttagen_grupp"]);

        var result = await CreateHandler(db).Handle(
            new GetCvSectionSuggestionsQuery(parsed.Id.Value), CancellationToken.None);

        result.ShouldNotBeNull();
        result.HasOccupationPreference.ShouldBeTrue();
        result.Branschgrupp.ShouldBe("ovriga");
        result.Suggestions.ShouldNotBeEmpty("Övriga bär sina egna förslag även vid drift.");
    }
}
