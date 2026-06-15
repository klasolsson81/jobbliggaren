using Jobbliggaren.Application.JobAds.Queries.GetJobAdExtractedTerms;
using Jobbliggaren.Application.UnitTests.Common;
using Jobbliggaren.Domain.Common;
using Jobbliggaren.Domain.JobAds;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Jobbliggaren.Application.UnitTests.JobAds.Queries.GetJobAdExtractedTerms;

/// <summary>
/// Fas 4 STEG 4 (F4-4) — <see cref="GetJobAdExtractedTermsQueryHandler"/> reads a
/// job ad's PERSISTED <c>ExtractedTerms</c> (no extraction logic here) and maps the
/// Domain value object to the transport DTO at the boundary (CLAUDE.md §2.3):
/// <list type="bullet">
/// <item><c>Kind</c>/<c>Source</c> stringified (API decoupled from enum ordinals)</item>
/// <item>cited evidence (<c>MatchedOn</c>) + <c>ConceptId</c> preserved</item>
/// <item><c>null</c> when the ad does not exist (→ 404 at the endpoint)</item>
/// <item>empty term list when the ad exists but is not-yet-extracted</item>
/// </list>
/// Uses the real in-memory <see cref="TestAppDbContextFactory"/> (parity
/// UpsertExternalJobAdCommandHandlerTests) — the handler's <c>.Select(...)</c>
/// projection over IQueryable is exercised end-to-end against a real provider, not
/// a brittle NSubstitute IQueryable stub.
///
/// RED until ExtractedTerms/ExtractedTerm(+enums) + JobAd.SetExtractedTerms +
/// GetJobAdExtractedTermsQuery(+Handler) + JobAdExtractionDto/ExtractedTermDto ship.
/// </summary>
public class GetJobAdExtractedTermsQueryHandlerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static JobAd NewJobAd(string title = "Backend-utvecklare", string description = "Beskrivning")
    {
        var company = Company.Create("Klarna").Value;
        var external = ExternalReference.Create(JobSource.Platsbanken, Guid.NewGuid().ToString("N")).Value;
        IDateTimeProvider clock = new FakeDateTimeProvider(Now);
        return JobAd.Import(
            title, company, description, "https://example.com/jobb/1",
            external, "{\"id\":\"x\"}", Now.AddDays(-1), Now.AddDays(30), clock).Value;
    }

    private static async Task<JobAd> SeedAsync(
        Jobbliggaren.Infrastructure.Persistence.AppDbContext db, JobAd jobAd, CancellationToken ct)
    {
        db.JobAds.Add(jobAd);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return jobAd;
    }

    // ===============================================================
    // Mapping a persisted (non-empty) ExtractedTerms VO → DTO
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldMapPersistedTermsToDto_WhenAdHasExtraction()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var jobAd = NewJobAd();
        jobAd.SetExtractedTerms(ExtractedTerms.From(
        [
            new ExtractedTerm(
                Lexeme: "1TC7_x8s_V7V", Display: "JavaScript",
                Kind: ExtractedTermKind.Skill, Source: ExtractedTermSource.Title,
                MatchedOn: "JavaScript", ConceptId: "1TC7_x8s_V7V", Weight: 3),
            new ExtractedTerm(
                Lexeme: "samordn", Display: "samordnare",
                Kind: ExtractedTermKind.Keyword, Source: ExtractedTermSource.Description,
                MatchedOn: "samordnare", ConceptId: null, Weight: 2),
        ]));
        await SeedAsync(db, jobAd, ct);
        var sut = new GetJobAdExtractedTermsQueryHandler(db);

        var dto = await sut.Handle(new GetJobAdExtractedTermsQuery(jobAd.Id.Value), ct);

        dto.ShouldNotBeNull();
        dto.JobAdId.ShouldBe(jobAd.Id.Value);
        dto.Terms.Count.ShouldBe(2);

        // Skill sorts before Keyword (Domain normalization preserved through the map).
        var skill = dto.Terms[0];
        skill.Kind.ShouldBe("Skill", "Kind ska vara stringifierad (frikopplad från enum-ordinal).");
        skill.Source.ShouldBe("Title");
        skill.ConceptId.ShouldBe("1TC7_x8s_V7V");
        skill.Lexeme.ShouldBe("1TC7_x8s_V7V");
        skill.MatchedOn.ShouldBe("JavaScript", "cited evidence ska bevaras genom mappningen.");
        skill.Display.ShouldBe("JavaScript");
        skill.Weight.ShouldBe(3);

        var keyword = dto.Terms[1];
        keyword.Kind.ShouldBe("Keyword");
        keyword.Source.ShouldBe("Description");
        keyword.ConceptId.ShouldBeNull("Keyword bär aldrig ConceptId.");
        keyword.MatchedOn.ShouldBe("samordnare");
    }

    [Fact]
    public async Task Handle_ShouldStringifyEnums_NotEmitOrdinals()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var jobAd = NewJobAd();
        jobAd.SetExtractedTerms(ExtractedTerms.From(
        [
            new ExtractedTerm("ekonomi", "ekonomi", ExtractedTermKind.Keyword,
                ExtractedTermSource.Description, "ekonomi", null, 1),
        ]));
        await SeedAsync(db, jobAd, ct);
        var sut = new GetJobAdExtractedTermsQueryHandler(db);

        var dto = await sut.Handle(new GetJobAdExtractedTermsQuery(jobAd.Id.Value), ct);

        dto.ShouldNotBeNull();
        var term = dto.Terms.ShouldHaveSingleItem();
        // The contract is the enum NAME, never "0"/"1".
        term.Kind.ShouldBe(nameof(ExtractedTermKind.Keyword));
        term.Source.ShouldBe(nameof(ExtractedTermSource.Description));
    }

    // ===============================================================
    // Ad exists but not yet extracted → empty term list (not null)
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnEmptyTermList_WhenAdNotYetExtracted()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var jobAd = NewJobAd(); // ExtractedTerms left NULL — never extracted
        await SeedAsync(db, jobAd, ct);
        var sut = new GetJobAdExtractedTermsQueryHandler(db);

        var dto = await sut.Handle(new GetJobAdExtractedTermsQuery(jobAd.Id.Value), ct);

        dto.ShouldNotBeNull("annonsen finns → ej null (null = 404).");
        dto.JobAdId.ShouldBe(jobAd.Id.Value);
        dto.Terms.ShouldBeEmpty("inte-extraherad annons ger tom term-lista, inte null DTO.");
    }

    [Fact]
    public async Task Handle_ShouldReturnEmptyTermList_WhenExtractionIsExplicitlyEmpty()
    {
        // An ad whose text resolved to nothing is extracted-to-Empty (distinct from
        // never-extracted at the DB level, but the read DTO is the same shape).
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var jobAd = NewJobAd();
        jobAd.SetExtractedTerms(ExtractedTerms.Empty);
        await SeedAsync(db, jobAd, ct);
        var sut = new GetJobAdExtractedTermsQueryHandler(db);

        var dto = await sut.Handle(new GetJobAdExtractedTermsQuery(jobAd.Id.Value), ct);

        dto.ShouldNotBeNull();
        dto.Terms.ShouldBeEmpty();
    }

    // ===============================================================
    // Ad absent → null (→ 404 at the endpoint)
    // ===============================================================

    [Fact]
    public async Task Handle_ShouldReturnNull_WhenAdDoesNotExist()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();
        var sut = new GetJobAdExtractedTermsQueryHandler(db);

        var dto = await sut.Handle(new GetJobAdExtractedTermsQuery(Guid.NewGuid()), ct);

        dto.ShouldBeNull("saknad annons ⇒ null ⇒ 404 vid endpoint (paritet GetJobAdQuery).");
    }

    [Fact]
    public async Task Handle_ShouldReadOnlyTheRequestedAd_WhenMultipleAdsExist()
    {
        // Guards the Where(j => j.Id == new JobAdId(query.JobAdId)) predicate.
        var ct = TestContext.Current.CancellationToken;
        var db = TestAppDbContextFactory.Create();

        var target = NewJobAd("Mjukvaruutvecklare");
        target.SetExtractedTerms(ExtractedTerms.From(
        [
            new ExtractedTerm("dev", "utvecklare", ExtractedTermKind.Keyword,
                ExtractedTermSource.Title, "utvecklare", null, 1),
        ]));
        var other = NewJobAd("Ekonom");
        other.SetExtractedTerms(ExtractedTerms.From(
        [
            new ExtractedTerm("ekonom", "ekonom", ExtractedTermKind.Keyword,
                ExtractedTermSource.Title, "ekonom", null, 1),
        ]));
        db.JobAds.AddRange(target, other);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        var sut = new GetJobAdExtractedTermsQueryHandler(db);

        var dto = await sut.Handle(new GetJobAdExtractedTermsQuery(target.Id.Value), ct);

        dto.ShouldNotBeNull();
        dto.JobAdId.ShouldBe(target.Id.Value);
        dto.Terms.ShouldHaveSingleItem().Lexeme.ShouldBe("dev");
    }
}
