using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Applications;

// RÖD svit (TDD — implementation följer). Spec: issue #315 / ADR 0086 D3 +
// GDPR Art. 5(1)(c) — på en TERMINAL transition (Accepted/Rejected/Withdrawn)
// minimeras AdSnapshot:ets Description (droppas), medan stats-/identitetsmetadata
// (titel/företag/ort/url/datum) BEHÅLLS. Icke-terminala transitions lämnar
// snapshot:et orört. Idempotent + null-säkert (AdSnapshot?.WithoutDescription()).
// Ghosted är INTE terminal (reaktiverbar).
public class ApplicationAdSnapshotRetentionTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly JobSeekerId ValidJobSeekerId = new(Guid.NewGuid());
    private static readonly JobAdId ValidJobAdId = new(Guid.NewGuid());

    private static readonly DateTimeOffset PublishedAt =
        new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAt =
        new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private const string MunicipalityConceptId = "1gEC_kvM_TXK";

    private static AdSnapshot SnapshotWithDescription() =>
        AdSnapshot.Capture(
            title: "Backend-utvecklare",
            company: "Klarna",
            municipalityConceptId: MunicipalityConceptId,
            url: "https://example.com/jobb/1",
            source: "Platsbanken",
            publishedAt: PublishedAt,
            expiresAt: ExpiresAt,
            description: "En lång beskrivning av tjänsten.",
            capturedAt: Clock.UtcNow);

    private static Application FromJobAdSubmitted()
    {
        var app = Application.CreateFromJobAd(
            ValidJobSeekerId, ValidJobAdId, SnapshotWithDescription(), null, Clock).Value;
        app.TransitionTo(ApplicationStatus.Submitted, Clock);
        return app;
    }

    private static void AssertMetadataRetained(AdSnapshot snapshot)
    {
        snapshot.Title.ShouldBe("Backend-utvecklare");
        snapshot.Company.ShouldBe("Klarna");
        snapshot.MunicipalityConceptId.ShouldBe(MunicipalityConceptId);
        snapshot.Url.ShouldBe("https://example.com/jobb/1");
        snapshot.Source.ShouldBe("Platsbanken");
        snapshot.PublishedAt.ShouldBe(PublishedAt);
        snapshot.ExpiresAt.ShouldBe(ExpiresAt);
        snapshot.CapturedAt.ShouldBe(Clock.UtcNow);
    }

    // ---------------------------------------------------------------
    // Submit (icke-terminal) lämnar Description orörd
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_Submitted_LeavesDescriptionIntact()
    {
        var app = Application.CreateFromJobAd(
            ValidJobSeekerId, ValidJobAdId, SnapshotWithDescription(), null, Clock).Value;

        app.TransitionTo(ApplicationStatus.Submitted, Clock);

        app.AdSnapshot.ShouldNotBeNull();
        app.AdSnapshot.Description.ShouldBe("En lång beskrivning av tjänsten.");
    }

    [Fact]
    public void TransitionTo_Acknowledged_NonTerminal_LeavesDescriptionIntact()
    {
        var app = FromJobAdSubmitted();

        app.TransitionTo(ApplicationStatus.Acknowledged, Clock);

        app.AdSnapshot.ShouldNotBeNull();
        app.AdSnapshot.Description.ShouldBe("En lång beskrivning av tjänsten.");
    }

    // ---------------------------------------------------------------
    // Terminal Rejected (Submitted→Rejected) minimerar Description
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_Rejected_MinimisesDescriptionButRetainsMetadata()
    {
        var app = FromJobAdSubmitted();

        app.TransitionTo(ApplicationStatus.Rejected, Clock);

        app.AdSnapshot.ShouldNotBeNull();
        app.AdSnapshot.Description.ShouldBeNull();
        AssertMetadataRetained(app.AdSnapshot);
    }

    // ---------------------------------------------------------------
    // Terminal Withdrawn (Submitted→Withdrawn) minimerar Description
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_Withdrawn_MinimisesDescriptionButRetainsMetadata()
    {
        var app = FromJobAdSubmitted();

        app.TransitionTo(ApplicationStatus.Withdrawn, Clock);

        app.AdSnapshot.ShouldNotBeNull();
        app.AdSnapshot.Description.ShouldBeNull();
        AssertMetadataRetained(app.AdSnapshot);
    }

    // ---------------------------------------------------------------
    // Terminal Accepted (full väg) minimerar Description
    // Submitted→Acknowledged→InterviewScheduled→Interviewing→OfferReceived→Accepted
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_Accepted_ViaFullWalk_MinimisesDescriptionButRetainsMetadata()
    {
        var app = FromJobAdSubmitted();
        app.TransitionTo(ApplicationStatus.Acknowledged, Clock);
        app.TransitionTo(ApplicationStatus.InterviewScheduled, Clock);
        app.TransitionTo(ApplicationStatus.Interviewing, Clock);
        app.TransitionTo(ApplicationStatus.OfferReceived, Clock);

        // Sista, terminala steget.
        var result = app.TransitionTo(ApplicationStatus.Accepted, Clock);

        result.IsSuccess.ShouldBeTrue();
        app.Status.ShouldBe(ApplicationStatus.Accepted);
        app.AdSnapshot.ShouldNotBeNull();
        app.AdSnapshot.Description.ShouldBeNull();
        AssertMetadataRetained(app.AdSnapshot);
    }

    // ---------------------------------------------------------------
    // Description intakt FÖRE terminal — minimeringen sker först vid terminal
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_BeforeTerminal_KeepsDescription_ThenMinimisesAtTerminal()
    {
        var app = FromJobAdSubmitted();
        app.TransitionTo(ApplicationStatus.Acknowledged, Clock);

        // Mellansteg: Description finns kvar.
        app.AdSnapshot!.Description.ShouldBe("En lång beskrivning av tjänsten.");

        app.TransitionTo(ApplicationStatus.Rejected, Clock);

        app.AdSnapshot!.Description.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // Idempotent — terminal när Description redan null stannar null, kastar ej
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_Terminal_WhenDescriptionAlreadyNull_StaysNullAndDoesNotThrow()
    {
        // Snapshot utan Description (t.ex. JobAd utan beskrivning). En terminal
        // transition får inte kasta och Description förblir null.
        var snapshotNoDescription = AdSnapshot.Capture(
            "Backend-utvecklare", "Klarna", MunicipalityConceptId,
            "https://example.com/jobb/1", "Platsbanken",
            PublishedAt, ExpiresAt, description: null, Clock.UtcNow);
        var app = Application.CreateFromJobAd(
            ValidJobSeekerId, ValidJobAdId, snapshotNoDescription, null, Clock).Value;
        app.TransitionTo(ApplicationStatus.Submitted, Clock);

        var result = app.TransitionTo(ApplicationStatus.Withdrawn, Clock);

        result.IsSuccess.ShouldBeTrue();
        app.AdSnapshot.ShouldNotBeNull();
        app.AdSnapshot.Description.ShouldBeNull();
    }

    // ---------------------------------------------------------------
    // Null-säkert — en snapshot-lös ansökan (Application.Create) klarar terminal
    // ---------------------------------------------------------------

    [Fact]
    public void TransitionTo_Terminal_WithoutAdSnapshot_DoesNotThrow()
    {
        // Application.Create sätter aldrig ett snapshot. AdSnapshot?. ska vara
        // null-säkert på terminal transition.
        var app = Application.Create(ValidJobSeekerId, ValidJobAdId, null, null, Clock).Value;
        app.TransitionTo(ApplicationStatus.Submitted, Clock);

        var result = app.TransitionTo(ApplicationStatus.Rejected, Clock);

        result.IsSuccess.ShouldBeTrue();
        app.AdSnapshot.ShouldBeNull();
    }
}
