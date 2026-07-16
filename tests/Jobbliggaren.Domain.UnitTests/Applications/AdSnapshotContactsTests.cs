using Jobbliggaren.Domain.Applications;
using Jobbliggaren.Domain.JobAds;
using Jobbliggaren.Domain.JobSeekers;
using Jobbliggaren.Domain.UnitTests.JobAds;
using Shouldly;

namespace Jobbliggaren.Domain.UnitTests.Applications;

/// <summary>
/// #842 Tier A on the frozen snapshot: capture carries the post-scrub contacts, the terminal
/// transition drops them WITH the body (R4(b) — the follow-up purpose is spent), and the surgical
/// Art. 17 arm removes ONLY the contacts, leaving the applicant's record intact (b1 §4.4, T2 CTO).
/// </summary>
public class AdSnapshotContactsTests
{
    private static readonly FakeDateTimeProvider Clock = FakeDateTimeProvider.Default;
    private static readonly JobSeekerId SeekerId = new(Guid.NewGuid());
    private static readonly JobAdId AdId = new(Guid.NewGuid());

    private static AdContacts SomeContacts() =>
        AdContacts.From(
            [AdContact.TryCreate("Anna Karlsson", "Rekryterare", "anna@acme.se", null, AdContactOrigin.Declared)],
            []);

    private static AdSnapshot Snapshot(AdContacts? contacts) =>
        AdSnapshot.Capture(
            title: "Backend-utvecklare",
            company: "Acme AB",
            municipalityConceptId: null,
            url: "https://arbetsformedlingen.se/platsbanken/annonser/1",
            source: "Platsbanken",
            publishedAt: Clock.UtcNow.AddDays(-7),
            expiresAt: null,
            description: "Beskrivning.",
            contacts: contacts,
            capturedAt: Clock.UtcNow);

    [Fact]
    public void WithoutAdBody_drops_description_AND_contacts()
    {
        var minimised = Snapshot(SomeContacts()).WithoutAdBody();

        minimised.Description.ShouldBeNull();
        minimised.Contacts.ShouldBeNull();
        minimised.Title.ShouldBe("Backend-utvecklare"); // identity metadata survives
        minimised.Company.ShouldBe("Acme AB");
    }

    [Fact]
    public void WithoutAdBody_is_idempotent_and_returns_self_when_already_minimised()
    {
        var minimised = Snapshot(null).WithoutAdBody().WithoutAdBody();

        var again = minimised.WithoutAdBody();

        again.ShouldBeSameAs(minimised);
    }

    [Fact]
    public void WithoutContacts_is_surgical_and_keeps_the_applicants_record()
    {
        var surgical = Snapshot(SomeContacts()).WithoutContacts();

        surgical.Contacts.ShouldBeNull();
        surgical.Description.ShouldBe("Beskrivning."); // her evidence is untouched
        surgical.Title.ShouldBe("Backend-utvecklare");
    }

    [Fact]
    public void EraseAdSnapshotContacts_clears_through_the_aggregate_and_is_idempotent()
    {
        var application = Application.CreateFromJobAd(
            SeekerId, AdId, Snapshot(SomeContacts()),
            coverLetter: null, Clock).Value;

        application.EraseAdSnapshotContacts();
        application.EraseAdSnapshotContacts();

        application.AdSnapshot!.Contacts.ShouldBeNull();
        application.AdSnapshot.Description.ShouldBe("Beskrivning.");
    }

    [Fact]
    public void A_terminal_transition_drops_the_contacts_with_the_body()
    {
        var application = Application.CreateFromJobAd(
            SeekerId, AdId, Snapshot(SomeContacts()),
            coverLetter: null, Clock).Value;

        application.TransitionTo(ApplicationStatus.Submitted, Clock).IsSuccess.ShouldBeTrue();
        application.AdSnapshot!.Contacts.ShouldNotBeNull(); // Submitted is not terminal

        application.TransitionTo(ApplicationStatus.Rejected, Clock).IsSuccess.ShouldBeTrue();

        application.AdSnapshot!.Contacts.ShouldBeNull();
        application.AdSnapshot.Description.ShouldBeNull();
    }
}
