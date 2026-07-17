using Jobbliggaren.Domain.Applications;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Applications;

// Spec: issue #315 / ADR 0086 (D4 final ruling: concept-id-at-read) — frozen
// ad-text snapshot owned VO. Speglar ManualPostingTests.cs-stilen. Till skillnad
// mot ManualPosting validerar AdSnapshot INTE (captured data, ej user-input,
// dotnet-architect M1): Capture returnerar VO:t direkt (inget Result).
// Municipality fryses som RÅ concept-id (MunicipalityConceptId) — namn-resolvering
// sker på läs-vägen (GetApplicationByIdQueryHandler), inte här.
public class AdSnapshotTests
{
    private static readonly DateTimeOffset PublishedAt =
        new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAt =
        new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 4, 19, 12, 0, 0, TimeSpan.Zero);

    // Concept-id-formad sträng (JobTechs municipality-concept-id), frusen RÅ.
    private const string MunicipalityConceptId = "1gEC_kvM_TXK";

    private static AdSnapshot FullSnapshot() =>
        AdSnapshot.Capture(
            title: "Backend-utvecklare",
            company: "Klarna",
            municipalityConceptId: MunicipalityConceptId,
            url: "https://example.com/jobb/1",
            source: "Platsbanken",
            publishedAt: PublishedAt,
            expiresAt: ExpiresAt,
            description: "En lång beskrivning av tjänsten.",
            capturedAt: CapturedAt, contacts: null);

    // ---------------------------------------------------------------
    // Capture — sätter varje fält (ingen validering, inget Result)
    // ---------------------------------------------------------------

    [Fact]
    public void Capture_SetsEveryField()
    {
        var snapshot = FullSnapshot();

        snapshot.Title.ShouldBe("Backend-utvecklare");
        snapshot.Company.ShouldBe("Klarna");
        snapshot.MunicipalityConceptId.ShouldBe(MunicipalityConceptId);
        snapshot.Url.ShouldBe("https://example.com/jobb/1");
        snapshot.Source.ShouldBe("Platsbanken");
        snapshot.PublishedAt.ShouldBe(PublishedAt);
        snapshot.ExpiresAt.ShouldBe(ExpiresAt);
        snapshot.Description.ShouldBe("En lång beskrivning av tjänsten.");
        snapshot.CapturedAt.ShouldBe(CapturedAt);
    }

    [Fact]
    public void Capture_AllowsNullOptionalFields()
    {
        // MunicipalityConceptId/Url/ExpiresAt/Description är nullable (graceful
        // degradation, ADR 0086 D4/D3). Capture utför ingen validering → null OK.
        var snapshot = AdSnapshot.Capture(
            title: "Backend-utvecklare",
            company: "Klarna",
            municipalityConceptId: null,
            url: null,
            source: "Platsbanken",
            publishedAt: PublishedAt,
            expiresAt: null,
            description: null,
            capturedAt: CapturedAt, contacts: null);

        snapshot.Title.ShouldBe("Backend-utvecklare");
        snapshot.Company.ShouldBe("Klarna");
        snapshot.MunicipalityConceptId.ShouldBeNull();
        snapshot.Url.ShouldBeNull();
        snapshot.ExpiresAt.ShouldBeNull();
        snapshot.Description.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // WithoutDescription — droppar Description, behåller övriga 8 fält
    // (retention / GDPR data-minimisation, ADR 0086 D3)
    // ---------------------------------------------------------------

    [Fact]
    public void WithoutDescription_DropsDescription()
    {
        var snapshot = FullSnapshot();

        var minimised = snapshot.WithoutAdBody();

        minimised.Description.ShouldBeNull();
    }

    [Fact]
    public void WithoutDescription_KeepsTheOtherEightFields()
    {
        var snapshot = FullSnapshot();

        var minimised = snapshot.WithoutAdBody();

        minimised.Title.ShouldBe("Backend-utvecklare");
        minimised.Company.ShouldBe("Klarna");
        minimised.MunicipalityConceptId.ShouldBe(MunicipalityConceptId);
        minimised.Url.ShouldBe("https://example.com/jobb/1");
        minimised.Source.ShouldBe("Platsbanken");
        minimised.PublishedAt.ShouldBe(PublishedAt);
        minimised.ExpiresAt.ShouldBe(ExpiresAt);
        minimised.CapturedAt.ShouldBe(CapturedAt);
    }

    [Fact]
    public void WithoutDescription_WhenDescriptionAlreadyNull_ReturnsSameInstance()
    {
        // Idempotent: ett redan-minimerat snapshot returnerar SIG SJÄLVT
        // (referenslikhet — produktionen returnerar `this`).
        var snapshot = AdSnapshot.Capture(
            "Backend-utvecklare", "Klarna", MunicipalityConceptId,
            "https://example.com/jobb/1", "Platsbanken",
            PublishedAt, ExpiresAt, description: null, contacts: null, CapturedAt);

        var result = snapshot.WithoutAdBody();

        result.ShouldBeSameAs(snapshot);
    }

    [Fact]
    public void WithoutDescription_IsIdempotent()
    {
        var snapshot = FullSnapshot();

        var once = snapshot.WithoutAdBody();
        var twice = once.WithoutAdBody();

        twice.Description.ShouldBeNull();
        twice.ShouldBe(once);
    }

    // ---------------------------------------------------------------
    // Record value-equality över alla 9 fält
    // ---------------------------------------------------------------

    [Fact]
    public void Equality_WithSameFieldValues_AreEqual()
    {
        var a = FullSnapshot();
        var b = FullSnapshot();

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void Equality_WithDifferentTitle_AreNotEqual()
    {
        var a = FullSnapshot();
        var b = AdSnapshot.Capture(
            "Frontend-utvecklare", "Klarna", MunicipalityConceptId,
            "https://example.com/jobb/1", "Platsbanken",
            PublishedAt, ExpiresAt, "En lång beskrivning av tjänsten.", null, CapturedAt);

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Equality_WithDifferentMunicipalityConceptId_AreNotEqual()
    {
        // Säkerställer att MunicipalityConceptId ingår i värde-likheten.
        var a = FullSnapshot();
        var b = AdSnapshot.Capture(
            "Backend-utvecklare", "Klarna", "ZZZZ_zzz_ZZZ",
            "https://example.com/jobb/1", "Platsbanken",
            PublishedAt, ExpiresAt, "En lång beskrivning av tjänsten.", null, CapturedAt);

        a.ShouldNotBe(b);
    }

    [Fact]
    public void Equality_WithDifferentDescription_AreNotEqual()
    {
        // Säkerställer att Description ingår i värde-likheten (annars skulle
        // WithoutDescription-kopia felaktigt vara lika med originalet).
        var a = FullSnapshot();
        var b = a.WithoutAdBody();

        a.ShouldNotBe(b);
    }
}
